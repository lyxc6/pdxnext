using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

internal class ModCreatorResponse
{
	public string username { get; set; }

	public ExternalLink[] externalLinks { get; set; }

	public string bio { get; set; }

	public Avatar avatar { get; set; }

	public int followerCount { get; set; }

	public ThirdPartyLinksResponse[] thirdPartyLinks { get; set; }

	public bool isFollowing { get; set; }
}
