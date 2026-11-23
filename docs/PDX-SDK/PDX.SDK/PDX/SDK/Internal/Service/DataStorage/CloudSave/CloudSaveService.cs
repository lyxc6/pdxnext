using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave.Models;
using PDX.SDK.Contracts.Service.DataStorage.CloudSave.Result;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Service.DataStorage.CloudSave.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.DataStorage.CloudSave;

internal class CloudSaveService : InternalService, ICloudSaveService
{
	private readonly IPathFactory _pathFactory;

	private const int MaxUploadSize = 104857600;

	internal CloudSaveService(Context context, IPathFactory pathFactory)
		: base("DataStorage.CloudSave", context)
	{
		_pathFactory = pathFactory;
	}

	public async Task<Result> Delete(Entry entry)
	{
		return await Delete(entry.Path, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(string remotePath)
	{
		return await Delete(remotePath, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(IEnumerable<Entry> entries)
	{
		return await Delete(entries, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Delete(params string[] remotePaths)
	{
		return await Delete(remotePaths, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ListEntriesResult> ListEntries()
	{
		return await ListEntries(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<UploadResult> Upload(string relativePath, string remotePath, bool @public = false)
	{
		return await Upload(relativePath, remotePath, @public, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<DownloadResult> Download(string remotePath, string relativePath, string etag = null)
	{
		return await Download(remotePath, relativePath, etag, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Delete(string remotePath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.CloudSave.Delete", LogLevel.L0_Info, flowData2);
		return await PerformDelete(remotePath, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Delete(IEnumerable<string> remotePaths, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.CloudSave.Delete", LogLevel.L0_Info, flowData2);
		return await remotePaths.Select((string path) => Delete(path)).ToArray().GetResultFromTasks()
			.ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Delete(IEnumerable<Entry> entries, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.CloudSave.Delete", LogLevel.L1_Debug, flowData2);
		return await entries.Select((Entry e) => Delete(e.Path)).ToArray().GetResultFromTasks()
			.ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ListEntriesResult> ListEntries(FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ListEntriesResult>("retrieve a list of cloud saves");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.CloudSave.ListEntries", LogLevel.L0_Info, fd);
		PDXResponseParser<ListResponse> pDXResponseParser = CreateResponseParser<ListResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("storage/blob/v2/list").AddNamespaceToPath()
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ListResponse response = pDXResponseParser.GetResponse();
		ListEntriesResult resultObject = pDXResponseParser.GetResultObject<ListEntriesResult>();
		resultObject.Entries = new List<Entry>();
		if (!resultObject.Success || response.list == null || response.list.Length == 0)
		{
			return resultObject;
		}
		EntryResponse[] list = response.list;
		foreach (EntryResponse entryResponse in list)
		{
			resultObject.Entries.Add(new Entry
			{
				Hash = entryResponse.etag,
				Path = entryResponse.path,
				IsPublic = entryResponse.@public,
				UserAgent = entryResponse.user_agent,
				Modified = ParseDateTime(entryResponse.modified, "CloudSave.List", "entry.modified", fd),
				Id = entryResponse.id,
				IsOfficial = entryResponse.official,
				Owner = entryResponse.owner,
				ContentLength = entryResponse.content_length,
				Game = entryResponse.game,
				Created = ParseDateTime(entryResponse.created, "CloudSave.List", "entry.created", fd)
			});
		}
		return resultObject;
	}

	internal async Task<UploadResult> Upload(string relativePath, string remotePath, bool @public = false, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.CloudSave.Upload", LogLevel.L0_Info, flowData2);
		return await PerformUpload(relativePath, remotePath, @public, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<UploadResult> PerformUpload(string relativePath, string remotePath, bool @public = false, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<UploadResult>("upload a file to cloud save");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.CloudSave.PerformUpload", LogLevel.L0_Info, fd);
		if (!_context.FileIO.PathExists(_pathFactory.GetCloudSavesPath(relativePath)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoDocument, string.Format(Defaults.Culture, "The local file \"{0}\" does not exist", relativePath), "")
			}.CastTo<UploadResult>();
		}
		if (!Uri.IsWellFormedUriString(remotePath, UriKind.Relative) || string.IsNullOrWhiteSpace(remotePath))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, string.Format(Defaults.Culture, "Parameter 'remotePath' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{0}\"", remotePath), "")
			}.CastTo<UploadResult>();
		}
		string fileAbsolutePath = _pathFactory.GetCloudSavesPath(relativePath);
		PDX.SDK.Network.Models.Response response = null;
		TransferStatus transfer = new TransferStatus(ETransferType.Upload, ETransferServiceType.CloudSaves, remotePath);
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(fileAbsolutePath);
		await readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		using (Stream fileStream = _context.FileIO.CreateReadStream(fileAbsolutePath, fd))
		{
			_ = 2;
			try
			{
				if (fileStream.Length > 104857600)
				{
					return new Result
					{
						Success = false,
						Error = new Error(DiskIOError.MaximumFileSizeReached, "Maximum file size reached", "")
					}.CastTo<UploadResult>();
				}
				transfer.SetSize((ulong)((fileStream.Length < 0) ? 0 : fileStream.Length));
				UploadChunkHandler chunkHandler = new UploadChunkHandler(transfer, _context.Config.ChunkSize.Value, fileStream, fileAbsolutePath);
				response = await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("storage/blob/v2").AddNamespaceToPath()
					.AddToPath(remotePath)
					.AddParameterToPath("public", @public.ToString())
					.SetAuthHeader()
					.SetContentType(ContentType.OctetStream)
					.SetChunkHandler(chunkHandler)
					.Send()
					.ConfigureAwait(continueOnCapturedContext: false);
			}
			finally
			{
				readerWriterLock.ReleaseWriterLock();
			}
		}
		PDXResponseParser<UploadResponse> pDXResponseParser = CreateResponseParser<UploadResponse>(response, fd);
		UploadResponse response2 = pDXResponseParser.GetResponse();
		UploadResult resultObject = pDXResponseParser.GetResultObject<UploadResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		foreach (string key in response.Headers.Keys)
		{
			if (key.ToLower().Equals("etag"))
			{
				resultObject.Etag = response.Headers[key];
				break;
			}
		}
		resultObject.Url = response2.uri;
		return resultObject;
	}

	internal async Task<DownloadResult> Download(string remotePath, string relativePath, string etag = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("DataStorage.CloudSave.Download", LogLevel.L1_Debug, flowData2);
		DownloadResult downloadResult = await PerformDownload(remotePath, relativePath, etag, flowData2).ConfigureAwait(continueOnCapturedContext: false);
		if (downloadResult.Success)
		{
			downloadResult.RelativePath = relativePath;
		}
		return downloadResult;
	}

	private async Task<Result> PerformDelete(string remotePath, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("delete a file from cloud");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.CloudSave.PerformDelete", LogLevel.L0_Info, fd);
		if (!Uri.IsWellFormedUriString(remotePath, UriKind.Relative) || string.IsNullOrWhiteSpace(remotePath))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, $"Parameter 'remotePath' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{remotePath}\"", "")
			};
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("storage/blob/v2").AddNamespaceToPath()
			.AddToPath(remotePath)
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	private async Task<DownloadResult> PerformDownload(string remotePath, string relativePath, string etag = null, FlowData flowData = null)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<DownloadResult>("download a cloud save");
		}
		FlowData fd = new FlowData(flowData);
		Log("DataStorage.CloudSave.Download", LogLevel.L0_Info, fd);
		if (!Uri.IsWellFormedUriString(remotePath, UriKind.Relative))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, $"Parameter 'remotePath' is invalid format and should be in accordance with RFC 3986 and RFC 3987: \"{remotePath}\"", "")
			}.CastTo<DownloadResult>();
		}
		string fileAbsolutePath = _pathFactory.GetCloudSavesPath(relativePath);
		AsyncReaderWriterLock readerWriterLock = Context.DiskFileLocks.FetchLock(fileAbsolutePath);
		await readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
		PDX.SDK.Network.Models.Response response = null;
		TransferStatus transfer = new TransferStatus(ETransferType.Download, ETransferServiceType.CloudSaves, remotePath);
		try
		{
			using Stream fileStream = _context.FileIO.CreateWriteStream(fileAbsolutePath, fd);
			DownloadChunkHandler chunkHandler = new DownloadChunkHandler(transfer, _context.Config.ChunkSize.Value, fileStream, fileAbsolutePath);
			response = await CreateRequestBuilder(fd).SetPath("storage/blob/v2").AddNamespaceToPath().AddToPath(remotePath)
				.SetAuthHeader()
				.AddHeader("If-None-Match", etag)
				.SetContentType(ContentType.OctetStream)
				.SetChunkHandler(chunkHandler)
				.Send()
				.ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception ex)
		{
			Log(ex.Message, LogLevel.L3_Error);
			InternalError internalError = InternalError.FileIOError;
			if (ex is UnauthorizedAccessException || ex is SecurityException)
			{
				internalError = InternalError.DiskPermissionError;
			}
			return new Result
			{
				Success = false,
				Error = new Error(internalError, $"Error downloading {remotePath}", ex.Message)
			}.CastTo<DownloadResult>();
		}
		finally
		{
			readerWriterLock.ReleaseWriterLock();
		}
		if (response.StatusCode != 304)
		{
			DownloadResult resultObject = CreateResponseParser<DownloadResponse>(response, fd).GetResultObject<DownloadResult>();
			if (!resultObject.Success)
			{
				return resultObject;
			}
			resultObject.Size = (int)transfer.ProcessedBytes;
			foreach (string key in response.Headers.Keys)
			{
				if (key.ToLower().Equals("etag"))
				{
					resultObject.Etag = response.Headers[key];
					break;
				}
			}
			return resultObject;
		}
		return new DownloadResult
		{
			Success = true,
			Size = 0,
			Etag = etag
		};
	}

	internal async Task CleanUpCloudSavesFromDisk()
	{
		OperationResult<List<string>> operationResult = await _context.FileIO.ListFiles(_pathFactory.GetCloudSavesPath()).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult.Status.Success)
		{
			return;
		}
		foreach (string item in operationResult.Result)
		{
			await _context.FileIO.Delete(item).ConfigureAwait(continueOnCapturedContext: false);
		}
	}
}
