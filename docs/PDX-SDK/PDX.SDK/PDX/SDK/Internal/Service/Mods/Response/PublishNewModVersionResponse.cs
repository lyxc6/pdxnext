using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class PublishNewModVersionResponse : HTTPBodyBase
{
	[Preserve]
	public int modId { get; set; }

	[Preserve]
	public int version { get; set; }

	[Preserve]
	public string userNamedVersion { get; set; }

	[Preserve]
	public string state { get; set; }
}
