using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ModDetailsResponse
{
	[Preserve]
	public string modId { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string game { get; set; }

	[Preserve]
	public string os { get; set; }

	[Preserve]
	public string arch { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public string shortDescription { get; set; }

	[Preserve]
	public string longDescription { get; set; }

	[Preserve]
	public string displayImagePath { get; set; }

	[Preserve]
	public string repositoryPath { get; set; }

	[Preserve]
	public string author { get; set; }

	[Preserve]
	public string requiredVersion { get; set; }

	[Preserve]
	public int latestVersion { get; set; }

	[Preserve]
	public string modVersion { get; set; }

	[Preserve]
	public string userModVersion { get; set; }

	[Preserve]
	public List<string> tags { get; set; }

	[Preserve]
	public List<ScreenshotResponse> screenshots { get; set; }

	[Preserve]
	public string background { get; set; }

	[Preserve]
	public List<RequirementResponse> requirements { get; set; }

	[Preserve]
	public List<ChangeResponse> changelog { get; set; }

	[Preserve]
	public int rating { get; set; }

	[Preserve]
	public int ratingsTotal { get; set; }

	[Preserve]
	public Dictionary<string, string> metadata { get; set; }

	[Preserve]
	public string state { get; set; }

	[Preserve]
	public ActionsResponse actions { get; set; }

	[Preserve]
	public List<PlaysetResponse> installedActivationSets { get; set; }

	[Preserve]
	public string forumLink { get; set; }

	[Preserve]
	public string[] forumLinks { get; set; }

	[Preserve]
	public int subscriptions { get; set; }

	[Preserve]
	public ThirdPartyLinksResponse[] thirdPartyLinks { get; set; }

	[Preserve]
	public List<ExternalLinkResponse> externalLinks { get; set; }

	[Preserve]
	public bool hasLiked { get; set; }

	[Preserve]
	public string creationDate { get; set; }

	[Preserve]
	public string latestUpdate { get; set; }
}
