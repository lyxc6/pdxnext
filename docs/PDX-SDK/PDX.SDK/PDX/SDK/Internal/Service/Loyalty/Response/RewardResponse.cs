using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class RewardResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string type { get; set; }

	[Preserve]
	public int received { get; set; }
}
