using System.Threading.Tasks;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Result;

namespace PDX.SDK.Contracts.Service.ThirdParty;

public interface IThirdPartyService
{
	INintendoSwitchService NintendoSwitch { get; }

	Task<GetAccountLinksResult> GetAccountLinks();

	Task<GetAccountLinksResult> GetAccountLinks(ICredential credential);

	Task<PDX.SDK.Contracts.Result> Link(AppleSignInCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(GoogleCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(StadiaCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(SteamCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(NintendoAccountCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(PsnCredential credential);

	Task<PDX.SDK.Contracts.Result> Link(XboxLiveCredential credential);

	Task SetAutoLoginPromptState(AutoLoginPromptState autoLoginPromptState);

	Task<PDX.SDK.Contracts.Result> Unlink(AppleSignInCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(GoogleCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(StadiaCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(SteamCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(NintendoAccountCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(Provider provider);

	Task<PDX.SDK.Contracts.Result> Unlink(PsnCredential credential);

	Task<PDX.SDK.Contracts.Result> Unlink(XboxLiveCredential credential);
}
