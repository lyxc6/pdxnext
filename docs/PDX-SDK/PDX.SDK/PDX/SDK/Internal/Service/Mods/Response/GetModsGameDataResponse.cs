using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class GetModsGameDataResponse : HTTPBodyBase
{
	[Preserve]
	public ModsGameDataResponse game { get; set; }
}
