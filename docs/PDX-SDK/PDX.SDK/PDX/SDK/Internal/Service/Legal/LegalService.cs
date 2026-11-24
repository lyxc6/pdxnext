using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Legal;
using PDX.SDK.Contracts.Service.Legal.Models;
using PDX.SDK.Contracts.Service.Legal.Result;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Service.Legal.Models;
using PDX.SDK.Internal.Service.Legal.Response;
using PDX.SDK.Internal.Service.Legal.Result;
using PDX.SDK.Internal.Service.Telemetry;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Legal;

internal class LegalService : InternalService, ILegalService
{
	private readonly List<string> DocumentsToTrackLocally;

	internal LegalService(Context context)
		: base("Legal", context)
	{
		DocumentsToTrackLocally = new List<string>();
	}

	public async Task<PDX.SDK.Contracts.Result> MarkAsViewed(Document document)
	{
		return await MarkAsViewed(document.Title, document.Type).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> MarkAsViewed(string title, string type)
	{
		FlowData fd = new FlowData();
		Log("Legal.MarkAsViewed", LogLevel.L1_Debug, fd);
		if (!(await _context.StateCache.Machine.AddViewedDocument(title).ConfigureAwait(continueOnCapturedContext: false)).Status.Success)
		{
			_context.Log("Saving Legal Viewed Documents from Cache to Disk failed", LogLevel.L2_Warning);
		}
		bool flag = !DocumentsToTrackLocally.Contains(title);
		if (flag)
		{
			flag = await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false);
		}
		if (flag && !(await PerformMarkAsViewed(title, fd)).Success)
		{
			_context.Log("Marking Legal Viewed Documents in the backend failed", LogLevel.L2_Warning);
		}
		if (!(await _context.StateCache.Player.RemoveDocument(title, type).ConfigureAwait(continueOnCapturedContext: false)).NewState.UnviewedDocuments.Any() && _context.Telemetry is TelemetryService)
		{
			await (_context.Telemetry as TelemetryService).SetAllLegalDocumentsViewedDone();
		}
		return PDX.SDK.Contracts.Result.ResultSuccessful();
	}

