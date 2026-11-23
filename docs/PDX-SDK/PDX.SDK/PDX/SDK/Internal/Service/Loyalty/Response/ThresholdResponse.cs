using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class ThresholdResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public int limit { get; set; }

	[Preserve]
	public List<RewardResponse> rewards { get; set; }
}
