using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SearchDependantModResponse
{
	[Preserve]
	public string modId { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public bool installed { get; set; }

	[Preserve]
	public string state { get; set; }
}
