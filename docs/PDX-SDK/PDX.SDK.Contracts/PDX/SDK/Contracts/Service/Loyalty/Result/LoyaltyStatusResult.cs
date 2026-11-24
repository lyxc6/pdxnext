using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Loyalty.Models;

namespace PDX.SDK.Contracts.Service.Loyalty.Result;

[Preserve]
public class LoyaltyStatusResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Campaign> Campaigns { get; set; }

	[Preserve]
	public List<ThresholdReached> ThresholdsReached { get; set; }
}
