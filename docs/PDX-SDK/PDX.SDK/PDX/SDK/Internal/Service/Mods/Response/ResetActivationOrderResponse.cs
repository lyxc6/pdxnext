using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ResetActivationOrderResponse : HTTPBodyBase
{
	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public string gameName { get; set; }

	[Preserve]
	public ModOrderPairResponse[] mods { get; set; }
}
