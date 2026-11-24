using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class PatchModVersionResponse : HTTPBodyBase
{
	[Preserve]
	public int modId { get; set; }

	[Preserve]
	public int version { get; set; }
}
