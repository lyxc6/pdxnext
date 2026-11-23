using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ModOrderPairRequest
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public int order { get; set; }
}
