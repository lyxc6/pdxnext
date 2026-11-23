using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ListModsInPlaysetResponse : HTTPBodyBase
{
	[Preserve]
	public ModInPlaysetResponse[] mods { get; set; }

	[Preserve]
	public int limit { get; set; }

	[Preserve]
	public int page { get; set; }

	[Preserve]
	public int count { get; set; }
}
