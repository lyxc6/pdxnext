using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ActivationOrderRequest
{
	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public ModOrderPairRequest[] mods { get; set; }
}
