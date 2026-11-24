using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
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
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Util;
using Xdelta;

namespace PDX.SDK.Internal.Platypatch;

internal class PatchManager : IPatchManager, IDisposable
{
	private readonly IFileIO _fileIo;

	private readonly IPathFactory _pathFactory;

	private readonly IFileSystemOperation _fileSystemOperation;

	private readonly ILogger _logger;

	private readonly ZipArchive _zipArchive;

	private bool _disposed;

	public PatchManager(string patchZipPath, IFileIO fileIo, IPathFactory pathFactory, IFileSystemOperation fileSystemOperation, ILogger logger = null)
	{
		//IL_003b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Expected O, but got Unknown
		_fileIo = fileIo;
		_pathFactory = pathFactory;
		_fileSystemOperation = fileSystemOperation;
		_logger = logger;
		Stream stream = (fileIo as FileIO).CreateReadStream(patchZipPath);
		_zipArchive = new ZipArchive(stream, (ZipArchiveMode)0, false, Encoding.UTF8);
	}

	public virtual async Task<PatchOperation[]> GetOperations(FlowData flowData = null)
	{
		ZipArchiveEntry entry = _zipArchive.GetEntry("operations");
		if (entry == null)
		{
			return null;
		}
		using Stream operationsFileStream = entry.Open();
		using StreamReader reader = new StreamReader(operationsFileStream);
		List<PatchOperation> patchOperations = new List<PatchOperation>();
		while (!reader.EndOfStream)
		{
			patchOperations.Add(new PatchOperation(await reader.ReadLineAsync()));
		}
		return patchOperations.ToArray();
	}

	public virtual async Task<Result> ApplyOperations(PatchOperation[] operations, FolderManifest manifest, string targetAbsoluteRoot, IProgressController progressController = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		ISubProgressChunk subProgress = progressController?.GetSubProcessChunk((uint)operations.Length);
		foreach (PatchOperation item in operations.OrderBy((PatchOperation x) => x.Type))
		{
			Result result = await ApplyOperation(item, manifest, targetAbsoluteRoot, fd);
			if (!result.Success)
			{
				return result;
			}
			subProgress?.RecordProgress(1uL);
		}
		return Result.ResultSuccessful();
	}

