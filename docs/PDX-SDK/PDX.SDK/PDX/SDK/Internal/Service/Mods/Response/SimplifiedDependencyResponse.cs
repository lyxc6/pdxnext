using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SimplifiedDependencyResponse
{
	[Preserve]
	public int id { get; set; }

	[Preserve]
	public string name { get; set; }
}
