using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Response;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Enums;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Service.Mods.Request;
using PDX.SDK.Internal.Service.Mods.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;
using PDX.SDK.Network.Models;
using PDX.SDK.Network.Util;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsBackendApiService : InternalService, IModsBackendApiService
{
	private KeyValuePair<string, string> ModsV2Header = new KeyValuePair<string, string>("X-Accept-Version", "2");

	[Preserve]
	public ModsBackendApiService(IContext context)
		: base("ModsBackendApiService", context as Context)
	{
	}

	public async Task<Result> MarkAsEnabled(int modId, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.MarkAsEnabled", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("enable a mod");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("mods/enable").AddParameterToPath("modId", modId.ToString())
			.AddParameterToPath("activationSetId", playsetId.ToString())
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> MarkAsDisabled(int modId, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.MarkAsDisabled", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("disable a mod");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("mods/disable").AddParameterToPath("modId", modId.ToString())
			.AddParameterToPath("activationSetId", playsetId.ToString())
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> EnableBulk(List<int> modIds, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.EnableBulk", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("enable multiple mods");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/enableBulk").AddParameterToPath("activationSetId", playsetId.ToString())
			.SetAuthHeader()
			.SetBody(modIds.ToArray())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> DisableBulk(List<int> modIds, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DisableBulk", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("disable multiple mods");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/disableBulk").AddParameterToPath("activationSetId", playsetId.ToString())
			.SetAuthHeader()
			.SetBody(modIds.ToArray())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<MarkAsSubscribedResult> MarkAsSubscribed(IEnumerable<KeyValuePair<int, string>> mods, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.MarkAsSubscribed", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<MarkAsSubscribedResult>("subscribe to mods");
		}
		List<ModSubscribeRequest> list = new List<ModSubscribeRequest>();
		foreach (KeyValuePair<int, string> mod in mods)
		{
			ModSubscribeRequest modSubscribeRequest = new ModSubscribeRequest
			{
				id = mod.Key,
				version = null
			};
			if (int.TryParse(mod.Value, out var result))
			{
				modSubscribeRequest.version = result;
			}
			list.Add(modSubscribeRequest);
		}
		MarkAsSubscribedRequest body = new MarkAsSubscribedRequest
		{
			activationSetId = playsetId,
			mods = list.ToArray()
		};
		PDXResponseParser<SubscribeResponse> pDXResponseParser = CreateResponseParser<SubscribeResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/installed").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		SubscribeResponse response = pDXResponseParser.GetResponse();
		MarkAsSubscribedResult resultObject = pDXResponseParser.GetResultObject<MarkAsSubscribedResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModsSubscribed = response.mods?.Select((SubscribeModResponse m) => m.ToModSubscribed()).ToArray();
		return resultObject;
	}

	public async Task<Result> MarkAsUnsubscribed(int modId, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.MarkAsUnsubscribed", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("unsubscribe from a mod");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("mods/installed").AddParameterToPath("modId", modId.ToString())
			.AddParameterToPath("activationSetId", playsetId.ToString())
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<GetDetailsResult> GetDetails(int modId, string version = null, string os = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Details", LogLevel.L1_Debug, fd);
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods").AddParameterToPath("os", os)
			.AddParameterToPath("arch", "Any")
			.AddParameterToPath("modId", modId.ToString())
			.AddParameterToPath("modVersion", version);
		if (await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false))
		{
			requestBuilder.SetAuthHeader();
		}
		PDXResponseParser<DetailsFilterResponse> pDXResponseParser = CreateResponseParser<DetailsFilterResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		DetailsFilterResponse response = pDXResponseParser.GetResponse();
		GetDetailsResult resultObject = pDXResponseParser.GetResultObject<GetDetailsResult>();
		if (!resultObject.Success || response.modDetail == null)
		{
			return resultObject;
		}
		if (response.modDetail.modId.ParseIntOrDefault() == -1)
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidNetworkResponse.UnexpectedResponseError, $"Mod ID is not a number", "")
			}.CastTo<GetDetailsResult>();
		}
		resultObject.Mod = response.modDetail.ToModDetails(_context.Logger, fd);
		return resultObject;
	}

	public async Task<GetForumThreadResult> GetForumThreadResult(int modId, string modVersion, int threadId, int page, int limit, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetForumThreadResult", LogLevel.L1_Debug, fd);
		PDXResponseParser<ForumThreadResponse> pDXResponseParser = CreateResponseParser<ForumThreadResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath($"mods/{modId}/versions/{modVersion}/forums/threads/{threadId}/posts").AddParameterToPath("page", page.ToString())
			.AddParameterToPath("limit", limit.ToString())
			.SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ForumThreadResponse response = pDXResponseParser.GetResponse();
		GetForumThreadResult resultObject = pDXResponseParser.GetResultObject<GetForumThreadResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.FirstPost = response.firstPost?.ToForumPost();
		resultObject.Posts = response.posts?.Select((ForumPostResponse p) => p.ToForumPost()).ToArray();
		resultObject.Count = response.count;
		resultObject.CanPost = response.canPost;
		return resultObject;
	}

	public async Task<CreateForumPostResult> CreateForumPost(int modId, string modVersion, int threadId, string message, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.CreateForumPost", LogLevel.L1_Debug, fd);
		PDXResponseParser<CreateForumPostResponse> pDXResponseParser = CreateResponseParser<CreateForumPostResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath($"mods/{modId}/versions/{modVersion}/forums/threads/{threadId}/posts").SetBody("{ \"message\": \"" + message + "\" }")
			.SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		CreateForumPostResponse response = pDXResponseParser.GetResponse();
		CreateForumPostResult resultObject = pDXResponseParser.GetResultObject<CreateForumPostResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Post = new ForumPost
		{
			Message = response.post.message,
			Username = response.post.username,
			UserId = response.post.userId,
			UserTitle = response.post.userTitle,
			Url = response.post.url,
			Created = response.post.created,
			PostId = response.post.postId
		};
		return resultObject;
	}

	public async Task<GetGameDataResult> GetGameData(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetGameData", LogLevel.L1_Debug, fd);
		string text = new GraphQLRequestBuilder(typeof(GameDataRequest)).Build();
		GraphQLRequest graphQLRequest = new GraphQLRequest();
		graphQLRequest.query = "{ game(id: \"" + _context.Namespace + "\" ) " + text + " }";
		GraphQLRequest body = graphQLRequest;
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).AddHeader("Accept-Language", _context.Config.Language.ToIetfTagLanguageString()).SetPath("metadata/graphql")
			.SetBody(body);
		if (await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false))
		{
			requestBuilder.SetAuthHeader();
		}
		PDXResponseParser<GraphQLResponse<GetGameDataResponse>> pDXResponseParser = CreateResponseParser<GraphQLResponse<GetGameDataResponse>>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true);
		GraphQLResponse<GetGameDataResponse> response = pDXResponseParser.GetResponse();
		GetGameDataResult resultObject = pDXResponseParser.GetResultObject<GetGameDataResult>();
		if (!resultObject.Success || response.data == null || response.data.game == null)
		{
			string details = "No game data were returned";
			if (response != null && response.errors != null)
			{
				details = string.Join(", ", response.errors.Select((GraphQLResponseError e) => e.message));
			}
			resultObject.Success = false;
			resultObject.Error = new Error(InternalError.InternalError, details, string.Empty);
			return resultObject;
		}
		GameDataResponse game = response.data.game;
		resultObject.Name = game.name;
		resultObject.Extension = game.extension;
		resultObject.Tags = ((IEnumerable<InternalModTag>)game.tags).ToDictionary((Func<InternalModTag, string>)((InternalModTag t) => t.id), (Func<InternalModTag, ITag>)((InternalModTag t) => new ModTag
		{
			Id = t.id,
			DisplayName = t.displayName
		}));
		resultObject.Actions = new GameActions
		{
			Install = game.actions.install,
			Enable = game.actions.enable,
			Rate = game.actions.rate,
			Publish = game.actions.publish,
			Download = game.actions.download,
			Report = game.actions.report,
			SelectVersion = game.actions.selectVersion,
			SetActivationOrder = game.actions.setActivationOrder,
			ActivationSets = game.actions.activationSets,
			ModDependencies = game.actions.modDependencies,
			View = game.actions.view,
			MultipleForumThreads = game.actions.multipleForumThreads,
			LooseModFilesSupport = game.actions.looseModFilesSupport
		};
		return resultObject;
	}

	public async Task<GetModsGameDataResult> GetModsGameData(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetModsGameData", LogLevel.L1_Debug, fd);
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).AddHeader("Accept-Language", _context.Config.Language.ToIetfTagLanguageString()).SetPath("mods/games/" + _context.Namespace);
		if (await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false))
		{
			requestBuilder.SetAuthHeader();
		}
		PDXResponseParser<GetModsGameDataResponse> pDXResponseParser = CreateResponseParser<GetModsGameDataResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		GetModsGameDataResponse response = pDXResponseParser.GetResponse();
		GetModsGameDataResult resultObject = pDXResponseParser.GetResultObject<GetModsGameDataResult>();
		if (!resultObject.Success || response?.game == null)
		{
			return resultObject;
		}
		ModsGameDataResponse game = response.game;
		resultObject.Filters.Tags = game.filters?.tags?.Select((InternalModNode internalModNode) => internalModNode.ToModTagNode()).ToList();
		return resultObject;
	}

	public async Task<GetModVersionsResult> GetVersions(int modId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetVersions", LogLevel.L1_Debug, fd);
		PDXResponseParser<ModVersionResponse> pDXResponseParser = CreateResponseParser<ModVersionResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/versions").AddParameterToPath("modId", modId.ToString())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ModVersionResponse response = pDXResponseParser.GetResponse();
		GetModVersionsResult resultObject = pDXResponseParser.GetResultObject<GetModVersionsResult>();
		if (!resultObject.Success || response.modVersions == null)
		{
			return resultObject;
		}
		if (response.modVersions.Count == 0)
		{
			return new GetModVersionsResult
			{
				Success = false,
				Error = new Error(NotFound.NoMod, string.Format(Defaults.Culture, "The mod id {0} does not exist", modId), "")
			};
		}
		resultObject.Versions = new List<ModVersionResult>();
		foreach (VersionResponse modVersion in response.modVersions)
		{
			resultObject.Versions.Add(new ModVersionResult
			{
				Id = modVersion.id.ToString(),
				Size = modVersion.size,
				CreatedDate = ParseDateTime(modVersion.created, "Mods.GetVersions", "version.created", fd)
			});
		}
		return resultObject;
	}

	public async Task<ModSearchResult> Search(SearchData searchData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Search", LogLevel.L1_Debug, fd);
		bool flag = await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false);
		if (searchData.onlyModsByMe == true && !flag)
		{
			return NotLoggedIn<ModSearchResult>("search for mods created by you");
		}
		SearchRequest body = searchData.ToSearchRequest(_context.Namespace, _context.Platform.GetModsPlatform().ToModsPlatformString());
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods").AddParameterToPath("metadata", "yes")
			.SetBody(body)
			.SetContentType(ContentType.JSON);
		if (flag)
		{
			requestBuilder.SetAuthHeader();
		}
		PDXResponseParser<SearchResponse> pDXResponseParser = CreateResponseParser<SearchResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		SearchResponse response = pDXResponseParser.GetResponse();
		ModSearchResult resultObject = pDXResponseParser.GetResultObject<ModSearchResult>();
		if (!resultObject.Success || response.mods == null || response.totalCount == 0)
		{
			return resultObject;
		}
		resultObject.TotalCount = response.totalCount;
		resultObject.Mods = response.mods.Select((SearchModResponse m) => m.ToModSearch(_context.Logger, fd)).ToList();
		return resultObject;
	}

	public async Task<Result> Report(int modId, ReportReason reason, string description, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Report", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("report a mod");
		}
		ReportRequest body = new ReportRequest
		{
			modId = modId.ToString(),
			reason = reason.GetReportReasonMod(),
			description = description
		};
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/report").SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<RateResult> Rate(int modId, int rating, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Rate", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<RateResult>("rate a mod");
		}
		UserRatingRequest body = new UserRatingRequest
		{
			rating = rating
		};
		PDXResponseParser<RatingResponse> pDXResponseParser = CreateResponseParser<RatingResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/rating").AddParameterToPath("modId", modId.ToString())
			.SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		RatingResponse response = pDXResponseParser.GetResponse();
		RateResult resultObject = pDXResponseParser.GetResultObject<RateResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.AverageRating = response.rating;
		return resultObject;
	}

	public async Task<GetUserRatingResult> GetUserRating(int modId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetUserRating", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetUserRatingResult>("get the user rating for a mod");
		}
		PDXResponseParser<RatingResponse> pDXResponseParser = CreateResponseParser<RatingResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/rating").AddParameterToPath("modId", modId.ToString())
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		RatingResponse response = pDXResponseParser.GetResponse();
		GetUserRatingResult resultObject = pDXResponseParser.GetResultObject<GetUserRatingResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Rating = response.rating;
		return resultObject;
	}

	public async Task<ModDependenciesResult> GetAllDependencies(List<int> mods, int? playsetId = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetDependencies", LogLevel.L1_Debug, fd);
		string text = "mods/dependenciesDetails";
		if (playsetId.HasValue)
		{
			text += $"?activationSetId={playsetId.Value}";
		}
		PDXResponseParser<DependenciesResponse> pDXResponseParser = CreateResponseParser<DependenciesResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath(text).SetAuthHeader()
			.SetBody(mods.ToArray())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		DependenciesResponse response = pDXResponseParser.GetResponse();
		ModDependenciesResult resultObject = pDXResponseParser.GetResultObject<ModDependenciesResult>();
		if (!resultObject.Success || response.mods == null || response.mods.Length == 0)
		{
			return resultObject;
		}
		resultObject.Dependencies = new List<ModDependencyResult>();
		DependencyResponse[] mods2 = response.mods;
		foreach (DependencyResponse dependencyResponse in mods2)
		{
			resultObject.Dependencies.Add(new ModDependencyResult
			{
				Id = dependencyResponse.modId,
				DisplayName = dependencyResponse.displayName,
				LoadOrder = dependencyResponse.activationOrder,
				Size = dependencyResponse.size,
				IsSubscribed = dependencyResponse.installed,
				State = dependencyResponse.state.GetModState(),
				Type = DependencyType.Mod
			});
		}
		return resultObject;
	}

	public async Task<ModDependenciesResult> GetDependencyCandidates(int modId, string searchQuery = null, int? amount = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetDependencyCandidates", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ModDependenciesResult>("get dependency candidates");
		}
		DependencyCandidatesRequest body = new DependencyCandidatesRequest
		{
			modId = modId,
			searchString = searchQuery,
			amount = amount
		};
		PDXResponseParser<DependencyCandidatesResponse> pDXResponseParser = CreateResponseParser<DependencyCandidatesResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/dependencies").SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		DependencyCandidatesResponse response = pDXResponseParser.GetResponse();
		ModDependenciesResult resultObject = pDXResponseParser.GetResultObject<ModDependenciesResult>();
		if (!resultObject.Success || response.mods == null || response.mods.Length == 0)
		{
			return resultObject;
		}
		resultObject.Dependencies = new List<ModDependencyResult>();
		SimplifiedDependencyResponse[] mods = response.mods;
		foreach (SimplifiedDependencyResponse simplifiedDependencyResponse in mods)
		{
			resultObject.Dependencies.Add(new ModDependencyResult
			{
				Id = simplifiedDependencyResponse.id,
				DisplayName = simplifiedDependencyResponse.name,
				Type = DependencyType.Mod
			});
		}
		return resultObject;
	}

	public async Task<ModDependantsResult> GetModDependants(List<int> modIds, int playsetId = 0, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetModDependants", LogLevel.L1_Debug, fd);
		ModDependantsRequest body = new ModDependantsRequest
		{
			activationSetId = playsetId,
			mods = modIds.Select((int mod) => new ModIdRequest
			{
				id = mod
			}).ToArray()
		};
		PDXResponseParser<ModDependantsResponse> pDXResponseParser = CreateResponseParser<ModDependantsResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/dependants").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ModDependantsResponse response = pDXResponseParser.GetResponse();
		ModDependantsResult resultObject = pDXResponseParser.GetResultObject<ModDependantsResult>();
		if (!resultObject.Success || response.mods == null)
		{
			return resultObject;
		}
		resultObject.Mods = response.mods;
		return resultObject;
	}

	public async Task<Result> SetLoadOrder(List<ModLoadOrder> modActivationOrderList, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SetLoadOrder", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<LoadOrderResult>("set load order");
		}
		ModOrderPairRequest[] mods = modActivationOrderList?.Select((ModLoadOrder order) => new ModOrderPairRequest
		{
			id = order.Mod.Id,
			order = order.LoadOrder
		})?.ToArray() ?? new ModOrderPairRequest[0];
		ActivationOrderRequest body = new ActivationOrderRequest
		{
			activationSetId = playsetId,
			mods = mods
		};
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/setOrder").SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<LoadOrderResult> ResetLoadOrder(int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.ResetLoadOrder", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<LoadOrderResult>("reset load order");
		}
		ResetActivationOrderRequest body = new ResetActivationOrderRequest
		{
			activationSetId = playsetId,
			gameName = _context.Namespace
		};
		PDXResponseParser<ResetActivationOrderResponse> pDXResponseParser = CreateResponseParser<ResetActivationOrderResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/resetOrder").SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ResetActivationOrderResponse response = pDXResponseParser.GetResponse();
		LoadOrderResult resultObject = pDXResponseParser.GetResultObject<LoadOrderResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.OrderList = (from order in response.mods
			select new ModLoadOrder
			{
				Mod = new Mod
				{
					Id = order.id
				},
				LoadOrder = order.order
			} into order
			orderby order.LoadOrder
			select order).ToList();
		return resultObject;
	}

	public async Task<CreatePlaysetResult> CreatePlayset(string playsetName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.CreatePlayset", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<CreatePlaysetResult>("create a remote playset");
		}
		CreatePlaysetRequest body = new CreatePlaysetRequest
		{
			game = _context.Namespace,
			name = playsetName
		};
		PDXResponseParser<CreatePlaysetResponse> pDXResponseParser = CreateResponseParser<CreatePlaysetResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/sets").SetAuthHeader()
			.SetBody(body)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		CreatePlaysetResponse response = pDXResponseParser.GetResponse();
		CreatePlaysetResult resultObject = pDXResponseParser.GetResultObject<CreatePlaysetResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.PlaysetId = response.id;
		resultObject.Name = response.name;
		return resultObject;
	}

	public async Task<RenamePlaysetResult> RenamePlayset(int playsetId, string playsetName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.RenamePlayset", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<RenamePlaysetResult>("rename a remote playset");
		}
		RenamePlaysetRequest body = new RenamePlaysetRequest
		{
			name = playsetName
		};
		PDXResponseParser<RenamePlaysetResponse> pDXResponseParser = CreateResponseParser<RenamePlaysetResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath($"mods/sets/{playsetId}").SetAuthHeader()
			.SetBody(body)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		RenamePlaysetResponse response = pDXResponseParser.GetResponse();
		RenamePlaysetResult resultObject = pDXResponseParser.GetResultObject<RenamePlaysetResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.PlaysetId = response.id;
		resultObject.Name = response.name;
		return resultObject;
	}

	public async Task<Result> DeletePlayset(int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DeletePlayset", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<CreatePlaysetResult>("delete a remote playset");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath($"mods/sets/{playsetId}").SetAuthHeader()
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<ListAllPlaysetsResult> ListAllPlaysets(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.ListAllPlaysets", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ListAllPlaysetsResult>("retrieve the remote list of playsets");
		}
		PDXResponseParser<ListAllPlaysetsResponse> pDXResponseParser = CreateResponseParser<ListAllPlaysetsResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/sets/list").AddNamespaceToPath()
			.SetAuthHeader()
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ListAllPlaysetsResponse response = pDXResponseParser.GetResponse();
		ListAllPlaysetsResult resultObject = pDXResponseParser.GetResultObject<ListAllPlaysetsResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.AllPlaysets = new List<Playset>();
		ListPlaysetResponse[] array = response.sets ?? new ListPlaysetResponse[0];
		foreach (ListPlaysetResponse playsetResponse in array)
		{
			resultObject.AllPlaysets.Add(playsetResponse.ToPlayset());
		}
		return resultObject;
	}

	public async Task<ListSubscribedModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.ListModsInPlayset", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ListSubscribedModsInPlaysetResult>("retrieve the playset's mods");
		}
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath($"mods/sets/{playsetId}/mods").SetAuthHeader();
		if (limit.HasValue)
		{
			requestBuilder = requestBuilder.AddParameterToPath("limit", limit.Value.ToString());
		}
		if (page.HasValue)
		{
			requestBuilder = requestBuilder.AddParameterToPath("page", page.Value.ToString());
		}
		PDXResponseParser<ListModsInPlaysetResponse> pDXResponseParser = CreateResponseParser<ListModsInPlaysetResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		ListModsInPlaysetResponse response = pDXResponseParser.GetResponse();
		ListSubscribedModsInPlaysetResult resultObject = pDXResponseParser.GetResultObject<ListSubscribedModsInPlaysetResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.TotalCount = response.count;
		resultObject.Page = response.page;
		resultObject.Limit = response.limit;
		resultObject.Mods = new List<PlaysetSubscribedMod>();
		ModInPlaysetResponse[] mods = response.mods;
		foreach (ModInPlaysetResponse modInPlaysetResponse in mods)
		{
			resultObject.Mods.Add(modInPlaysetResponse.ToPlaysetMod());
		}
		return resultObject;
	}

	public async Task<ListOfFeaturedModsResult> GetFeaturedModsLists(int? page, int? limit, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetFeaturedModsLists", LogLevel.L0_Info, fd);
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/lists/featured").AddParameterToPath("gameName", _context.Namespace)
			.SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value);
		if (limit.HasValue)
		{
			requestBuilder = requestBuilder.AddParameterToPath("limit", limit.Value.ToString());
		}
		if (page.HasValue)
		{
			requestBuilder = requestBuilder.AddParameterToPath("page", page.Value.ToString());
		}
		PDXResponseParser<ListOfFeaturedModsResponse> pDXResponseParser = CreateResponseParser<ListOfFeaturedModsResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		ListOfFeaturedModsResponse response = pDXResponseParser.GetResponse();
		ListOfFeaturedModsResult resultObject = pDXResponseParser.GetResultObject<ListOfFeaturedModsResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Count = response.count;
		resultObject.Page = response.page;
		resultObject.Limit = response.limit;
		resultObject.Lists = new List<FeaturedModsList>();
		foreach (InternalListOfFeaturedModsItem item2 in response.items)
		{
			FeaturedModsList featuredModsList = new FeaturedModsList();
			featuredModsList.Created = item2.created;
			featuredModsList.DisplayName = item2.displayName;
			featuredModsList.Id = item2.id;
			featuredModsList.Updated = item2.updated;
			featuredModsList.Published = item2.published;
			featuredModsList.Archived = item2.archived;
			featuredModsList.Pinned = item2.pinned;
			featuredModsList.Mods = new List<FeaturedModsItem>();
			foreach (InternalFeaturedModsItem mod in item2.mods)
			{
				FeaturedModsItem item = new FeaturedModsItem
				{
					Id = mod.id,
					Name = mod.name,
					Size = mod.size,
					Author = mod.author,
					Rating = mod.rating,
					RatingsTotal = mod.ratingsTotal,
					DisplayName = mod.displayName,
					EditorTitle = mod.editorTitle,
					LatestVersion = mod.latestVersion,
					InstalledCount = mod.installedCount,
					ShortDescription = mod.shortDescription,
					EditorDescription = mod.editorDescription,
					Cover = mod.cover
				};
				featuredModsList.Mods.Add(item);
			}
			resultObject.Lists.Add(featuredModsList);
		}
		return resultObject;
	}

	public async Task<ListSubscribedResult> GetAllSubscribed(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetAllSubscribedMods", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ListSubscribedResult>("get all subscribed mods");
		}
		PDXResponseParser<SubscribedResponse> pDXResponseParser = CreateResponseParser<SubscribedResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/installed").AddParameterToPath("game", _context.Namespace)
			.AddParameterToPath("os", _context.Platform.GetModsPlatform().ToModsPlatformString())
			.AddParameterToPath("arch", "Any")
			.SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		SubscribedResponse response = pDXResponseParser.GetResponse();
		ListSubscribedResult resultObject = pDXResponseParser.GetResultObject<ListSubscribedResult>();
		if (!resultObject.Success || response.mods == null)
		{
			return resultObject;
		}
		resultObject.LatestUpdate = response.latestUpdate.ParseDateTime("GetAllSubscribed", "latestUpdate", "ModsBackendApiService", _context.Logger, fd);
		resultObject.Mods = new List<ModDetails>();
		SubscribedModResponse[] mods = response.mods;
		foreach (SubscribedModResponse subscribedModResponse in mods)
		{
			ModDetails modDetails = resultObject.Mods.Find((ModDetails x) => x.Id == subscribedModResponse.modId.ParseIntOrDefault() && x.Version == subscribedModResponse.preferredVersion);
			if (modDetails == null)
			{
				modDetails = subscribedModResponse.ToModDetails(_context.Logger, fd);
				resultObject.Mods.Add(modDetails);
			}
			modDetails.Playsets.Add(subscribedModResponse.ToPlaysetInMod());
			modDetails.InstalledDate = modDetails.Playsets.OrderBy((PlaysetInMod p) => p.SubscribedDate).First().SubscribedDate;
		}
		return resultObject;
	}

	public async Task<PublishResult> Publish(PublishRequest inputData, bool v2 = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PerformPublish", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishResult>("publish a mod");
		}
		RequestBuilder requestBuilder = CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetAuthHeader().SetBody(inputData)
			.SetContentType(ContentType.JSON);
		if (v2)
		{
			requestBuilder.SetPath("mods").AddHeader(ModsV2Header.Key, ModsV2Header.Value);
		}
		else
		{
			requestBuilder.SetPath("mods/publish");
		}
		PDXResponseParser<PublishResponse> pDXResponseParser = CreateResponseParser<PublishResponse>(await requestBuilder.Send().ConfigureAwait(continueOnCapturedContext: false), fd);
		PublishResponse response = pDXResponseParser.GetResponse();
		PublishResult resultObject = pDXResponseParser.GetResultObject<PublishResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModId = response.modId;
		resultObject.State = response.state.GetModState();
		return resultObject;
	}

	public async Task<PublishUpdateResult> PublishUpdate(PublishUpdateRequest inputData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishUpdate", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishUpdateResult>("update a published mod");
		}
		PDXResponseParser<PublishUpdateResponse> pDXResponseParser = CreateResponseParser<PublishUpdateResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/publishUpdate").SetAuthHeader()
			.SetBody(inputData)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		PublishUpdateResponse response = pDXResponseParser.GetResponse();
		PublishUpdateResult resultObject = pDXResponseParser.GetResultObject<PublishUpdateResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModId = inputData.modId.ParseIntOrDefault();
		resultObject.ModVersion = response.modVersion;
		resultObject.UserModVersion = inputData.userModVersion;
		resultObject.State = response.state.GetModState();
		return resultObject;
	}

	public async Task<PublishUpdateResult> PublishNewModVersion(PublishNewModVersionRequest inputData, int modId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishNewVersion", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishUpdateResult>("publish a new version of a mod");
		}
		PDXResponseParser<PublishNewModVersionResponse> pDXResponseParser = CreateResponseParser<PublishNewModVersionResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath($"mods/{modId}/versions").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetBody(inputData)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		PublishNewModVersionResponse response = pDXResponseParser.GetResponse();
		PublishUpdateResult resultObject = pDXResponseParser.GetResultObject<PublishUpdateResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModId = response.modId;
		resultObject.ModVersion = response.version;
		resultObject.UserModVersion = response.userNamedVersion ?? inputData.userModVersion;
		resultObject.State = response.state.GetModState();
		return resultObject;
	}

	public async Task<PublishUpdateResult> PatchModVersion(PatchModVersionRequest inputData, int modId, string version, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PatchModVersion", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishUpdateResult>("update a published mod's version");
		}
		PDXResponseParser<PatchModVersionResponse> pDXResponseParser = CreateResponseParser<PatchModVersionResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PATCH).SetPath($"mods/{modId}/versions/{version}").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetBody(inputData)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		PatchModVersionResponse response = pDXResponseParser.GetResponse();
		PublishUpdateResult resultObject = pDXResponseParser.GetResultObject<PublishUpdateResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModId = response.modId;
		resultObject.ModVersion = response.version;
		resultObject.UserModVersion = inputData.userModVersion;
		resultObject.State = ModState.Published;
		return resultObject;
	}

	public async Task<ReportModCreatorResult> ReportModCreator(string userName, ReportReason reportReason, string description, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.FollowedModCreators", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<ReportModCreatorResult>("Report a Mod Creator ");
		}
		string body = "{ \"reason\": \"" + reportReason.GetReportReasonModCreator() + "\", \"description\": \"" + description + "\"}";
		PDXResponseParser<ReportModCreatorResponse> pDXResponseParser = CreateResponseParser<ReportModCreatorResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/creators/" + userName + "/reports").SetBody(body)
			.SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		ReportModCreatorResponse response = pDXResponseParser.GetResponse();
		ReportModCreatorResult resultObject = pDXResponseParser.GetResultObject<ReportModCreatorResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Id = response.id;
		return resultObject;
	}

	public async Task<GetFollowedModCreatorsResult> GetFollowedModCreators(int? limit = 20, int? page = 1, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.FollowedModCreators", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetFollowedModCreatorsResult>("Get all followed Mod Creators");
		}
		PDXResponseParser<GetFollowedModCreatorsResponse> pDXResponseParser = CreateResponseParser<GetFollowedModCreatorsResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/creators/following").AddParameterToPath("limit", (!limit.HasValue) ? null : limit.ToString())
			.AddParameterToPath("page", (!page.HasValue) ? null : page.ToString())
			.SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetFollowedModCreatorsResponse response = pDXResponseParser.GetResponse();
		GetFollowedModCreatorsResult resultObject = pDXResponseParser.GetResultObject<GetFollowedModCreatorsResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.Creators = new List<ModCreator>();
		for (int i = 0; i < response.creators.Length; i++)
		{
			resultObject.Creators.Add(new ModCreator
			{
				Username = response.creators[i].username,
				Bio = response.creators[i].bio,
				Avatar = response.creators[i].avatar,
				FollowerCount = response.creators[i].followerCount,
				ExternalLinks = response.creators[i].externalLinks.ToList(),
				ThirdPartyLinks = response.creators[i].thirdPartyLinks?.Select((ThirdPartyLinksResponse x) => x.ToThirdPartyProfile()).ToList(),
				IsFollowing = response.creators[i].isFollowing
			});
		}
		return resultObject;
	}

	public async Task<GetModCreatorsProfileResult> GetModCreatorProfile(string userName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetModCreatorProfile", LogLevel.L0_Info, fd);
		if (string.IsNullOrEmpty(userName))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidNetworkResponse.UnexpectedResponseError, $"Mod creator userName should not be empty", "")
			}.CastTo<GetModCreatorsProfileResult>();
		}
		PDXResponseParser<GetModCreatorsProfileResponse> pDXResponseParser = CreateResponseParser<GetModCreatorsProfileResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("mods/creators/" + userName).SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetModCreatorsProfileResponse response = pDXResponseParser.GetResponse();
		GetModCreatorsProfileResult resultObject = pDXResponseParser.GetResultObject<GetModCreatorsProfileResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.CreatorProfile = new ModCreator
		{
			Username = response.profile.username,
			Bio = response.profile.bio,
			Avatar = response.profile.avatar,
			FollowerCount = response.profile.followerCount,
			ExternalLinks = response.profile.externalLinks.ToList(),
			ThirdPartyLinks = response.profile.thirdPartyLinks?.Select((ThirdPartyLinksResponse x) => x.ToThirdPartyProfile()).ToList(),
			IsFollowing = response.profile.isFollowing
		};
		return resultObject;
	}

	public async Task<Result> FollowModCreator(string userName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.FollowModCreator", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<Result>("Follow a Mod Creator ");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/creators/" + userName + "/followers").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> UnfollowModCreator(string userName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UnfollowModCreator", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<Result>("Unfollow a Mod Creator ");
		}
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("mods/creators/" + userName + "/followers").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> Unpublish(int modId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Unpublish", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("remove the mod");
		}
		UnpublishRequest body = new UnpublishRequest
		{
			modId = modId.ToString()
		};
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/delete").SetAuthHeader()
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> UploadFile(FileType filetype, string uuid, string absoluteFilePath, TransferStatus transferStatus, Stream fileStream, byte[] body = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UploadFile", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("upload the mod");
		}
		string fileName = Path.GetFileName(absoluteFilePath);
		UploadChunkHandler chunkHandler = new UploadChunkHandler(transferStatus, _context.Config.ChunkSize.Value, fileStream, absoluteFilePath);
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath("mods/content/" + _context.Namespace + "/" + uuid + "/" + filetype.GetFileType() + "/" + fileName).SetAuthHeader()
			.SetContentType(ContentType.OctetStream)
			.SetChunkHandler(chunkHandler)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<Result> DeleteFile(FileType filetype, string uuid, string filePath, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DeleteFile", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("remove the mod");
		}
		string fileName = Path.GetFileName(filePath);
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.DELETE).SetPath("mods/content/" + _context.Namespace + "/" + uuid + "/" + filetype.GetFileType() + "/" + fileName).SetAuthHeader()
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
	}

	public async Task<GetPresignedUrlResult> GetPresignedUrl(string modGuid, string fileName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.GetPresignedUrl", LogLevel.L0_Info, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<GetPresignedUrlResult>("get a presigned-url to upload a mod's file");
		}
		GetPresignedUrlRequest body = new GetPresignedUrlRequest
		{
			gameName = _context.Namespace,
			modName = modGuid,
			fileName = fileName
		};
		PDXResponseParser<GetPresignedUrlResponse> pDXResponseParser = CreateResponseParser<GetPresignedUrlResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.POST).SetPath("mods/presigned-urls").SetAuthHeader()
			.AddHeader(ModsV2Header.Key, ModsV2Header.Value)
			.SetBody(body)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true);
		GetPresignedUrlResponse response = pDXResponseParser.GetResponse();
		GetPresignedUrlResult resultObject = pDXResponseParser.GetResultObject<GetPresignedUrlResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.ModGuid = response.modName;
		resultObject.FileName = response.fileName;
		resultObject.PresignedUrl = response.presignedUrl;
		resultObject.ContentType = ContentTypeExtensionMethods.GetContentTypeEnum(response.contentType);
		return resultObject;
	}

	public async Task<Result> UploadFileToPresignedUrl(string url, string absolutePath, ContentType contentType, TransferStatus transferStatus, Stream fileStream, byte[] body = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UploadFileToPresignedUrl", LogLevel.L0_Info, fd);
		UploadChunkHandler chunkHandler = new UploadChunkHandler(transferStatus, _context.Config.ChunkSize.Value, fileStream, absolutePath);
		return CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetHostType(HostTypes.HostType.None).SetPath(url)
			.SetContentType(contentType)
			.SetChunkHandler(chunkHandler)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd, hasJSONBody: true, strictHttpCode: true).GetResultObject();
	}
}
