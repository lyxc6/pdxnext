using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class PublishUpdateResponse : HTTPBodyBase
{
	[Preserve]
	public int modVersion { get; set; }

	[Preserve]
	public string state { get; set; }
}
