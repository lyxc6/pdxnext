using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ThirdPartyLinksResponse
{
	[Preserve]
	public string platform { get; set; }

	[Preserve]
	public string thirdPartyDisplayName { get; set; }

	[Preserve]
	public string thirdPartyId { get; set; }
}
