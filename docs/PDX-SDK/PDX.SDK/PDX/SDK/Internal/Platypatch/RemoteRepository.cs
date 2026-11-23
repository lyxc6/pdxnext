using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Exceptions;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Platypatch.Pgp;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;
using PDX.SDK.Internal.Platypatch.Results;
using PDX.SDK.Internal.Util;
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;
using TinyJson;

namespace PDX.SDK.Internal.Platypatch;

internal class RemoteRepository : IRemoteRepository
{
	private readonly IRequestBuilderAndResponseParserFactory _requestBuilderAndResponseParserFactory;

	private readonly IRepoPgpContext _repoPgpContext;

	private readonly IFileDownloader _fileDownloader;

	private readonly IPathFactory _pathFactory;

	private readonly IFileIntegrityVerifier _fileIntegrityVerifier;

	private readonly IMessageValidator _messageValidator;

	private readonly UrlAccessClient _urlAccessClient;

	public RemoteRepository(string repositoryName, IRequestBuilderAndResponseParserFactory requestBuilderAndResponseParserFactory, IRepoPgpContext repoPgpContext, IFileDownloader fileDownloader, IPathFactory pathFactory, IFileIntegrityVerifier fileIntegrityVerifier, IMessageValidator messageValidator)
	{
		_urlAccessClient = new UrlAccessClient(repositoryName, requestBuilderAndResponseParserFactory);
		_requestBuilderAndResponseParserFactory = requestBuilderAndResponseParserFactory;
		_repoPgpContext = repoPgpContext;
		_fileDownloader = fileDownloader;
		_pathFactory = pathFactory;
		_fileIntegrityVerifier = fileIntegrityVerifier;
		_messageValidator = messageValidator;
	}

	public async Task Initialize(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		PgpKeyMessage repoKey = await GetMessageFile<PgpKeyMessage>("_keys/public.gpg", fd).ConfigureAwait(continueOnCapturedContext: false);
		_repoPgpContext.Initialize(repoKey);
	}

	public async Task<RepoMetaData> GetRepositoryMetaData(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		return (await GetSignedFileAsString("repository.json", fd).ConfigureAwait(continueOnCapturedContext: false)).FromJson<RepoMetaData>();
	}

	public Task<FolderManifest> GetCompleteManifest(string version, FlowData flowData = null)
	{
		return GetManifest(version, "complete", flowData);
	}

	public Task<FolderManifest> GetDependenciesManifest(string version, FlowData flowData = null)
	{
		return GetManifest(version, "_dependencies", flowData);
	}

	public Task<FolderManifest> GetPatchManifest(string version, string fromVersion, FlowData flowData = null)
	{
		return GetManifest(version, fromVersion + "_to_" + version, flowData);
	}

	public async Task<DownloadFilesResult> DownloadFilesInManifest(FolderManifest manifest, string absoluteLocalRootPath, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		TransferStatus transferStatus = progressController?.GetTransferStatus(ETransferType.Download, ETransferServiceType.FolderContent) ?? new TransferStatus(ETransferType.Download, ETransferServiceType.FolderContent, manifest.RemoteFolderPath);
		List<string> downloadedFilesPaths = new List<string>();
		long size = manifest.FileEntries.Sum((ManifestFileEntry fe) => fe.Size);
		transferStatus.SetSize((ulong)size);
		ISubProgressChunk subProgress = progressController?.GetSubProcessChunk((ulong)manifest.FileEntries.Length);
		ManifestFileEntry[] fileEntries = manifest.FileEntries;
		foreach (ManifestFileEntry manifestFileEntry in fileEntries)
		{
			string remoteUrl = await _urlAccessClient.GetUrl(manifest.RemoteFolderPath + "/" + manifestFileEntry.RelativePath).ConfigureAwait(continueOnCapturedContext: false);
			string absoluteLocalPath = _pathFactory.GetPath(Path.Combine(absoluteLocalRootPath, manifestFileEntry.RelativePath));
			downloadedFilesPaths.Add(absoluteLocalPath);
			bool doNotSetTransferStateToSuccessful2 = doNotSetTransferStateToSuccessful || manifest.FileEntries.Last() != manifestFileEntry;
			if (FileAlreadyDownloaded(manifest, manifestFileEntry, absoluteLocalPath, fd))
			{
				subProgress?.RecordProgress(1uL);
				continue;
			}
			Result result = await _fileDownloader.PerformDownload(remoteUrl, absoluteLocalPath, transferStatus, fd, doNotSetTransferStateToSuccessful2).ConfigureAwait(continueOnCapturedContext: false);
			if (!result.Success)
			{
				DownloadFilesResult downloadFilesResult = result.CastTo<DownloadFilesResult>();
				downloadFilesResult.LocalPaths = downloadedFilesPaths;
				return downloadFilesResult;
			}
			if (!_fileIntegrityVerifier.CheckIntegrity(manifest, manifestFileEntry, absoluteLocalPath))
			{
				return new DownloadFilesResult
				{
					Success = false,
					Error = new Error(InternalError.InternalError, "Downloaded file (" + absoluteLocalPath + ") did not pass integrity check!", ""),
					LocalPaths = downloadedFilesPaths
				};
			}
			subProgress?.RecordProgress(1uL);
		}
		return new DownloadFilesResult
		{
			Success = true,
			LocalPaths = downloadedFilesPaths
		};
	}

