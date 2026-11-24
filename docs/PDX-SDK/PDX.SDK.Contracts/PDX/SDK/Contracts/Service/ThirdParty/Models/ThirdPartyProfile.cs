using PDX.SDK.Contracts.Service.ThirdParty.Enums;

namespace PDX.SDK.Contracts.Service.ThirdParty.Models;

public class ThirdPartyProfile
{
	public Provider Provider { get; set; }

	public string DisplayName { get; set; }

	public string Id { get; set; }
}
