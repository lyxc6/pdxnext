using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Service.ThirdParty;

namespace PDX.SDK.Internal.Service.ThirdParty;

internal class NintendoSwitchService : InternalService, INintendoSwitchService
{
	public NintendoSwitchService(Context context)
		: base("ThirdParty.Nintendo", context)
	{
	}

	public Result ResolveNetworkIssues()
	{
		return new Result
		{
			Error = new Error(InternalError.ThirdPartyException, "Operation unsupported on platform", ""),
			Success = false
		};
	}
}
