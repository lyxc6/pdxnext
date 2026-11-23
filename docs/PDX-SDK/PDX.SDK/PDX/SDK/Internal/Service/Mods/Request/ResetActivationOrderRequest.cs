using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ResetActivationOrderRequest
{
	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public string gameName { get; set; }
}
