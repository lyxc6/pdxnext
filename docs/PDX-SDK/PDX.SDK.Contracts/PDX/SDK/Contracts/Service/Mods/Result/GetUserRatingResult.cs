using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetUserRatingResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public int Rating { get; set; }
}