	internal bool FileAlreadyDownloaded(FolderManifest folderManifest, ManifestFileEntry manifestFileEntry, string absolutePath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		return _fileIntegrityVerifier.CheckIntegrity(folderManifest, manifestFileEntry, absolutePath, flowData2);
	}

	internal async Task<string> GetFileAsString(string relativeFilePath, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		byte[] bytes = await GetFileAsBytes(relativeFilePath, flowData2).ConfigureAwait(continueOnCapturedContext: false);
		return Encoding.UTF8.GetString(bytes);
	}

	internal async Task<byte[]> GetFileAsBytes(string relativeFilePath, FlowData flowData = null)
	{
		return await GetFileInner(flowData: new FlowData(flowData), fileUri: await _urlAccessClient.GetUrl(relativeFilePath).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<byte[]> GetFileInner(string fileUri, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		if (!Uri.IsWellFormedUriString(fileUri, UriKind.Absolute) || string.IsNullOrWhiteSpace(fileUri))
		{
			throw new ExceptionWithSubCategory(InvalidNetworkResponse.UnexpectedResponseError, "Malformed URL for fileUri: \"" + fileUri + "\"");
		}
		using MemoryStream memoryStream = new MemoryStream();
		DownloadChunkHandler chunkHandler = new DownloadChunkHandler(null, 32768, memoryStream, null, doNotSetTransferStateToSuccessful: false, leaveStreamOpen: true);
		Response response = await _requestBuilderAndResponseParserFactory.CreateRequestBuilder(fd).SetHostType(HostTypes.HostType.None).SetPath(fileUri)
			.SetContentType(ContentType.OctetStream)
			.SetChunkHandler(chunkHandler)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false);
		if (response.StatusCode != 200 && response.StatusCode != 204)
		{
			throw new Exception("HTTP request to CDN did not return 200 '" + fileUri + "'");
		}
		return memoryStream.ToArray();
	}

	private async Task<T> GetMessageFile<T>(string relativePath, FlowData fd) where T : class, IPgpMessage
	{
		return AsciiArmourParser.GetMessage(await GetFileAsString(relativePath).ConfigureAwait(continueOnCapturedContext: false)) as T;
	}

	private async Task<string> GetSignedFileAsString(string relativePath, FlowData fd)
	{
		FlowData flowData = new FlowData(fd);
		byte[] fileContents = await GetFileAsBytes(relativePath, flowData).ConfigureAwait(continueOnCapturedContext: false);
		PgpSignatureMessage message = await GetMessageFile<PgpSignatureMessage>(relativePath + ".asc", flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!_messageValidator.IsValid(message, fileContents, _repoPgpContext.KeyRing))
		{
			throw new Exception("Signature verification failed, " + relativePath + " is not valid");
		}
		return Encoding.UTF8.GetString(fileContents);
	}

	private async Task<FolderManifest> GetManifest(string version, string subPath, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		string relativePath = string.Join("/", version, subPath, "manifest");
		string contents = await GetSignedFileAsString(relativePath, fd).ConfigureAwait(continueOnCapturedContext: false);
		return new FolderManifest(string.Join("/", version, subPath), contents, version);
	}
}
