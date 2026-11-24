using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class UserPointResponse : HTTPBodyBase
{
	[Preserve]
	public string accountId { get; set; }

	[Preserve]
	public string @namespace { get; set; }

	[Preserve]
	public string pointType { get; set; }

	[Preserve]
	public int sum { get; set; }
}
