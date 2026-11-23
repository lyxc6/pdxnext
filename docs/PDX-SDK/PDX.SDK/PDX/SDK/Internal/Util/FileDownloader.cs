using System;
using System.IO;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Util;
using PDX.SDK.Events;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Platypatch;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

internal class FileDownloader : IFileDownloader
{
	private readonly IRequestBuilderAndResponseParserFactory _requestBuilderAndResponseParserFactory;

	private readonly IFileIO _fileIo;

	private readonly FileLocks _fileLocks;

	private readonly ICancellationTokenRegister _cancellationTokenRegister;

	private readonly IInternalEventHub _internalEventHub;

	private readonly ILogger _logger;

	private readonly int _chunkSize;

	public FileDownloader(IRequestBuilderAndResponseParserFactory requestBuilderAndResponseParserFactory, IFileIO fileIo, FileLocks fileLocks, ICancellationTokenRegister cancellationTokenRegister, IInternalEventHub internalEventHub = null, ILogger logger = null, int chunkSize = 32768)
	{
		_requestBuilderAndResponseParserFactory = requestBuilderAndResponseParserFactory;
		_fileIo = fileIo;
		_fileLocks = fileLocks;
		_cancellationTokenRegister = cancellationTokenRegister;
		_internalEventHub = internalEventHub;
		_logger = logger;
		_chunkSize = chunkSize;
	}

	public async Task<Result> PerformDownload(string remoteUrl, string localAbsolutePath, TransferStatus downloadTransfer = null, FlowData flowData = null, bool doNotSetTransferStateToSuccessful = false, CancellationToken cancellationToken = default(CancellationToken))
	{
		FlowData fd = new FlowData(flowData);
		_logger?.Log("FileDownloader.PerformDownload", LogLevel.L0_Info, fd);
		if (!Uri.IsWellFormedUriString(remoteUrl, UriKind.Absolute) || string.IsNullOrWhiteSpace(remoteUrl))
		{
			if (downloadTransfer != null)
			{
				downloadTransfer.State = ETransferState.Failed;
			}
			return new Result
			{
				Success = false,
				Error = new Error(InvalidNetworkResponse.UnexpectedResponseError, $"Malformed URL for repository path: \"{remoteUrl}\"", "")
			};
		}
		AsyncReaderWriterLock readerWriterLock = _fileLocks.FetchLock(localAbsolutePath);
		await readerWriterLock.AcquireWriterLock(cancellationToken);
		CancellationToken registeredCancellationToken = _cancellationTokenRegister.GetToken(CancellationTokenRegistrationType.Download, fd);
		CancellationTokenSource cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, registeredCancellationToken);
		try
		{
			using Stream fileStream = (_fileIo as FileIO).CreateWriteStream(localAbsolutePath, fd);
			DownloadChunkHandler chunkHandler = new DownloadChunkHandler(downloadTransfer, _chunkSize, fileStream, localAbsolutePath, doNotSetTransferStateToSuccessful, leaveStreamOpen: false, _internalEventHub);
			Response response = await _requestBuilderAndResponseParserFactory.CreateRequestBuilder(fd).SetHostType(HostTypes.HostType.None).SetPath(remoteUrl)
				.SetContentType(ContentType.OctetStream)
				.SetChunkHandler(chunkHandler)
				.SetCancellationToken(cancellationTokenSource.Token)
				.Send()
				.ConfigureAwait(continueOnCapturedContext: false);
			if (response.StatusCode == 200)
			{
				return new Result
				{
					Success = true
				};
			}
			return _requestBuilderAndResponseParserFactory.CreateResponseParser(response, fd).GetResultObject();
		}
		catch (Exception ex)
		{
			_logger?.Log(ex.Message, LogLevel.L3_Error, fd);
			InternalError internalError = InternalError.FileIOError;
			if (ex is IOException ex2 && (ex2.HResult == -2147024857 || ex2.HResult == -2147024784))
			{
				internalError = InternalError.DiskFullError;
			}
			else if (ex is UnauthorizedAccessException || ex is SecurityException)
			{
				internalError = InternalError.DiskPermissionError;
			}
			return new Result
			{
				Success = false,
				Error = new Error(internalError, $"Error during downloading {remoteUrl}", ex.Message)
			};
		}
		finally
		{
			IDiskIO diskIO = (_fileIo as FileIO).DiskIO;
			if (diskIO is DiskIODefaultWindows && (diskIO as DiskIODefaultWindows).FileHandle != null)
			{
				(diskIO as DiskIODefaultWindows).FileHandle.Dispose();
				(diskIO as DiskIODefaultWindows).FileHandle = null;
			}
			readerWriterLock.ReleaseWriterLock();
			_cancellationTokenRegister.RemoveToken(registeredCancellationToken);
		}
	}
}
