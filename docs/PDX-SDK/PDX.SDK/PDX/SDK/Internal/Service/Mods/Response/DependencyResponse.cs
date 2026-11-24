using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class DependencyResponse
{
	[Preserve]
	public int modId { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public int activationOrder { get; set; }

	[Preserve]
	public ulong size { get; set; }

	[Preserve]
	public bool installed { get; set; }

	[Preserve]
	public string state { get; set; }
}
