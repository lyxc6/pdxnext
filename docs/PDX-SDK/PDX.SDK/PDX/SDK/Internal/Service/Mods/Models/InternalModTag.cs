using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Models;

[Preserve]
internal class InternalModTag
{
	[Preserve]
	public string id { get; set; }

	[Preserve]
	public string displayName { get; set; }
}
