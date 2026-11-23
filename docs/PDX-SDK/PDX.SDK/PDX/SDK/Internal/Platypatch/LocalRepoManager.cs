using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

internal class LocalRepoManager : ILocalRepoManager
{
	private readonly string _absoluteRepoPath;

	private readonly string _appName;

	private readonly string _version;

	private readonly IFileIO _fileIo;

	private readonly IFileSystemOperation _fileSystemOperation;

	private readonly ILogger _logger;

	public LocalRepoManager(string absoluteRepoPath, string appName, string version, IFileIO fileIO, IFileSystemOperation fileSystemOperation, ILogger logger = null)
	{
		_absoluteRepoPath = absoluteRepoPath;
		_appName = appName;
		_version = version;
		_fileIo = fileIO;
		_fileSystemOperation = fileSystemOperation;
		_logger = logger;
	}

	public async Task<OperationResult<string>> GetInstalledVersion(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		string path = Path.Combine(_absoluteRepoPath, ".cpatch", _appName, "version");
		if (!_fileIo.PathExists(path))
		{
			return new OperationResult<string>
			{
				Status = new FileIOStatus
				{
					Success = true
				},
				Result = "-1"
			};
		}
		return await _fileIo.ReadText(path, flowData2);
	}

	public Task<Result> WriteCompleteManifest(FolderManifest manifest, FlowData flowData = null)
	{
		return WriteManifest(manifest, "complete", flowData);
	}

	public Task<Result> WriteDependenciesManifest(FolderManifest manifest, FlowData flowData = null)
	{
		return WriteManifest(manifest, "_dependencies", flowData);
	}

	public async Task<Result> WriteVersionFile(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("LocalRepoManager.WriteVersionFile", LogLevel.L0_Info, flowData2);
		string path = Path.Combine(_absoluteRepoPath, ".cpatch", _appName, "version");
		return (await _fileIo.Write(path, new MemoryStream(Encoding.UTF8.GetBytes(_version)), flowData2)).ToResult();
	}

	public async Task<Result> Uninstall(bool clear = true, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		if (clear)
		{
			return await ClearUninstall(flowData2);
		}
		return await CleverWithDeletions(flowData2);
	}

	public async Task<PatchSizeResult> CalculateSizeOfPatch(RepoMetaData repoMetaData, string versionToInstall, FlowData flowData = null)
	{
		OperationResult<string> operationResult = await GetInstalledVersion(new FlowData(flowData));
		if (!operationResult.Status.Success)
		{
			return operationResult.Status.ToResult().CastTo<PatchSizeResult>();
		}
		string result = operationResult.Result;
		return new PatchSizeResult
		{
			Success = true,
			PatchSize = repoMetaData.CalculateSizeOfPatch(versionToInstall, result)
		};
	}