	public async Task<ListUnviewedResult> ListUnviewed()
	{
		return await ListUnviewed(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDocumentResult> GetPrivacy(Language? lang = null)
	{
		FlowData flowData = new FlowData();
		Log("Legal.GetPrivacy", LogLevel.L1_Debug, flowData);
		return await GetDocumentByType("privacy", lang, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDocumentResult> GetEula(Language? lang = null)
	{
		FlowData flowData = new FlowData();
		Log("Legal.GetEula", LogLevel.L1_Debug, flowData);
		return await GetDocumentByType("eula", lang, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetAllDocumentResult> GetNotices(Language? lang = null)
	{
		FlowData flowData = new FlowData();
		Log("Legal.GetNotices", LogLevel.L1_Debug, flowData);
		return await GetAllOfType("notice", (lang ?? _context.Config.Language).Value, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDocumentResult> DownloadDocument(string title, string type, Language? lang = null)
	{
		return await DownloadDocument(title, type, lang, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ListUnviewedResult> ListUnviewed(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Legal.ListUnviewed", LogLevel.L1_Debug, fd);
		ListAllDocumentsResult allDocuments = await ListAll(fd);
		if (!allDocuments.Success)
		{
			return allDocuments.CastTo<ListUnviewedResult>();
		}
		GetViewedResult viewed = await GetViewed(fd);
		if (!viewed.Success)
		{
			return viewed.CastTo<ListUnviewedResult>();
		}
		GetDocumentResult[] source = await Task.WhenAll((from document in allDocuments.Documents
			where document.Required
			where !viewed.DocumentTitles.Any((string title) => document.Title == title)
			select document).Select(delegate(DocumentFrontpage document)
		{
			LegalService legalService = this;
			FlowData flowData2 = fd;
			return legalService.DownloadDocument(document, null, flowData2);
		})).ConfigureAwait(continueOnCapturedContext: false);
		GetDocumentResult getDocumentResult = source.FirstOrDefault((GetDocumentResult result) => !result.Success);
		if (getDocumentResult != null)
		{
			return getDocumentResult.CastTo<ListUnviewedResult>();
		}
		ListUnviewedResult unviewedResults = new ListUnviewedResult
		{
			Success = true,
			Documents = source.Select((GetDocumentResult result) => result.Document).ToList()
		};
		if (unviewedResults.Documents.Count == 0 && _context.Telemetry is TelemetryService)
		{
			await (_context.Telemetry as TelemetryService).SetAllLegalDocumentsViewedDone();
		}
		return unviewedResults;
	}

	internal async Task<GetDocumentResult> DownloadDocument(string title, string type, Language? lang = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Legal.DownloadDocument", LogLevel.L1_Debug, fd);
		PDXResponseParser<GetDocumentResponse> pDXResponseParser = CreateResponseParser<GetDocumentResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("legal/documents").AddToPath(type)
			.AddToPath(title)
			.AddParameterToPath("language", (lang ?? _context.Config.Language).ToIetfTagLanguageString())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetDocumentResponse response = pDXResponseParser.GetResponse();
		GetDocumentResult resultObject = pDXResponseParser.GetResultObject<GetDocumentResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		Document document = new Document();
		if (response.document != null)
		{
			DocumentResponse document2 = response.document;
			document.Title = document2.title;
			document.Type = document2.type;
			document.Text = document2.text;
			document.Language = IsoExtensionMethods.GetLanguage(document2.language);
			document.URL = document2.url;
		}
		resultObject.Document = document;
		return resultObject;
	}

	private async Task<GetDocumentResult> GetDocumentByType(string type, Language? lang = null, FlowData fd = null)
	{
		GetAllDocumentResult getAllDocumentResult = await GetAllOfType(type, (lang ?? _context.Config.Language).Value, fd).ConfigureAwait(continueOnCapturedContext: false);
		GetDocumentResult getDocumentResult = getAllDocumentResult.CastTo<GetDocumentResult>();
		getDocumentResult.Document = getAllDocumentResult.Documents?.FirstOrDefault();
		return getDocumentResult;
	}

	private async Task<GetViewedResult> GetViewed(FlowData flowData = null)
	{
		GetViewedResult resultViewed = new GetViewedResult
		{
			Success = true,
			DocumentTitles = new List<string>()
		};
		IMachineData machineData = await _context.StateCache.GetMachineData().ConfigureAwait(continueOnCapturedContext: false);
		resultViewed.DocumentTitles.AddRange(machineData.ViewedDocuments);
		if (await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false))
		{
			PDXResponseParser<LegalViewedResponse> pDXResponseParser = CreateResponseParser<LegalViewedResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("legal/documents/viewed").SetAuthHeader()
				.Send()
				.ConfigureAwait(continueOnCapturedContext: false), flowData);
			LegalViewedResponse response = pDXResponseParser.GetResponse();
			GetViewedResult resultObject = pDXResponseParser.GetResultObject<GetViewedResult>();
			if (!resultObject.Success)
			{
				if (resultObject.Error != BaseCategory.NotAuthorized)
				{
					return resultObject;
				}
			}
			else
			{
				if (response.viewed != null)
				{
					string[] viewed = response.viewed;
					foreach (string title in viewed)
					{
						bool flag = DocumentsToTrackLocally.Contains(title);
						if (!resultViewed.DocumentTitles.Exists((string e) => e == title) && !flag)
						{
							resultViewed.DocumentTitles.Add(title);
						}
					}
				}
				await SyncLocallyViewedDocuments(resultViewed.DocumentTitles, response.viewed).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return resultViewed;
	}

	private async Task SyncLocallyViewedDocuments(IEnumerable<string> documentsViewedLocally, IEnumerable<string> documentsViewedOnline)
	{
		IEnumerable<string> enumerable = documentsViewedLocally.Where((string d) => !documentsViewedOnline.Contains(d) && !DocumentsToTrackLocally.Contains(d));
		List<Task> list = new List<Task>();
		foreach (string item in enumerable)
		{
			list.Add(PerformMarkAsViewed(item));
		}
		await Task.WhenAll(list).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<PDX.SDK.Contracts.Result> PerformMarkAsViewed(string title, FlowData flowData = null)
	{
		return CreateResponseParser(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.PUT).SetPath("legal/documents/viewed").AddParameterToPath("title", title)
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData).GetResultObject();
	}

	internal async Task<ListAllDocumentsResult> ListAll(FlowData flowData = null)
	{
		PDXResponseParser<DocumentListResponse> pDXResponseParser = CreateResponseParser<DocumentListResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("legal/documents/list").AddHeader("X-Accept-Version", "2")
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData);
		DocumentListResponse response = pDXResponseParser.GetResponse();
		ListAllDocumentsResult resultObject = pDXResponseParser.GetResultObject<ListAllDocumentsResult>();
		if (!resultObject.Success || response.documents == null)
		{
			return resultObject;
		}
		resultObject.Documents = new List<DocumentFrontpage>();
		SimplifiedDocumentResponse[] documents = response.documents;
		foreach (SimplifiedDocumentResponse simplifiedDocumentResponse in documents)
		{
			DocumentFrontpage documentFrontpage = new DocumentFrontpage
			{
				Title = simplifiedDocumentResponse.title,
				Type = simplifiedDocumentResponse.type,
				Required = simplifiedDocumentResponse.required,
				ShowOncePerInstall = simplifiedDocumentResponse.showOncePerInstall
			};
			resultObject.Documents.Add(documentFrontpage);
			if (documentFrontpage.ShowOncePerInstall && !DocumentsToTrackLocally.Contains(documentFrontpage.Title))
			{
				DocumentsToTrackLocally.Add(documentFrontpage.Title);
			}
		}
		return resultObject;
	}

	private async Task<GetAllDocumentResult> GetAllOfType(string type, Language lang, FlowData flowData = null)
	{
		PDXResponseParser<GetAllDocumentsResponse> pDXResponseParser = CreateResponseParser<GetAllDocumentsResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("legal/documents").AddToPath(type)
			.AddParameterToPath("language", lang.ToIetfTagLanguageString())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData);
		GetAllDocumentsResponse response = pDXResponseParser.GetResponse();
		GetAllDocumentResult resultObject = pDXResponseParser.GetResultObject<GetAllDocumentResult>();
		resultObject.Documents = new List<Document>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		if (response.documents != null)
		{
			DocumentResponse[] documents = response.documents;
			foreach (DocumentResponse documentResponse in documents)
			{
				Document item = new Document
				{
					Title = documentResponse.title,
					Type = documentResponse.type,
					Text = documentResponse.text,
					Language = IsoExtensionMethods.GetLanguage(documentResponse.language),
					URL = documentResponse.url
				};
				resultObject.Documents.Add(item);
			}
		}
		return resultObject;
	}

	private async Task<GetDocumentResult> DownloadDocument(DocumentFrontpage document, Language? lang = null, FlowData flowData = null)
	{
		return await DownloadDocument(document.Title, document.Type, lang, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}
}
