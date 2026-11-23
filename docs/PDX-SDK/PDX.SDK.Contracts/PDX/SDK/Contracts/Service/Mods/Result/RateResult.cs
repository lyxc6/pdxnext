using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class RateResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int AverageRating { get; set; }
}
