using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ExternalLinkResponse
{
	[Preserve]
	public string type { get; set; }

	[Preserve]
	public string url { get; set; }

	[Preserve]
	public string displayName { get; set; }
}
