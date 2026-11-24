using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

internal class ModIdRequest
{
	[Preserve]
	public int id { get; set; }
}
