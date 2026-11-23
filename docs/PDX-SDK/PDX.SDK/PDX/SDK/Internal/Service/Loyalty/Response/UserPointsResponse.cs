using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class UserPointsResponse : HTTPBodyBase
{
	[Preserve]
	public string accountId { get; set; }

	[Preserve]
	public string @namespace { get; set; }

	[Preserve]
	public List<PointResponse> points { get; set; }
}
