using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Events;
using PDX.SDK.Internal.Errors;
using PDX.SDK.Internal.Events.Upload;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Enums;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Request;
using PDX.SDK.Internal.Util;
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsPublishService : InternalService, IModsPublishService
{
	private readonly IPathFactory _pathFactory;

	private readonly IModsCache _modsCache;

	private readonly IModsBackendApiService _modsBackendApiService;

	private readonly IFileIO _fileIO;

	private readonly IInternalEventHub _internalEventHub;

	[Preserve]
	public ModsPublishService(IContext context, IPathFactory pathFactory, IModsCache modsCache, IModsBackendApiService modsBackendApiService, IFileIO fileIO, IInternalEventHub internalEventHub = null)
		: base("ModsPublishService", context as Context)
	{
		_pathFactory = pathFactory;
		_modsCache = modsCache;
		_modsBackendApiService = modsBackendApiService;
		_fileIO = fileIO;
		_internalEventHub = internalEventHub;
	}

	public async Task<PublishResult> PublishWIP(PublishWipData publishWipData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishWIP", LogLevel.L1_Debug, fd);
		IMod wipMod = await _modsCache.GetWipMod(publishWipData.wipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(wipMod.LocalData.ContentFileOrFolder) || !_fileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder)))
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "The wip mod " + publishWipData.wipGuid + " is missing a content file", "")
			};
		}
		if (!File.Exists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder)))
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidType, "The content " + wipMod.LocalData.ContentFileOrFolder + " is not a file", "")
			};
		}
		_context.HTTP.RemoveOngoingTransfer(ETransferType.Upload, ETransferServiceType.Mods, publishWipData.wipGuid);
		Result result = await PreparePublishWithoutZipping(wipMod, wipMod.Name, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<PublishResult>();
		}
		PublishResult publishResult = await _modsBackendApiService.Publish(publishWipData.ToPublishV1Request(wipMod, _context.Namespace), v2: false, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (publishResult.Success)
		{
			wipMod.Id = publishResult.ModId;
			ModsCacheError modsCacheError = await _modsCache.UpdateMod(wipMod).ConfigureAwait(continueOnCapturedContext: false);
			if (!modsCacheError.Success)
			{
				Enum subCategory = modsCacheError.FileIoStatus.ErrorCode.ToInternalError();
				publishResult.Success = false;
				publishResult.Error = new Error(subCategory, "Published successfully, but setting Id in cache failed. If this WIP mod is published again, it will create a new mod in the backend.", modsCacheError.FileIoStatus.Message);
			}
		}
		return publishResult;
	}

	public async Task<PublishResult> PublishV2WIP(PublishWipData publishWipData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishV2WIP", LogLevel.L1_Debug, fd);
		IMod wipMod = await _modsCache.GetWipMod(publishWipData.wipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (wipMod.LocalData.ContentFileOrFolder == null)
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "The wip mod " + publishWipData.wipGuid + " is missing content files", "")
			};
		}
		_context.HTTP.RemoveOngoingTransfer(ETransferType.Upload, ETransferServiceType.Mods, publishWipData.wipGuid);
		ModsUploadProgressController progressController = new ModsUploadProgressController(publishWipData.wipGuid);
		string zipName = wipMod.Name + ".zip";
		ZipContentResult uploadFilesResult = await PreparePublishWithZipping(wipMod, zipName, wipMod.LocalData.ScreenshotsFilenames, progressController, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!uploadFilesResult.Success)
		{
			progressController.MarkAsFailed();
			await _fileIO.Delete(uploadFilesResult.ZipPath).ConfigureAwait(continueOnCapturedContext: false);
			return uploadFilesResult.CastTo<PublishResult>();
		}
		PublishResult publishResult = await _modsBackendApiService.Publish(publishWipData.ToPublishRequest(wipMod, _context.Namespace, Path.GetFileName(uploadFilesResult.ZipPath)), v2: true, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (publishResult.Success)
		{
			progressController.MarkAsSuccess();
			wipMod.Id = publishResult.ModId;
			ModsCacheError modsCacheError = await _modsCache.UpdateMod(wipMod).ConfigureAwait(continueOnCapturedContext: false);
			if (!modsCacheError.Success)
			{
				Enum subCategory = modsCacheError.FileIoStatus.ErrorCode.ToInternalError();
				publishResult.Success = false;
				publishResult.Error = new Error(subCategory, "Published successfully, but setting Id in cache failed. If this WIP mod is published again, it will create a new mod in the backend.", modsCacheError.FileIoStatus.Message);
			}
		}
		if (Path.GetFileName(uploadFilesResult.ZipPath) == zipName)
		{
			try
			{
				await _fileIO.Delete(uploadFilesResult.ZipPath).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
		return publishResult;
	}

	private async Task<Result> PreparePublishWithoutZipping(IMod wipMod, string modName, FlowData fd)
	{
		List<Task<Result>> list = new List<Task<Result>>();
		if (!string.IsNullOrEmpty(wipMod.LocalData.ContentFileOrFolder))
		{
			list.Add(UploadFile(FileType.Content, modName, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder), fd));
		}
		if (!string.IsNullOrEmpty(wipMod.LocalData.ThumbnailFilename) && _context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename)))
		{
			list.Add(UploadFile(FileType.Thumbnail, modName, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename), fd));
		}
		if (wipMod.LocalData.ScreenshotsFilenames != null)
		{
			foreach (string screenshotsFilename in wipMod.LocalData.ScreenshotsFilenames)
			{
				if (_context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, screenshotsFilename)))
				{
					list.Add(UploadFile(FileType.Screenshot, modName, Path.Combine(wipMod.LocalData.FolderAbsolutePath, screenshotsFilename), fd));
				}
			}
		}
		Result[] array = await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
		foreach (Result result in array)
		{
			if (!result.Success)
			{
				return new Result
				{
					Success = false,
					Error = new Error(InternalError.InternalError, "Couldn't upload all files to the backend", result.Error.ToString())
				};
			}
		}
		return Result.ResultSuccessful();
	}

	private async Task<ZipContentResult> PreparePublishWithZipping(IMod wipMod, string zipName, List<string> screenshotNames, IProgressController progressController, FlowData fd = null)
	{
		List<Task<Result>> uploadTasks = new List<Task<Result>>();
		string zipPath;
		if (!string.IsNullOrEmpty(wipMod.LocalData.ContentFileOrFolder) && Path.GetExtension(wipMod.LocalData.ContentFileOrFolder) == ".zip")
		{
			zipPath = Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder);
			PublishPackagingStatusUpdateEvent(PackagingStage.PackagingCompleted, progressController?.Reference, 1f, zipPath);
		}
		else
		{
			zipPath = Path.Combine(wipMod.LocalData.FolderAbsolutePath, zipName);
			Result result = await ZipContent(wipMod, zipPath, progressController).ConfigureAwait(continueOnCapturedContext: false);
			if (!result.Success)
			{
				return result.CastTo<ZipContentResult>();
			}
		}
		if (_context.FileIO.PathExists(zipPath))
		{
			uploadTasks.Add(UploadFileWithPresignedUrl(wipMod.Name, zipPath, isContentFile: true, progressController, fd));
		}
		if (!string.IsNullOrEmpty(wipMod.LocalData.ThumbnailFilename) && _context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename)))
		{
			uploadTasks.Add(UploadFileWithPresignedUrl(wipMod.Name, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename), isContentFile: false, null, fd));
		}
		if (screenshotNames != null)
		{
			foreach (string screenshotName in screenshotNames)
			{
				if (ModPath.VerifyModResourceAbsolutePath(_fileIO, wipMod.LocalData.FolderAbsolutePath, screenshotName, out var absolutePath))
				{
					uploadTasks.Add(UploadFileWithPresignedUrl(wipMod.Name, absolutePath, isContentFile: false, null, fd));
				}
			}
		}
		Result[] array = await Task.WhenAll(uploadTasks).ConfigureAwait(continueOnCapturedContext: false);
		foreach (Result result2 in array)
		{
			if (!result2.Success)
			{
				return new ZipContentResult
				{
					Success = false,
					Error = new Error(InternalError.InternalError, "Couldn't upload all files to the backend", result2.Error.ToString()),
					ZipPath = zipPath
				};
			}
		}
		return new ZipContentResult
		{
			Success = true,
			ZipPath = zipPath
		};
	}

	public async Task<PublishUpdateResult> PublishUpdateV1(PublishUpdateData inputData, bool isNewVersion = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishUpdateV1", LogLevel.L1_Debug, fd);
		IMod wipMod = await _modsCache.GetWipMod(inputData.WipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (isNewVersion)
		{
			if (string.IsNullOrEmpty(wipMod.LocalData.ContentFileOrFolder) || !_fileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder)))
			{
				return new PublishUpdateResult
				{
					Success = false,
					Error = new Error(InvalidInput.MissingParameter, "The wip mod " + inputData.WipGuid + " is missing a content file", "")
				};
			}
			if (!File.Exists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder)))
			{
				return new PublishUpdateResult
				{
					Success = false,
					Error = new Error(InvalidInput.InvalidType, "The content " + wipMod.LocalData.ContentFileOrFolder + " is not a file", "")
				};
			}
			_context.HTTP.RemoveOngoingTransfer(ETransferType.Upload, ETransferServiceType.Mods, wipMod.Name);
		}
		GetDetailsResult getDetailsResult = await _modsBackendApiService.GetDetails(wipMod.Id, null, null, fd);
		if (!getDetailsResult.Success)
		{
			return getDetailsResult.CastTo<PublishUpdateResult>();
		}
		List<string> newScreenshots = inputData.ScreenshotNames ?? wipMod.LocalData.ScreenshotsFilenames?.Select((string ls) => Path.GetFileName(ls)).ToList();
		object obj;
		if (newScreenshots.Count != 0)
		{
			obj = getDetailsResult.Mod.Screenshots?.Where((Screenshot s) => !newScreenshots.Contains(Path.GetFileName(s.Image)));
		}
		else
		{
			IEnumerable<Screenshot> screenshots = getDetailsResult.Mod.Screenshots;
			obj = screenshots;
		}
		IEnumerable<Screenshot> enumerable = (IEnumerable<Screenshot>)obj;
		if (enumerable != null && enumerable.Count() > 0)
		{
			Result[] array = await Task.WhenAll((from screenshot in enumerable
				where Path.GetFileName(screenshot.Image) != Path.GetFileName(wipMod.LocalData.ThumbnailFilename)
				select _modsBackendApiService.DeleteFile(FileType.Screenshot, wipMod.Name, Path.GetFileName(screenshot.Image), fd)).ToArray()).ConfigureAwait(continueOnCapturedContext: false);
			foreach (Result result in array)
			{
				if (!result.Success)
				{
					return new PublishUpdateResult
					{
						Success = false,
						Error = new Error(InternalError.InternalError, "Could not delete files in the backend", result.Error.Raw)
					};
				}
			}
		}
		Result result2 = await PreparePublishUpdateWithoutZipping(isNewVersion, wipMod, newScreenshots, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result2.Success)
		{
			return result2.CastTo<PublishUpdateResult>();
		}
		PublishUpdateRequest inputData2 = inputData.ToPublishUpdateRequest(wipMod, getDetailsResult.Mod, isNewVersion);
		return await _modsBackendApiService.PublishUpdate(inputData2, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PublishUpdateResult> PublishNewModVersionV2(PublishUpdateData publishNewModVersionData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishNewModVersionV2", LogLevel.L1_Debug, fd);
		IMod wipMod = await _modsCache.GetWipMod(publishNewModVersionData.WipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(wipMod.LocalData?.FolderAbsolutePath) || wipMod.LocalData?.ContentFileOrFolder == null)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "The wip mod " + publishNewModVersionData.WipGuid + " is missing content files", "")
			};
		}
		_context.HTTP.RemoveOngoingTransfer(ETransferType.Upload, ETransferServiceType.Mods, publishNewModVersionData.WipGuid);
		ModsUploadProgressController progressController = new ModsUploadProgressController(publishNewModVersionData.WipGuid);
		string zipName = wipMod.Name + "_" + publishNewModVersionData.UserModVersion + ".zip";
		List<string> screenshotNames = publishNewModVersionData.ScreenshotNames ?? wipMod.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToList();
		ZipContentResult uploadFilesResult = await PreparePublishWithZipping(wipMod, zipName, screenshotNames, progressController, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!uploadFilesResult.Success)
		{
			progressController.MarkAsFailed();
			await _fileIO.Delete(uploadFilesResult.ZipPath).ConfigureAwait(continueOnCapturedContext: false);
			return uploadFilesResult.CastTo<PublishUpdateResult>();
		}
		PublishUpdateResult resultToReturn = await _modsBackendApiService.PublishNewModVersion(publishNewModVersionData.ToPublishNewModVersionRequest(wipMod, Path.GetFileName(uploadFilesResult.ZipPath)), wipMod.Id, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (resultToReturn.Success)
		{
			progressController.MarkAsSuccess();
		}
		if (Path.GetFileName(uploadFilesResult.ZipPath) == zipName)
		{
			try
			{
				await _fileIO.Delete(uploadFilesResult.ZipPath).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch
			{
			}
		}
		return resultToReturn;
	}

	public async Task<PublishUpdateResult> UpdatePublishedModVersionV2(PublishUpdateData publishUpdateData, string version, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UpdatePublishedModVersionV2", LogLevel.L1_Debug, fd);
		IMod wipMod = await _modsCache.GetWipMod(publishUpdateData.WipGuid).ConfigureAwait(continueOnCapturedContext: false);
		List<Task<Result>> list = new List<Task<Result>>();
		if (!string.IsNullOrEmpty(wipMod.LocalData.ThumbnailFilename) && _context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename)))
		{
			list.Add(UploadFileWithPresignedUrl(wipMod.Name, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename), isContentFile: false, null, fd));
		}
		List<string> list2 = publishUpdateData.ScreenshotNames ?? wipMod.LocalData.ScreenshotsFilenames?.Select((string s) => Path.GetFileName(s)).ToList();
		if (list2 != null)
		{
			foreach (string item in list2)
			{
				if (ModPath.VerifyModResourceAbsolutePath(_fileIO, wipMod.LocalData.FolderAbsolutePath, item, out var absolutePath))
				{
					list.Add(UploadFileWithPresignedUrl(wipMod.Name, absolutePath, isContentFile: false, null, fd));
				}
			}
		}
		Result[] source = await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
		if (source.Any((Result r) => !r.Success))
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "Couldn't upload all files to the backend", source.FirstOrDefault((Result r) => !r.Success).Error.Raw)
			};
		}
		return await _modsBackendApiService.PatchModVersion(publishUpdateData.ToPatchModVersionRequest(wipMod), wipMod.Id, version, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<Result> PreparePublishUpdateWithoutZipping(bool updateContent, IMod wipMod, List<string> screenshotNames, FlowData fd)
	{
		List<Task<Result>> list = new List<Task<Result>>();
		if (updateContent && !string.IsNullOrEmpty(wipMod.LocalData.ContentFileOrFolder) && _context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder)))
		{
			list.Add(UploadFile(FileType.Content, wipMod.Name, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder), fd));
		}
		if (!string.IsNullOrEmpty(wipMod.LocalData.ThumbnailFilename) && _context.FileIO.PathExists(Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename)))
		{
			list.Add(UploadFile(FileType.Thumbnail, wipMod.Name, Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ThumbnailFilename), fd));
		}
		if (screenshotNames != null)
		{
			foreach (string screenshotName in screenshotNames)
			{
				if (ModPath.VerifyModResourceAbsolutePath(_fileIO, wipMod.LocalData.FolderAbsolutePath, screenshotName, out var absolutePath))
				{
					list.Add(UploadFile(FileType.Screenshot, wipMod.Name, absolutePath, fd));
				}
			}
		}
		Result[] array = await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
		foreach (Result result in array)
		{
			if (!result.Success)
			{
				return new Result
				{
					Success = false,
					Error = new Error(InternalError.InternalError, "Couldn't upload all files to the backend", result.Error.Raw)
				};
			}
		}
		return Result.ResultSuccessful();
	}

	internal async Task<Result> ZipContent(IMod wipMod, string zipPath, IProgressController progressController = null)
	{
		string absoluteSourcePath = Path.Combine(wipMod.LocalData.FolderAbsolutePath, wipMod.LocalData.ContentFileOrFolder);
		PackagingStage currentStage = PackagingStage.PackagingStarted;
		try
		{
			if (progressController != null)
			{
				progressController.ProgressChanged += OnProgressChanged;
			}
			ISubProgressChunk overallPackagingProgress = progressController?.GetSubProcessChunk(3uL);
			overallPackagingProgress?.RecordProgress(1uL);
			List<string> list = new List<string>();
			int fileAttributes = _fileIO.DiskIO.GetFileAttributes(absoluteSourcePath);
			bool isDirectory = (fileAttributes & 0x10) > 0;
			if (isDirectory)
			{
				list = await ListAllFilesUnderDirectory(absoluteSourcePath).ConfigureAwait(continueOnCapturedContext: false);
				if (list.Count == 0)
				{
					return new Result
					{
						Success = false,
						Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "There are no content files to compress", string.Empty)
					};
				}
			}
			ZipArchive val = ZipFile.Open(zipPath, (ZipArchiveMode)1);
			try
			{
				currentStage = PackagingStage.ZipFileCreated;
				overallPackagingProgress?.RecordProgress(1uL);
				if (isDirectory)
				{
					ISubProgressChunk subProgressChunk = progressController?.GetSubProcessChunk((ulong)list.Count);
					foreach (string item in list)
					{
						string entryName = item.Substring(absoluteSourcePath.Length + 1);
						val.CreateEntryFromFile(item, entryName);
						subProgressChunk?.RecordProgress(1uL);
					}
				}
				else
				{
					val.CreateEntryFromFile(absoluteSourcePath, wipMod.LocalData.ContentFileOrFolder);
				}
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
			currentStage = PackagingStage.PackagingCompleted;
			overallPackagingProgress?.RecordProgress(1uL);
			return Result.ResultSuccessful();
		}
		catch (Exception ex)
		{
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, "Could not compress the resource, got exception: " + ex.Message, string.Empty)
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
			PublishPackagingStatusUpdateEvent(currentStage, progressController?.Reference, progress, absoluteSourcePath);
		}
	}

	private async Task<List<string>> ListAllFilesUnderDirectory(string path)
	{
		List<string> allFiles = new List<string>();
		allFiles.AddRange((await _fileIO.ListFiles(path).ConfigureAwait(continueOnCapturedContext: false)).Result);
		foreach (string item in (await _fileIO.ListDirectories(path).ConfigureAwait(continueOnCapturedContext: false)).Result)
		{
			if (!(Path.GetFileName(item) == ".metadata"))
			{
				allFiles.AddRange(await ListAllFilesUnderDirectory(item).ConfigureAwait(continueOnCapturedContext: false));
			}
		}
		return allFiles;
	}

	private void PublishPackagingStatusUpdateEvent(PackagingStage publishStage, string reference, float currentProgress, string absoluteSource)
	{
		_internalEventHub?.Publish(new PackagingProgressEvent
		{
			EventId = Guid.NewGuid(),
			Reference = reference,
			CurrentStage = publishStage,
			Progress = currentProgress,
			SourceFolder = absoluteSource
		});
	}

	public async Task<Result> Unpublish(int modId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Unpublish", LogLevel.L1_Debug, flowData2);
		return await _modsBackendApiService.Unpublish(modId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<Result> UploadFile(FileType filetype, string uuid, string absoluteFilePath, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UploadFile", LogLevel.L1_Debug, fd);
		string fileName = Path.GetFileName(absoluteFilePath);
		Path.Combine(_context.Namespace, uuid, filetype.GetFileType(), fileName);
		TransferStatus transfer = null;
		if (filetype == FileType.Content)
		{
			transfer = new TransferStatus(ETransferType.Upload, ETransferServiceType.Mods, uuid);
		}
		AsyncReaderWriterLock diskLock = Context.DiskFileLocks.FetchLock(absoluteFilePath);
		await diskLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream fileStream = _context.FileIO.CreateReadStream(absoluteFilePath, fd);
			if (transfer != null)
			{
				transfer.SetSize((ulong)((fileStream != null && fileStream.Length < 0) ? 0 : fileStream.Length));
			}
			return await _modsBackendApiService.UploadFile(filetype, uuid, absoluteFilePath, transfer, fileStream, null, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Log(ex.Message, LogLevel.L3_Error);
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, $"Error during uploading {absoluteFilePath}", ex.Message)
			};
		}
		finally
		{
			diskLock.ReleaseWriterLock();
		}
	}

	private async Task<Result> UploadFileWithPresignedUrl(string wipGuid, string absoluteFilePath, bool isContentFile = false, IProgressController progressController = null, FlowData flowData = null)
	{
		GetPresignedUrlResult getPresignedUrl = await _modsBackendApiService.GetPresignedUrl(wipGuid, Path.GetFileName(absoluteFilePath), flowData);
		if (!getPresignedUrl.Success)
		{
			return getPresignedUrl;
		}
		TransferStatus transfer = null;
		if (isContentFile)
		{
			transfer = progressController?.GetTransferStatus(ETransferType.Upload, ETransferServiceType.Mods) ?? new TransferStatus(ETransferType.Upload, ETransferServiceType.Mods, wipGuid);
		}
		AsyncReaderWriterLock diskLock = Context.DiskFileLocks.FetchLock(absoluteFilePath);
		await diskLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			using Stream fileStream = _context.FileIO.CreateReadStream(absoluteFilePath, flowData);
			if (transfer != null)
			{
				transfer.SetSize((ulong)((fileStream != null && fileStream.Length < 0) ? 0 : fileStream.Length));
			}
			return await _modsBackendApiService.UploadFileToPresignedUrl(getPresignedUrl.PresignedUrl, absoluteFilePath, getPresignedUrl.ContentType, transfer, fileStream, null, flowData).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Log(ex.Message, LogLevel.L3_Error);
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, $"Error during uploading {absoluteFilePath}", ex.Message)
			};
		}
		finally
		{
			diskLock.ReleaseWriterLock();
		}
	}
}
