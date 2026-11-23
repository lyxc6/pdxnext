using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class ExternalLinkRequest
{
	[Preserve]
	public string type;

	[Preserve]
	public string url;

	[Preserve]
	public string displayName;
}
