using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class RenamePlaysetResponse : HTTPBodyBase
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string game { get; set; }
}
