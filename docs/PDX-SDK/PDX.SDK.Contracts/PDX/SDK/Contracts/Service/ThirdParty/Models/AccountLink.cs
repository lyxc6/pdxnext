using PDX.SDK.Contracts.Service.ThirdParty.Enums;

namespace PDX.SDK.Contracts.Service.ThirdParty.Models;

public class AccountLink
{
	public string ParadoxUserId { get; set; }

	public string ThirdPartyUserId { get; set; }

	public Provider Provider { get; set; }
}
