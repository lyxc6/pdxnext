using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SubscribeResponse : HTTPBodyBase
{
	[Preserve]
	public SubscribeModResponse[] mods { get; set; }

	[Preserve]
	public int activationSetId { get; set; }
}
