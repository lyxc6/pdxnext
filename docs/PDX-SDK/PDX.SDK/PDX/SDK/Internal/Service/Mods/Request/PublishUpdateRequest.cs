using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class PublishUpdateRequest
{
	[Preserve]
	public string modId;

	[Preserve]
	public string displayName;

	[Preserve]
	public string os;

	[Preserve]
	public string arch;

	[Preserve]
	public string recommendedGameVersion;

	[Preserve]
	public string shortDescription;

	[Preserve]
	public string longDescription;

	[Preserve]
	public string[] tags;

	[Preserve]
	public string contentFileName;

	[Preserve]
	public string thumbnail;

	[Preserve]
	public string[] screenshotsToAdd;

	[Preserve]
	public string[] screenshotsToRemove;

	[Preserve]
	public string backgroundColor;

	[Preserve]
	public PublishDependencyRequest[] dependencies;

	[Preserve]
	public string changelogEntry;

	[Preserve]
	public string forumLink;

	[Preserve]
	public string[] forumLinksToAdd;

	[Preserve]
	public string[] forumLinksToRemove;

	[Preserve]
	public ExternalLinkRequest[] externalLinks;

	[Preserve]
	public string userModVersion;
}
