using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class RequirementResponse
{
	[Preserve]
	public string id { get; set; }

	[Preserve]
	public string productType { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public int version { get; set; }
}
