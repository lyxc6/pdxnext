using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Contracts.Service.ThirdParty;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Models;
using PDX.SDK.Contracts.Service.ThirdParty.Result;
using PDX.SDK.Internal.Service.ThirdParty.Response;
using PDX.SDK.Internal.Util;
using PDX.SDK.Network.Enums;

namespace PDX.SDK.Internal.Service.ThirdParty;

internal class ThirdPartyService : InternalService, IThirdPartyService
{
	private static Dictionary<string, AutoLoginPromptState> autoLoginPromptStates = new Dictionary<string, AutoLoginPromptState>
	{
		{
			"AutoLoginAccepted",
			AutoLoginPromptState.AutoLoginAccepted
		},
		{
			"AutoLoginRejected",
			AutoLoginPromptState.AutoLoginRejected
		},
		{
			"AutoLoginPromptNotShown",
			AutoLoginPromptState.PromptNotShown
		}
	};

	public INintendoSwitchService NintendoSwitch { get; set; }

	internal ThirdPartyService(Context context)
		: base("ThirdParty", context)
	{
		NintendoSwitch = new NintendoSwitchService(context);
	}

	public async Task<GetAccountLinksResult> GetAccountLinks()
	{
		return await GetAccountLinks(null, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetAccountLinksResult> GetAccountLinks(ICredential credential)
	{
		return await GetAccountLinks(credential, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SetAutoLoginPromptState(AutoLoginPromptState autoLoginPromptState)
	{
		await _context.StateCache.Player.SetThirdPartyAutoLoginPromptState(autoLoginPromptState).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(AppleSignInCredential credential)
	{
		return await Link(Provider.AppleSignIn, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(GoogleCredential credential)
	{
		return await Link(Provider.Google, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(StadiaCredential credential)
	{
		return await Link(Provider.Stadia, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(SteamCredential credential)
	{
		return await Link(Provider.Steam, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(NintendoAccountCredential credential)
	{
		return await Link(Provider.NintendoAccount, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(PsnCredential credential)
	{
		return await Link(Provider.PSN, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> link(SteamCredential credential)
	{
		return await Link(Provider.Steam, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Link(XboxLiveCredential credential)
	{
		return await Link(Provider.XboxLive, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(AppleSignInCredential credential)
	{
		return await Unlink(Provider.AppleSignIn, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(GoogleCredential credential)
	{
		return await Unlink(Provider.Google, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(StadiaCredential credential)
	{
		return await Unlink(Provider.Stadia, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(NintendoAccountCredential credential)
	{
		return await Unlink(Provider.NintendoAccount, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(PsnCredential credential)
	{
		return await Unlink(Provider.PSN, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(SteamCredential credential)
	{
		return await Unlink(Provider.Steam, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(XboxLiveCredential credential)
	{
		return await Unlink(Provider.XboxLive, credential).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unlink(Provider provider)
	{
		return await Unlink(provider, new SessionToken()).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetAccountLinksResult> GetAccountLinks(ICredential credential = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("ThirdParty.GetAccountLinks", LogLevel.L1_Debug, fd);
		bool flag = await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false);
		if (credential == null && !flag)
		{
			return NotLoggedIn<GetAccountLinksResult>("get the list of linked accounts");
		}
		PDXResponseParser<GetAccountLinksResponse> pDXResponseParser = CreateResponseParser<GetAccountLinksResponse>(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.GET).SetPath("/accounts/connections/u").SetAuthHeader(credential ?? new SessionToken())
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd);
		GetAccountLinksResponse response = pDXResponseParser.GetResponse();
		GetAccountLinksResult resultObject = pDXResponseParser.GetResultObject<GetAccountLinksResult>();
		if (!resultObject.Success)
		{
			return resultObject;
		}
		resultObject.AccountLinks = new List<AccountLink>();
		AccountLinkResponse[] connections = response.connections;
		foreach (AccountLinkResponse accountLinkResponse in connections)
		{
			if (Provider.Unknown != ProviderExtensions.GetProvider(accountLinkResponse.universe))
			{
				resultObject.AccountLinks.Add(new AccountLink
				{
					ParadoxUserId = accountLinkResponse.accountId,
					ThirdPartyUserId = accountLinkResponse.userid,
					Provider = ProviderExtensions.GetProvider(accountLinkResponse.universe)
				});
			}
		}
		return resultObject;
	}

	internal static string GetAutoLoginPromptState(AutoLoginPromptState promptState)
	{
		string result = "AutoLoginPromptNotShown";
		foreach (KeyValuePair<string, AutoLoginPromptState> autoLoginPromptState in autoLoginPromptStates)
		{
			if (autoLoginPromptState.Value == promptState)
			{
				return autoLoginPromptState.Key;
			}
		}
		return result;
	}

	internal static AutoLoginPromptState GetAutoLoginPromptState(string promptState)
	{
		if (promptState == null)
		{
			return AutoLoginPromptState.PromptNotShown;
		}
		if (autoLoginPromptStates.ContainsKey(promptState))
		{
			return autoLoginPromptStates[promptState];
		}
		return AutoLoginPromptState.PromptNotShown;
	}

	private async Task<Result> Link(Provider provider, ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("ThirdParty.Link", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("link a third party account " + credential?.GetType().Name);
		}
		Result result = CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath(GetConnectUrl(provider)).SetAuthHeader()
			.SetBody(credential)
			.SetContentType(ContentType.JSON)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
		if (result.Success)
		{
			await SetAutoLoginPromptState(AutoLoginPromptState.AutoLoginAccepted).ConfigureAwait(continueOnCapturedContext: false);
		}
		return result;
	}

	private async Task<Result> Unlink(Provider provider, ICredential credential, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("ThirdParty.Unlink", LogLevel.L1_Debug, fd);
		bool flag = await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false);
		if (credential is SessionToken && !flag)
		{
			return NotLoggedIn("unlink a third-party account");
		}
		Result result = CreateResponseParser(await CreateRequestBuilder(fd).SetRestMethod(RESTMethod.PUT).SetPath(GetDisconnectUrl(provider)).SetAuthHeader(credential)
			.Send()
			.ConfigureAwait(continueOnCapturedContext: false), fd).GetResultObject();
		if (result.Success)
		{
			await SetAutoLoginPromptState(AutoLoginPromptState.PromptNotShown).ConfigureAwait(continueOnCapturedContext: false);
		}
		return result;
	}

	private string GetConnectUrl(Provider provider)
	{
		return provider switch
		{
			Provider.XboxLive => "accounts/xbox/connect/v2", 
			Provider.PSN => "accounts/psn/connect", 
			Provider.NintendoAccount => "accounts/nintendo/connect", 
			Provider.AppleSignIn => "accounts/apple/connect", 
			Provider.Google => "accounts/google/connect", 
			Provider.Stadia => "accounts/stadia/connect", 
			Provider.Steam => "accounts/steam/connect", 
			_ => "", 
		};
	}

	private string GetDisconnectUrl(Provider provider)
	{
		return provider switch
		{
			Provider.PSN => "accounts/psn/disconnect", 
			Provider.XboxLive => "accounts/xbox/disconnect", 
			Provider.NintendoAccount => "accounts/nintendo/disconnect", 
			Provider.AppleSignIn => "accounts/apple/disconnect", 
			Provider.Google => "accounts/google/disconnect", 
			Provider.Stadia => "accounts/stadia/disconnect", 
			Provider.Steam => "accounts/steam/disconnect", 
			_ => "", 
		};
	}

	internal async Task<GetThirdPartyProfilesResult> GetGamerDisplayNames(List<ulong> thirdPartyUserIds, FlowData flowData = null)
	{
		new FlowData(flowData);
		_context.Log("Thirdparty.GetGamerDisplayNames", LogLevel.L0_Info);
		return new GetThirdPartyProfilesResult
		{
			Success = false,
			Error = new Error(NotFound.NoProfile, "No third-party profiles can be fetched on the current platform", string.Empty)
		};
	}
}
