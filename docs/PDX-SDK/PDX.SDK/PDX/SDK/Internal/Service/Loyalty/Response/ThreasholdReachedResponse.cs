using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class ThreasholdReachedResponse
{
	[Preserve]
	public string pointType { get; set; }

	[Preserve]
	public List<ThresholdResponse> thresholds { get; set; }
}
