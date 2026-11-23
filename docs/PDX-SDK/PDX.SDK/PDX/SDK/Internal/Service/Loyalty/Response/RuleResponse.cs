using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class RuleResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string @event { get; set; }

	[Preserve]
	public string type { get; set; }

	[Preserve]
	public string metadata { get; set; }

	[Preserve]
	public string description { get; set; }

	[Preserve]
	public List<RewardResponse> rewards { get; set; }
}
