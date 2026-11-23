using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SubscribeModResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public int version { get; set; }
}
