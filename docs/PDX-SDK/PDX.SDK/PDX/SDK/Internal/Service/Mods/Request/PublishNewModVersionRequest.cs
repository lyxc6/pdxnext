using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Request;

[Preserve]
internal class PublishNewModVersionRequest
{
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
	public string[] screenshotNames;

	[Preserve]
	public PublishDependencyRequest[] dependencies;

	[Preserve]
	public string[] forumLinks;

	[Preserve]
	public ExternalLinkRequest[] externalLinks;

	[Preserve]
	public string userModVersion;

	[Preserve]
	public string changelogEntry;
}
