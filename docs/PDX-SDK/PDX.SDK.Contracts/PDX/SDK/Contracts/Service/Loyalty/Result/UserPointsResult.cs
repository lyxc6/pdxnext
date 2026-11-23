using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Loyalty.Result;

[Preserve]
public class UserPointsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public Dictionary<string, int> Points { get; set; }
}
