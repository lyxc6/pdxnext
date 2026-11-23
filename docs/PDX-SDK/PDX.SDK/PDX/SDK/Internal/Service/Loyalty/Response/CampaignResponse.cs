using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class CampaignResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string startDate { get; set; }

	[Preserve]
	public string endDate { get; set; }

	[Preserve]
	public string description { get; set; }

	[Preserve]
	public List<RuleResponse> rules { get; set; }
}
