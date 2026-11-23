using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.ThirdParty.Response;

[Preserve]
internal class AccountLinkResponse
{
	[Preserve]
	public string accountId { get; set; }

	[Preserve]
	public string userid { get; set; }

	[Preserve]
	public string universe { get; set; }
}