	internal virtual Task<Result> ApplyOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		return operation.Type switch
		{
			PatchOperationType.New => ApplyNewFileOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			PatchOperationType.Patch => ApplyPatchOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			PatchOperationType.Delete => ApplyDeleteOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			PatchOperationType.CreateDirectory => ApplyCreateDirectoryOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			PatchOperationType.Symlink => ApplySymlinkOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			PatchOperationType.UpdateFlags => ApplyUpdateFlagsOperation(operation, manifest, targetAbsoluteRoot, flowData2), 
			_ => throw new ArgumentOutOfRangeException(), 
		};
	}

	private Task<Result> ApplyUpdateFlagsOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData flowData)
	{
		FlowData flowData2 = new FlowData(flowData);
		TaskCompletionSource<Result> taskCompletionSource = new TaskCompletionSource<Result>();
		ManifestFileEntry manifestFileEntry = manifest?.FileEntries?.FirstOrDefault((ManifestFileEntry e) => e.RelativePath == operation.RelativePath);
		if (manifestFileEntry == null)
		{
			taskCompletionSource.SetResult(new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, "File entry " + operation.RelativePath + " incompatible with the Manifest.", "")
			});
			return taskCompletionSource.Task;
		}
		Result result = _fileSystemOperation.ApplyFlags(manifestFileEntry, targetAbsoluteRoot, flowData2);
		taskCompletionSource.SetResult(result);
		return taskCompletionSource.Task;
	}

	private async Task<Result> ApplySymlinkOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData flowData)
	{
		FlowData fd = new FlowData(flowData);
		string linkName = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(operation.RelativePath));
		string text = await ReadEntryAsStringOrDefault(operation.RelativePath, fd);
		if (string.IsNullOrEmpty(text))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "No symlink target found in archive '" + operation.RelativePath + "'", "")
			};
		}
		string target = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(text));
		if (_fileIo.PathExists(linkName))
		{
			FileIOStatus fileIOStatus = await DeleteTarget(linkName);
			if (!fileIOStatus.Success)
			{
				return fileIOStatus.ToResult();
			}
		}
		return _fileSystemOperation.CreateSymlink(target, linkName, fd, _logger);
	}

	private async Task<Result> ApplyCreateDirectoryOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData fd)
	{
		string path = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(operation.RelativePath));
		return (await _fileIo.CreateDirectory(path)).ToResult();
	}

	private async Task<Result> ApplyDeleteOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData fd)
	{
		string path = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(operation.RelativePath));
		if (_fileIo.PathExists(path))
		{
			return (await DeleteTarget(path)).ToResult();
		}
		return Result.ResultSuccessful();
	}

	private async Task<Result> ApplyPatchOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData flowData)
	{
		FlowData fd = new FlowData(flowData);
		string vcDiffFileName = operation.RelativePath + ".vcdiff";
		Result result = await WriteEntryToDisk(vcDiffFileName, targetAbsoluteRoot, fd);
		if (!result.Success)
		{
			return result;
		}
		string path = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(vcDiffFileName));
		string inFilePath = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(operation.RelativePath));
		string outFilePath = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(operation.RelativePath + ".new"));
		try
		{
			FileIO fileIO = _fileIo as FileIO;
			using (Stream stream = fileIO.CreateReadStream(path, fd))
			{
				using (Stream input = fileIO.CreateReadStream(inFilePath, fd))
				{
					using Stream output = fileIO.CreateReadWriteStream(outFilePath, fd);
					new Xdelta.Decoder(input, stream, output).Run();
				}
				stream.Dispose();
			}
			await _fileIo.Delete(path);
			FileIOStatus fileIOStatus = await MoveToFinalDestination(inFilePath, outFilePath);
			if (!fileIOStatus.Success)
			{
				return fileIOStatus.ToResult();
			}
			return await ApplyUpdateFlagsOperation(operation, manifest, targetAbsoluteRoot, fd);
		}
		catch (Exception ex)
		{
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.InternalError, ex.Message, "")
			};
		}
	}

	private async Task<FileIOStatus> MoveToFinalDestination(string inFilePath, string outFilePath)
	{
		if (_fileIo.PathExists(inFilePath))
		{
			FileIOStatus fileIOStatus = await _fileIo.Delete(inFilePath);
			if (!fileIOStatus.Success)
			{
				return fileIOStatus;
			}
		}
		_fileIo.DiskIO.Move(outFilePath, inFilePath);
		return new FileIOStatus
		{
			Success = true
		};
	}

	private async Task<Result> ApplyNewFileOperation(PatchOperation operation, FolderManifest manifest, string targetAbsoluteRoot, FlowData flowData)
	{
		Result result = await WriteEntryToDisk(operation.RelativePath, targetAbsoluteRoot, flowData);
		if (!result.Success)
		{
			return result;
		}
		return await ApplyUpdateFlagsOperation(operation, manifest, targetAbsoluteRoot, flowData);
	}

	private async Task<Result> WriteEntryToDisk(string relativePath, string targetAbsoluteRoot, FlowData flowData)
	{
		FlowData fd = new FlowData(flowData);
		string finalTargetPath = Path.Combine(targetAbsoluteRoot, PathHelper.SanitizePath(relativePath));
		ZipArchiveEntry entry = _zipArchive.GetEntry(relativePath);
		if (entry == null)
		{
			entry = _zipArchive.Entries.FirstOrDefault((ZipArchiveEntry e) => e.FullName.Replace('/', Path.DirectorySeparatorChar) == relativePath);
		}
		try
		{
			using Stream inStream = entry.Open();
			return (await _fileIo.Write(finalTargetPath, inStream, fd)).ToResult();
		}
		catch (InvalidDataException)
		{
			using Stream inStream = ((object)entry).GetType().GetMethod("OpenInReadMode", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(entry, new object[1] { false }) as Stream;
			return (await _fileIo.Write(finalTargetPath, inStream, fd)).ToResult();
		}
		catch (Exception ex2)
		{
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.InternalError, ex2.Message, "")
			};
		}
	}

	private async Task<string> ReadEntryAsStringOrDefault(string relativePath, FlowData flowData)
	{
		FlowData fd = new FlowData(flowData);
		try
		{
			ZipArchiveEntry entry = _zipArchive.GetEntry(relativePath);
			using Stream stream = entry.Open();
			using StreamReader reader = new StreamReader(stream);
			return await reader.ReadToEndAsync();
		}
		catch (Exception ex)
		{
			_logger?.Log("Error getting '" + relativePath + "' from archive. Got exception: " + ex.Message, LogLevel.L3_Error, fd);
			return null;
		}
	}

	private async Task<FileIOStatus> DeleteTarget(string path)
	{
		return (!string.IsNullOrEmpty(Path.GetFileName(path)) && !Directory.Exists(path)) ? (await _fileIo.Delete(path)) : (await _fileIo.DeleteDirectory(path));
	}

	~PatchManager()
	{
		Dispose();
	}

	public virtual void Dispose()
	{
		Dispose(disposing: true);
		GC.SuppressFinalize(this);
	}

	protected void Dispose(bool disposing)
	{
		if (_disposed)
		{
			return;
		}
		if (disposing)
		{
			ZipArchive zipArchive = _zipArchive;
			if (zipArchive != null)
			{
				zipArchive.Dispose();
			}
		}
		_disposed = true;
	}
}
