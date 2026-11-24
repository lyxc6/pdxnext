using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class DependencyCandidatesRequest
{
	[Preserve]
	public int? modId;

	[Preserve]
	public string searchString;

	[Preserve]
	public int? amount;
}
