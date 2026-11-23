using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Events;
using PDX.SDK.Internal.Events;
using PDX.SDK.Internal.Events.Internal.Mods;
using PDX.SDK.Internal.Platypatch;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Platypatch.Results;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Util;
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsDownloaderService : InternalService, IModsDownloaderService
{
	private readonly IPathFactory _pathFactory;

	private readonly IModsBackendApiService _modsBackendApiService;

	private readonly IFileDownloader _fileDownloader;

	private readonly IExecutor _executor;

	private readonly ILogger _logger;

	[Preserve]
	public ModsDownloaderService(IContext context, IPathFactory pathFactory, IModsBackendApiService modsBackendApiService, IFileDownloader fileDownloader, IExecutor executor, ILogger logger)
		: base("ModsDownloaderService", context as Context)
	{
		_pathFactory = pathFactory;
		_modsBackendApiService = modsBackendApiService;
		_fileDownloader = fileDownloader;
		_executor = executor;
		_logger = logger;
	}

	public virtual async Task<DownloadResult> Download(ModDownloadMetadata modDownloadMetadata, bool doNotSetTransferStateToSuccessful = false, bool useFinalDirAsTemp = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Download", LogLevel.L0_Info, fd);
		(_context.Events as IInternalEventHub).Publish(new ModDownloadStarted
		{
			EventId = Guid.NewGuid(),
			ModId = modDownloadMetadata.Id
		});
		GetGameDataResult getGameDataResult = await _modsBackendApiService.GetGameData(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!getGameDataResult.Success)
		{
			return getGameDataResult.CastTo<DownloadResult>();
		}
		ModsDownloadProgressController progressController = new ModsDownloadProgressController(modDownloadMetadata.Id);
		string modFolderName = ModPath.GetModFolderName(modDownloadMetadata.Id, modDownloadMetadata.Version, tempFolder: true, _logger);
		if (modFolderName == null)
		{
			progressController.MarkAsFailed();
			return new DownloadResult
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "Could not create proper download location", "")
			};
		}
		string finalFolderAbsolutePath = _pathFactory.GetModsPath(ModPath.GetModFolderName(modDownloadMetadata.Id, modDownloadMetadata.Version, tempFolder: false, _context.Logger));
		string tempModAbsolutePath;
		if (useFinalDirAsTemp)
		{
			tempModAbsolutePath = finalFolderAbsolutePath;
		}
		else
		{
			tempModAbsolutePath = _pathFactory.GetModsPath(modFolderName);
			if (_context.FileIO.PathExists(tempModAbsolutePath))
			{
				await _context.FileIO.DeleteDirectory(tempModAbsolutePath, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		Result downloadContentResult = await DownloadContent(modDownloadMetadata, tempModAbsolutePath, progressController, doNotSetTransferStateToSuccessful, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadContentResult.Success)
		{
			await CleanUpDownloads(modDownloadMetadata, tempModAbsolutePath).ConfigureAwait(continueOnCapturedContext: false);
			progressController.MarkAsFailed();
			return downloadContentResult.CastTo<DownloadResult>();
		}
		DownloadResult downloadImages = await DownloadAllImages(modDownloadMetadata, tempModAbsolutePath, null, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadImages.Success)
		{
			await CleanUpDownloads(modDownloadMetadata, tempModAbsolutePath).ConfigureAwait(continueOnCapturedContext: false);
			progressController.MarkAsFailed();
			return downloadImages.CastTo<DownloadResult>();
		}
		if (!useFinalDirAsTemp)
		{
			if (_context.FileIO.PathExists(finalFolderAbsolutePath))
			{
				await _context.FileIO.DeleteDirectory(finalFolderAbsolutePath, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
			}
			FileIOStatus fileIOStatus = await _context.FileIO.Move(tempModAbsolutePath, finalFolderAbsolutePath).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				progressController.MarkAsFailed();
				Enum subCategory = fileIOStatus.ErrorCode.ToInternalError();
				return new DownloadResult
				{
					Success = false,
					Error = new Error(subCategory, "Could not rename the temporary mod folder", fileIOStatus.Message)
				};
			}
		}
		progressController.MarkAsSuccess();
		(_context.Events as InternalEventHub).Publish(new ModDownloadCompleted
		{
			EventId = Guid.NewGuid(),
			ModId = modDownloadMetadata.Id
		});
		string contentFileOrFolder = (getGameDataResult.Actions.LooseModFilesSupport ? string.Empty : GetModContentFileName(modDownloadMetadata.Name, getGameDataResult.Extension));
		return new DownloadResult
		{
			Success = true,
			ContentFileOrFolder = contentFileOrFolder,
			ThumbnailFilename = downloadImages.ThumbnailFilename,
			ScreenshotsFilenames = downloadImages.ScreenshotsFilenames
		};
	}

	public async Task<DownloadResult> DownloadToWip(string folderName, ModDownloadMetadata modDownloadMetadata, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DownloadToWip", LogLevel.L0_Info, fd);
		(_context.Events as IInternalEventHub).Publish(new ModDownloadStarted
		{
			EventId = Guid.NewGuid(),
			ModId = modDownloadMetadata.Id
		});
		GetGameDataResult getGameDataResult = await _modsBackendApiService.GetGameData(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!getGameDataResult.Success)
		{
			return getGameDataResult.CastTo<DownloadResult>();
		}
		ModsDownloadProgressController progressController = new ModsDownloadProgressController(modDownloadMetadata.Id);
		string modFolderPath = _pathFactory.GetModsPath(folderName, LocalType.WorkInProgress);
		Result downloadContentResult = await DownloadContent(modDownloadMetadata, modFolderPath, progressController, doNotSetTransferStateToSuccessful, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadContentResult.Success)
		{
			await CleanUpDownloads(modDownloadMetadata, modFolderPath).ConfigureAwait(continueOnCapturedContext: false);
			progressController.MarkAsFailed();
			return downloadContentResult.CastTo<DownloadResult>();
		}
		string path = Path.Combine(modFolderPath, ".cpatch");
		if (_context.FileIO.PathExists(path) && !(await _context.FileIO.DeleteDirectory(path, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).Success)
		{
			_context.Log("Unable to delete the .cpatch folder", LogLevel.L2_Warning, fd);
		}
		DownloadResult downloadImages = await DownloadAllImages(modDownloadMetadata, modFolderPath, null, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadImages.Success)
		{
			await CleanUpDownloads(modDownloadMetadata, modFolderPath).ConfigureAwait(continueOnCapturedContext: false);
			progressController.MarkAsFailed();
			return downloadImages.CastTo<DownloadResult>();
		}
		progressController.MarkAsSuccess();
		(_context.Events as InternalEventHub).Publish(new ModDownloadCompleted
		{
			EventId = Guid.NewGuid(),
			ModId = modDownloadMetadata.Id
		});
		string contentFileOrFolder = (getGameDataResult.Actions.LooseModFilesSupport ? string.Empty : GetModContentFileName(modDownloadMetadata.Name, getGameDataResult.Extension));
		return new DownloadResult
		{
			Success = true,
			ContentFileOrFolder = contentFileOrFolder,
			ThumbnailFilename = downloadImages.ThumbnailFilename,
			ScreenshotsFilenames = downloadImages.ScreenshotsFilenames
		};
	}

	public async Task<DownloadResult> DownloadAllImages(ModDownloadMetadata modDownloadMetadata, string modFolderPath, IProgressController progressController = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.DownloadAllImages", LogLevel.L0_Info, flowData2);
		List<Task<Result>> list = new List<Task<Result>>();
		List<string> screenshots = new List<string>();
		string thumbnailPath = string.Empty;
		if (modDownloadMetadata.Screenshots != null && modDownloadMetadata.Screenshots.Count > 0)
		{
			foreach (Screenshot screenshot in modDownloadMetadata.Screenshots)
			{
				string modImagePath = GetModImagePath(screenshot.Image, modFolderPath);
				screenshots.Add(Path.Combine(".metadata", Path.GetFileName(modImagePath)));
				list.Add(DownloadImage(screenshot.Image, modImagePath, progressController, flowData));
			}
		}
		if (!string.IsNullOrEmpty(modDownloadMetadata.ThumbnailPath))
		{
			thumbnailPath = GetModImagePath(modDownloadMetadata.ThumbnailPath, modFolderPath);
			list.Add(DownloadImage(modDownloadMetadata.ThumbnailPath, thumbnailPath, progressController, flowData));
		}
		Result[] array = await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
		foreach (Result result in array)
		{
			if (!result.Success)
			{
				return result.CastTo<DownloadResult>();
			}
		}
		return new DownloadResult
		{
			Success = true,
			ThumbnailFilename = ((!string.IsNullOrEmpty(thumbnailPath)) ? Path.Combine(".metadata", Path.GetFileName(thumbnailPath)) : null),
			ScreenshotsFilenames = screenshots
		};
	}

	public async Task<Result> DownloadContent(ModDownloadMetadata modDownloadMetadata, string contentFolderPath, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DownloadContent", LogLevel.L0_Info, fd);
		RepositoryMetaDataResult repositoryMetaDataResult = await _executor.GetRepositoryMetaData(modDownloadMetadata.RepositoryPath, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!repositoryMetaDataResult.Success)
		{
			return repositoryMetaDataResult;
		}
		Result result = await _executor.InstallToFolder(repositoryMetaDataResult.RepositoryMetaData, modDownloadMetadata.RepositoryPath, modDownloadMetadata.Name, contentFolderPath, modDownloadMetadata.Version, progressController, doNotSetTransferStateToSuccessful, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			Log("Mod download failed: " + result.Error?.Details, LogLevel.L2_Warning, fd);
		}
		return result;
	}

	public async Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string versionToInstall, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		RepositoryMetaDataResult repositoryMetaDataResult = await _executor.GetRepositoryMetaData(modDetails.RepositoryPath, fd);
		if (!repositoryMetaDataResult.Success)
		{
			return repositoryMetaDataResult.CastTo<ModInstallSizeResult>();
		}
		string modsPath = _pathFactory.GetModsPath(ModPath.GetModFolderName(modDetails.Id, versionToInstall, tempFolder: false, _logger));
		PatchSizeResult patchSizeResult = await _executor.CalculateSizeOfPatch(repositoryMetaDataResult.RepositoryMetaData, modDetails.Name, modsPath, versionToInstall, fd);
		if (!patchSizeResult.Success)
		{
			return patchSizeResult.CastTo<ModInstallSizeResult>();
		}
		return new ModInstallSizeResult
		{
			Success = true,
			InstallSize = patchSizeResult.PatchSize
		};
	}

	public async Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string currentlyInstalledVersion, string versionToInstall, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		RepositoryMetaDataResult repositoryMetaDataResult = await _executor.GetRepositoryMetaData(modDetails.RepositoryPath, fd);
		if (!repositoryMetaDataResult.Success)
		{
			return repositoryMetaDataResult.CastTo<ModInstallSizeResult>();
		}
		string modsPath = _pathFactory.GetModsPath(ModPath.GetModFolderName(modDetails.Id, currentlyInstalledVersion, tempFolder: false, _logger));
		PatchSizeResult patchSizeResult = await _executor.CalculateSizeOfPatch(repositoryMetaDataResult.RepositoryMetaData, modDetails.Name, modsPath, versionToInstall, fd);
		if (!patchSizeResult.Success)
		{
			return patchSizeResult.CastTo<ModInstallSizeResult>();
		}
		return new ModInstallSizeResult
		{
			Success = true,
			InstallSize = patchSizeResult.PatchSize
		};
	}

	internal async Task<Result> DownloadImage(string remotePath, string localAbsolutePath, IProgressController progressController = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.DownloadImage", LogLevel.L0_Info, flowData2);
		TransferStatus downloadTransfer = progressController?.GetTransferStatus(ETransferType.Download, ETransferServiceType.Mods, localAbsolutePath);
		return await _fileDownloader.PerformDownload(remotePath, localAbsolutePath, downloadTransfer, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task CleanUpDownloads(ModDownloadMetadata modDownloadMetadata, string modPath)
	{
		if (modPath != null)
		{
			OperationResult<List<string>> operationResult = await _context.FileIO.ListDirectories(modPath).ConfigureAwait(continueOnCapturedContext: false);
			if (operationResult.Status.Success)
			{
				foreach (string item in operationResult.Result)
				{
					if (Path.GetFileName(item) != ".metadata")
					{
						FileIOStatus fileIOStatus = await _context.FileIO.DeleteDirectory(item, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
						if (!fileIOStatus.Success)
						{
							Log(fileIOStatus.Message, LogLevel.L2_Warning);
						}
					}
				}
			}
			OperationResult<List<string>> operationResult2 = await _context.FileIO.ListFiles(modPath).ConfigureAwait(continueOnCapturedContext: false);
			if (operationResult2.Status.Success)
			{
				foreach (string item2 in operationResult2.Result)
				{
					FileIOStatus fileIOStatus = await _context.FileIO.Delete(item2).ConfigureAwait(continueOnCapturedContext: false);
					if (!fileIOStatus.Success)
					{
						Log(fileIOStatus.Message, LogLevel.L2_Warning);
					}
				}
			}
		}
		if (GetModImagePath(modDownloadMetadata.ThumbnailPath, modPath) != null)
		{
			FileIOStatus fileIOStatus = await _context.FileIO.Delete(GetModImagePath(modDownloadMetadata.ThumbnailPath, modPath)).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				Log(fileIOStatus.Message, LogLevel.L2_Warning);
			}
		}
		foreach (Screenshot screenshot in modDownloadMetadata.Screenshots)
		{
			FileIOStatus fileIOStatus = await _context.FileIO.Delete(GetModImagePath(screenshot.Image, modPath)).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				Log(fileIOStatus.Message, LogLevel.L2_Warning);
			}
		}
	}

	internal string GetModImagePath(string imageFilename, string modFolderPath)
	{
		return Path.Combine(modFolderPath, ".metadata", Path.GetFileName(imageFilename));
	}

	internal string GetModContentFileName(string modName, string fileExtension)
	{
		if (string.IsNullOrWhiteSpace(fileExtension))
		{
			return modName;
		}
		return $"{modName}.{fileExtension}";
	}

	internal IExecutor GetExecutor()
	{
		return _executor;
	}
}
