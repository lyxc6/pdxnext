using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Profile;
using PDX.SDK.Contracts.Service.Profile.Models;
using PDX.SDK.Contracts.Service.Profile.Result;
using PDX.SDK.Internal.Service.Profile.Request;
using PDX.SDK.Internal.Service.Profile.Response;
using PDX.SDK.Internal.Service.Profile.Result;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.Profile;

internal class ProfileService : InternalService, IProfileService
{
	internal ProfileService(Context context)
		: base("Profile", context)
	{
	}

	public async Task<PDX.SDK.Contracts.Result> CreateGameDisplayName(string displayName)
	{
		return await CreateDisplayName(_context.Namespace, displayName, "Profile.CreateGameDisplayName", "create a game profile").ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Result> CreateSocialDisplayName(string displayName)
	{
		return await CreateDisplayName("social", displayName, "Profile.CreateSocialDisplayName", "create a social profile").ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PDX.SDK.Contracts.Result> CreateDisplayName(string @namespace, string displayName, string logArea, string notLoggedInReturnMessage)
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn(notLoggedInReturnMessage);
		}
		FlowData flowData = new FlowData();
		Log(logArea, LogLevel.L1_Debug, flowData);
		CreateSocialDisplayNameRequest createRequest = new CreateSocialDisplayNameRequest
		{
			username = displayName,
			@namespace = @namespace
		};
		return await PerformCreateProfileDisplayName(createRequest, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PDX.SDK.Contracts.Service.Profile.Result.GetProfileResult> Get()
	{
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PDX.SDK.Contracts.Service.Profile.Result.GetProfileResult>("retrieve a game profile");
		}
		FlowData fd = new FlowData();
		Log("Profile.Get", LogLevel.L1_Debug, fd);
		PDX.SDK.Internal.Service.Profile.Result.GetProfileResult gameProfileResult = await GetProfileForNamespace(_context.Namespace, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!gameProfileResult.Success && gameProfileResult.Error != NotFound.NoProfile)
		{
			return gameProfileResult.CastTo<PDX.SDK.Contracts.Service.Profile.Result.GetProfileResult>();
		}
		PDX.SDK.Internal.Service.Profile.Result.GetProfileResult getProfileResult = await GetProfileForNamespace("social", fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!getProfileResult.Success && getProfileResult.Error != NotFound.NoProfile)
		{
			return getProfileResult.CastTo<PDX.SDK.Contracts.Service.Profile.Result.GetProfileResult>();
		}
		UserProfile game = new UserProfile
		{
			DisplayName = gameProfileResult.DisplayName,
			Developer = gameProfileResult.Developer,
			IsBanned = gameProfileResult.IsBanned
		};
		UserProfile social = new UserProfile
		{
			DisplayName = getProfileResult.DisplayName,
			Developer = getProfileResult.Developer,
			IsBanned = getProfileResult.IsBanned
		};
		return new PDX.SDK.Contracts.Service.Profile.Result.GetProfileResult
		{
			Success = true,
			Error = null,
			Game = game,
			Social = social
		};
	}

	private async Task<PDX.SDK.Internal.Service.Profile.Result.GetProfileResult> GetProfileForNamespace(string @namespace, FlowData flowData = null)
	{
		PDXResponseParser<GetProfileResponse> pDXResponseParser = CreateResponseParser<GetProfileResponse>(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.GET).SetPath("profiles").SetAuthHeader()
			.AddParameterToPath("namespace", @namespace)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData);
		GetProfileResponse response = pDXResponseParser.GetResponse();
		PDX.SDK.Internal.Service.Profile.Result.GetProfileResult resultObject = pDXResponseParser.GetResultObject<PDX.SDK.Internal.Service.Profile.Result.GetProfileResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.DisplayName = response.username;
		resultObject.IsBanned = response.isBanned.ParseBool();
		resultObject.Developer = response.developer.ParseBool();
		return resultObject;
	}

	private async Task<PDX.SDK.Contracts.Result> PerformCreateProfileDisplayName(CreateSocialDisplayNameRequest createRequest, FlowData fd = null)
	{
		FlowData flowData = fd;
		if (flowData == null)
		{
			flowData = new FlowData();
		}
		return CreateResponseParser(await CreateRequestBuilder(flowData).SetRestMethod(RESTMethod.POST).SetPath("profiles").SetAuthHeader()
			.SetBody(createRequest)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), flowData).GetResultObject();
	}
}
