using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ModSubscribeRequest
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public int? version { get; set; }
}
