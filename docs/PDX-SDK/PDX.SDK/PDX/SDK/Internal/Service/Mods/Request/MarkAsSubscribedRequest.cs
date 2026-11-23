using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class MarkAsSubscribedRequest
{
	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public ModSubscribeRequest[] mods { get; set; }
}
