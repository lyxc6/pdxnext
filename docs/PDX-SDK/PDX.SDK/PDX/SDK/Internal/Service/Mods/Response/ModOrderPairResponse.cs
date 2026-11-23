using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ModOrderPairResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public int order { get; set; }
}
