using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Loyalty.Response;

[Preserve]
internal class PointResponse
{
	[Preserve]
	public string pointType { get; set; }

	[Preserve]
	public int sum { get; set; }
}
