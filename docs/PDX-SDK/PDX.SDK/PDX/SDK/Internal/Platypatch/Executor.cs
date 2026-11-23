using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Exceptions;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Events;
using PDX.SDK.Internal.Events.Download;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Platypatch.Results;
using PDX.SDK.Internal.Util;
using PDX.SDK.Internal.Util.Progress;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal class Executor : IExecutor
{
	private readonly IRemoteRepositoryFactory _remoteRepositoryFactory;

	private readonly ILocalRepoManagerFactory _localRepoManagerFactory;

	private readonly ILogger _logger;

	private readonly IFileIO _fileIo;

	private readonly IPatchManagerFactory _patchManagerFactory;

	private readonly IPathFactory _pathFactory;

	private readonly IInternalEventHub _internalEventHub;

	private const int TotalInstallationProgressChunk = 8;

	[Preserve]
	public Executor(IRemoteRepositoryFactory remoteRepositoryFactory, ILocalRepoManagerFactory localRepoManagerFactory, IFileIO fileIo, IPatchManagerFactory patchManagerFactory, IPathFactory pathFactory, IInternalEventHub internalEventHub = null, ILogger logger = null)
	{
		_remoteRepositoryFactory = remoteRepositoryFactory;
		_localRepoManagerFactory = localRepoManagerFactory;
		_fileIo = fileIo;
		_patchManagerFactory = patchManagerFactory;
		_pathFactory = pathFactory;
		_internalEventHub = internalEventHub;
		_logger = logger;
	}

	public virtual async Task<RepositoryMetaDataResult> GetRepositoryMetaData(string repositoryName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		try
		{
			RepoMetaData repositoryMetaData = await (await _remoteRepositoryFactory.Create(repositoryName)).GetRepositoryMetaData(fd);
			return new RepositoryMetaDataResult
			{
				Success = true,
				RepositoryMetaData = repositoryMetaData
			};
		}
		catch (ExceptionWithSubCategory exceptionWithSubCategory)
		{
			return exceptionWithSubCategory.ToResult().CastTo<RepositoryMetaDataResult>();
		}
		catch (Exception ex)
		{
			_logger?.Log("Error getting repository metadata for repository " + repositoryName + ", got exception: " + ex.Message, LogLevel.L3_Error, fd);
			InternalError internalError = ((ex is PlatypatchException) ? InternalError.PlatypatchError : InternalError.InternalError);
			return new RepositoryMetaDataResult
			{
				Success = false,
				Error = new Error(internalError, ex.Message, "")
			};
		}
	}

	public virtual async Task<PatchSizeResult> CalculateSizeOfPatch(RepoMetaData repositoryMetadata, string appName, string absoluteDestination, string versionToInstall, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		try
		{
			return await _localRepoManagerFactory.Create(absoluteDestination, appName, versionToInstall).CalculateSizeOfPatch(repositoryMetadata, versionToInstall, fd);
		}
		catch (ExceptionWithSubCategory exceptionWithSubCategory)
		{
			return exceptionWithSubCategory.ToResult().CastTo<PatchSizeResult>();
		}
		catch (Exception ex)
		{
			_logger?.Log("Error calculating size of patch for version " + versionToInstall + " in destination folder " + absoluteDestination + ", got exception: " + ex.Message, LogLevel.L3_Error, fd);
			InternalError internalError = ((ex is PlatypatchException) ? InternalError.PlatypatchError : InternalError.InternalError);
			return new PatchSizeResult
			{
				Success = false,
				Error = new Error(internalError, ex.Message, "")
			};
		}
	}

	public virtual async Task<Result> InstallToFolder(RepoMetaData repositoryMetadata, string repositoryName, string appName, string absoluteDestination, string version = null, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		_logger?.Log("Install " + appName + " to folder " + absoluteDestination, LogLevel.L0_Info, fd);
		InstallStage currentStage = InstallStage.PreStepsStarted;
		string patchZipFilePath = "";
		try
		{
			if (progressController != null)
			{
				progressController.ProgressChanged += OnProgressChanged;
			}
			IRemoteRepository remoteRepository = await _remoteRepositoryFactory.Create(repositoryName);
			ISubProgressChunk overallInstallProgress = progressController?.GetSubProcessChunk(8uL);
			if (version == null)
			{
				version = repositoryMetadata.Content.VersionOrder.Last();
			}
			ILocalRepoManager localRepoManager = _localRepoManagerFactory.Create(absoluteDestination, appName, version);
			OperationResult<string> operationResult = await localRepoManager.GetInstalledVersion(fd);
			if (!operationResult.Status.Success)
			{
				return operationResult.Status.ToResult();
			}
			string text = operationResult.Result ?? "-1";
			if (text == version)
			{
				_logger?.Log("Install " + appName + " cancelled, version " + version + " already exists", LogLevel.L2_Warning, fd);
				return Result.ResultSuccessful();
			}
			overallInstallProgress?.RecordProgress(1uL);
			if (!IsValidPatchStep(repositoryMetadata, text, version))
			{
				if (!(text != "-1") || repositoryMetadata.Content?.Versions?.ContainsKey(version) != true)
				{
					Result result = new Result();
					result.Success = false;
					result.Error = new Error(InternalError.InternalError, "Going from version " + text + " to version " + version + " is not supported", "");
					return result;
				}
				Result result2 = await Uninstall(absoluteDestination, appName, clean: false, fd);
				if (!result2.Success)
				{
					return result2;
				}
				text = "-1";
			}
			currentStage = InstallStage.PatchDownloadStarted;
			overallInstallProgress?.RecordProgress(1uL);
			DownloadPatchZipResult downloadPatchZipResult = await DownloadPatchZip(remoteRepository, repositoryName, version, text, progressController, doNotSetTransferStateToSuccessful, fd);
			if (!downloadPatchZipResult.Success)
			{
				await CleanupTemporaryFiles(downloadPatchZipResult.ZipFilePath);
				return downloadPatchZipResult;
			}
			patchZipFilePath = downloadPatchZipResult.ZipFilePath;
			currentStage = InstallStage.PatchDownloadCompleted;
			overallInstallProgress?.RecordProgress(1uL);
			FolderManifest completeManifest = await remoteRepository.GetCompleteManifest(version, fd);
			Result saveCompleteManifestResult = await localRepoManager.WriteCompleteManifest(completeManifest, fd);
			if (!saveCompleteManifestResult.Success)
			{
				await CleanupTemporaryFiles(patchZipFilePath);
				return saveCompleteManifestResult;
			}
			overallInstallProgress?.RecordProgress(1uL);
			Result applyOperationsResult;
			using (IPatchManager patchManager = _patchManagerFactory.Create(patchZipFilePath))
			{
				PatchOperation[] operations = await patchManager.GetOperations(fd);
				currentStage = InstallStage.ApplyOperationsStarted;
				overallInstallProgress?.RecordProgress(1uL);
				applyOperationsResult = await patchManager.ApplyOperations(operations, completeManifest, absoluteDestination, progressController, fd);
			}
			if (!applyOperationsResult.Success)
			{
				await CleanupTemporaryFiles(patchZipFilePath);
				return applyOperationsResult;
			}
			currentStage = InstallStage.ApplyOperationsCompleted;
			overallInstallProgress?.RecordProgress(1uL);
			Result writeVersionFileResult = await localRepoManager.WriteVersionFile();
			if (!writeVersionFileResult.Success)
			{
				await CleanupTemporaryFiles(patchZipFilePath);
				return writeVersionFileResult;
			}
			overallInstallProgress?.RecordProgress(1uL);
			Result result3 = await CleanupTemporaryFiles(patchZipFilePath);
			if (!result3.Success)
			{
				return result3;
			}
			currentStage = InstallStage.CleanupCompleted;
			overallInstallProgress?.RecordProgress(1uL);
			return Result.ResultSuccessful();
		}
		catch (ExceptionWithSubCategory exceptionWithSubCategory)
		{
			return exceptionWithSubCategory.ToResult();
		}
		catch (Exception ex)
		{
			_logger?.Log("Error installing " + appName + " (version: " + version + ") to folder " + absoluteDestination + ", got exception: " + ex.Message, LogLevel.L3_Error, fd);
			await CleanupTemporaryFiles(patchZipFilePath);
			InternalError internalError = ((ex is PlatypatchException) ? InternalError.PlatypatchError : InternalError.InternalError);
			return new Result
			{
				Success = false,
				Error = new Error(internalError, ex.Message, "")
			};
		}
		finally
		{
			if (progressController != null)
			{
				progressController.ProgressChanged -= OnProgressChanged;
			}
		}
		void OnProgressChanged(object _, float progress)
		{
			PublishInstallStatusUpdateEvent(currentStage, progressController?.Reference, progress, repositoryName, appName, absoluteDestination, version);
		}
	}

	private bool IsValidPatchStep(RepoMetaData repositoryMetadata, string installedVersion, string toVersion)
	{
		if (repositoryMetadata?.Content?.Versions?.ContainsKey(toVersion) != true)
		{
			return false;
		}
		return repositoryMetadata.Content.Versions[toVersion].Patches.ContainsKey(installedVersion);
	}

	private void PublishInstallStatusUpdateEvent(InstallStage installStage, string reference, float currentProgress, string repositoryName, string appName, string absoluteDestination, string version)
	{
		_internalEventHub?.Publish(new InstallProgressEvent
		{
			EventId = Guid.NewGuid(),
			Reference = reference,
			CurrentStage = installStage,
			Progress = currentProgress,
			RepositoryName = repositoryName,
			AppName = appName,
			InstallFolder = absoluteDestination,
			InstallingVersion = version
		});
	}

	public async Task<bool> CopyRepoFiles(string sourcepath, string targetpath, IEnumerable<string> filesInFinal = null)
	{
		if (_fileIo.PathExists(sourcepath))
		{
			if (!(await _fileIo.CreateDirectory(targetpath)).Success)
			{
				return false;
			}
			HashSet<string> directFilesInFinal = null;
			if (filesInFinal != null)
			{
				directFilesInFinal = new HashSet<string>(filesInFinal.Where((string x) => Path.GetFileName(x) == x));
			}
			foreach (string item in (await _fileIo.ListFiles(sourcepath).ConfigureAwait(continueOnCapturedContext: false)).Result)
			{
				string fileName = Path.GetFileName(item);
				if (directFilesInFinal == null || directFilesInFinal.Contains(fileName))
				{
					string targetFile = item.Replace(sourcepath, targetpath);
					if (!_fileIo.DiskIO.CopyFile(item, targetFile))
					{
						return false;
					}
				}
			}
			foreach (string dir in (await _fileIo.ListDirectories(sourcepath).ConfigureAwait(continueOnCapturedContext: false)).Result)
			{
				IEnumerable<string> filesInFinal2 = null;
				if (filesInFinal != null)
				{
					filesInFinal2 = filesInFinal.Where((string x) => Path.GetDirectoryName(x) == dir);
				}
				string targetpath2 = dir.Replace(sourcepath, targetpath);
				if (!(await CopyRepoFiles(dir, targetpath2, filesInFinal2)))
				{
					return false;
				}
			}
			return true;
		}
		return false;
	}

	private async Task<DownloadPatchZipResult> DownloadPatchZip(IRemoteRepository remoteRepository, string repositoryName, string version, string installedVersion, IProgressController progressController, bool doNotSetTransferStateToSuccessful = false, FlowData fd = null)
	{
		_logger?.Log("InstallToFolder.DownloadPatchZip", LogLevel.L0_Info, fd);
		FolderManifest patchManifest = await remoteRepository.GetPatchManifest(version, installedVersion, fd);
		string patchStagingPath = GetPatchStagingPath(repositoryName);
		DownloadFilesResult downloadFilesResult = await remoteRepository.DownloadFilesInManifest(patchManifest, patchStagingPath, progressController, doNotSetTransferStateToSuccessful, fd);
		if (!downloadFilesResult.Success)
		{
			DownloadPatchZipResult downloadPatchZipResult = downloadFilesResult.CastTo<DownloadPatchZipResult>();
			downloadPatchZipResult.ZipFilePath = downloadFilesResult.LocalPaths?.FirstOrDefault();
			return downloadPatchZipResult;
		}
		List<string> localPaths = downloadFilesResult.LocalPaths;
		if (localPaths == null || localPaths.Count != 1)
		{
			return new DownloadPatchZipResult
			{
				Success = false,
				ZipFilePath = downloadFilesResult.LocalPaths.First(),
				Error = new Error(InternalError.PlatypatchError, $"A single patch zip file should have been downloaded, but downloaded {downloadFilesResult.LocalPaths.Count} files.", string.Empty)
			};
		}
		return new DownloadPatchZipResult
		{
			Success = true,
			PatchManifest = patchManifest,
			ZipFilePath = downloadFilesResult.LocalPaths.First()
		};
	}

	private async Task<Result> CleanupTemporaryFiles(string tempfolder)
	{
		if (string.IsNullOrEmpty(tempfolder))
		{
			return Result.ResultSuccessful();
		}
		string folderToDelete = _fileIo.DiskIO.GetDirectoryName(tempfolder);
		FileIOStatus fileIOStatus = await _fileIo.DeleteDirectory(folderToDelete, recursive: true);
		if (!fileIOStatus.Success)
		{
			return fileIOStatus.ToResult();
		}
		if (string.IsNullOrEmpty(folderToDelete))
		{
			return fileIOStatus.ToResult();
		}
		return Result.ResultSuccessful();
	}

	public string GetPatchStagingPath(string repoName)
	{
		string pathSafeRepoName = GetPathSafeRepoName(repoName);
		return _pathFactory.GetPatchDownloadStagingPath(pathSafeRepoName);
	}

	private string GetPathSafeRepoName(string repoName)
	{
		return CryptUtils.HashString(repoName);
	}

	public async Task<Result> Uninstall(string absoluteInstallationPath, string appName, bool clean, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		try
		{
			return await _localRepoManagerFactory.Create(absoluteInstallationPath, appName, null).Uninstall(clean, fd);
		}
		catch (Exception ex)
		{
			_logger?.Log("Error uninstalling " + appName + " from folder " + absoluteInstallationPath + ", got exception: " + ex.Message, LogLevel.L3_Error, fd);
			InternalError internalError = ((ex is PlatypatchException) ? InternalError.PlatypatchError : InternalError.InternalError);
			return new Result
			{
				Success = false,
				Error = new Error(internalError, ex.Message, "")
			};
		}
	}
}
