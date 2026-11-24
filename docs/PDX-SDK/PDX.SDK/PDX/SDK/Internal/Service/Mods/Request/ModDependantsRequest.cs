using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

internal class ModDependantsRequest
{
	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public ModIdRequest[] mods { get; set; }
}
