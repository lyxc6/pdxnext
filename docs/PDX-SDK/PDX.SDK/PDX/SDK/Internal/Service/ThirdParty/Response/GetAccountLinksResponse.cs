using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.ThirdParty.Response;

[Preserve]
internal class GetAccountLinksResponse : HTTPBodyBase
{
	[Preserve]
	public AccountLinkResponse[] connections { get; set; }
}
