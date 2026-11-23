using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ListModsInPlaysetRequest
{
	[Preserve]
	public int? limit;

	[Preserve]
	public int? page;
}