	private async Task<Result> ClearUninstall(FlowData flowData = null)
	{
		string path = Path.Combine(_absoluteRepoPath, ".cpatch");
		if (!_fileIo.PathExists(path))
		{
			return new Result
			{
				Success = false,
				Error = new Error(BadInput.BadInput, "The folder at " + _absoluteRepoPath + " is not a platypatch repository", "")
			};
		}
		return (await _fileIo.DeleteDirectory(_absoluteRepoPath, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).ToResult();
	}

	private async Task<Result> CleverWithDeletions(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		string versionToUninstall = _version;
		if (versionToUninstall == null)
		{
			OperationResult<string> operationResult = await GetInstalledVersion(fd);
			if (!operationResult.Status.Success)
			{
				return operationResult.Status.ToResult();
			}
			if (operationResult.Result == "-1")
			{
				_logger?.Log("Attempted to uninstall a not installed repo at '" + _absoluteRepoPath + "'", LogLevel.L0_Info, fd);
				return Result.ResultSuccessful();
			}
			versionToUninstall = operationResult.Result;
		}
		string path = Path.Combine(_absoluteRepoPath, ".cpatch", _appName, versionToUninstall, "complete", "manifest");
		OperationResult<string> operationResult2 = await _fileIo.ReadText(path, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult2.Status.Success)
		{
			return operationResult2.Status.ToResult();
		}
		FolderManifest folderManifest = new FolderManifest("", operationResult2.Result, versionToUninstall);
		IEnumerable<ManifestFileEntry> enumerable = folderManifest.FileEntries.Where((ManifestFileEntry f) => f.Flags.HasFlag(ManifestFileEntryFlags.Symlink));
		IEnumerable<ManifestFileEntry> enumerable2 = folderManifest.FileEntries.Where((ManifestFileEntry f) => !f.Flags.HasFlag(ManifestFileEntryFlags.Directory) && !f.Flags.HasFlag(ManifestFileEntryFlags.Symlink));
		IEnumerable<ManifestFileEntry> directoryEntries = folderManifest.FileEntries.Where((ManifestFileEntry f) => f.Flags.HasFlag(ManifestFileEntryFlags.Directory) && !f.Flags.HasFlag(ManifestFileEntryFlags.Symlink));
		foreach (ManifestFileEntry item in enumerable)
		{
			string path2 = Path.Combine(_absoluteRepoPath, item.RelativePath);
			Result result = _fileSystemOperation.Unlink(path2);
			if (!result.Success)
			{
				return result;
			}
		}
		foreach (ManifestFileEntry item2 in enumerable2)
		{
			string text = Path.Combine(_absoluteRepoPath, item2.RelativePath);
			if (_fileIo.PathExists(text))
			{
				FileIOStatus fileIOStatus = await _fileIo.Delete(text).ConfigureAwait(continueOnCapturedContext: false);
				if (!fileIOStatus.Success)
				{
					return fileIOStatus.ToResult();
				}
			}
			else
			{
				_logger?.Log("Tried to delete expected file at '" + text + "' but it did not exist", LogLevel.L0_Info, fd);
			}
		}
		foreach (ManifestFileEntry item3 in directoryEntries)
		{
			string fullPath = Path.Combine(_absoluteRepoPath, item3.RelativePath);
			if (_fileIo.PathExists(fullPath) && !(await _fileIo.ListFiles(fullPath).ConfigureAwait(continueOnCapturedContext: false)).Result.Any())
			{
				FileIOStatus fileIOStatus2 = await _fileIo.DeleteDirectory(fullPath).ConfigureAwait(continueOnCapturedContext: false);
				if (!fileIOStatus2.Success)
				{
					return fileIOStatus2.ToResult();
				}
			}
		}
		OperationResult<List<string>> filesInRepoRoot = await _fileIo.ListFiles(_absoluteRepoPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!filesInRepoRoot.Status.Success)
		{
			return filesInRepoRoot.Status.ToResult();
		}
		OperationResult<List<string>> operationResult3 = await _fileIo.ListDirectories(_absoluteRepoPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult3.Status.Success)
		{
			return operationResult3.Status.ToResult();
		}
		bool num = filesInRepoRoot.Result.Count == 0;
		bool flag = operationResult3.Result.Count == 0;
		bool flag2 = operationResult3.Result.Count == 1 && Path.GetFileName(operationResult3.Result[0]) == ".cpatch";
		if (num && (flag || flag2))
		{
			return (await _fileIo.DeleteDirectory(_absoluteRepoPath, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).ToResult();
		}
		string path3 = Path.Combine(_absoluteRepoPath, ".cpatch", _appName);
		return (await _fileIo.DeleteDirectory(path3, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).ToResult();
	}

	private async Task<Result> WriteManifest(FolderManifest manifest, string subFolder, FlowData flowData = null)
	{
		string path = Path.Combine(_absoluteRepoPath, ".cpatch", _appName, _version, subFolder, "manifest");
		byte[] bytes = Encoding.UTF8.GetBytes(manifest.RawContents);
		return (await _fileIo.Write(path, new MemoryStream(bytes), flowData).ConfigureAwait(continueOnCapturedContext: false)).ToResult();
	}
}
