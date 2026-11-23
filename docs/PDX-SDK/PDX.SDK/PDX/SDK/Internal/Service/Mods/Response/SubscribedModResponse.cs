using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class SubscribedModResponse
{
	[Preserve]
	public string modId { get; set; }

	[Preserve]
	public string state { get; set; }

	[Preserve]
	public bool enabled { get; set; }

	[Preserve]
	public string installed { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public string displayImagePath { get; set; }

	[Preserve]
	public string repositoryPath { get; set; }

	[Preserve]
	public string name { get; set; }

	[Preserve]
	public string shortDescription { get; set; }

	[Preserve]
	public string latestVersion { get; set; }

	[Preserve]
	public string requiredVersion { get; set; }

	[Preserve]
	public int activationSetId { get; set; }

	[Preserve]
	public string activationSetName { get; set; }

	[Preserve]
	public Dictionary<string, string> metadata { get; set; }

	[Preserve]
	public string[] tags { get; set; }

	[Preserve]
	public int activationOrder { get; set; }

	[Preserve]
	public string preferredVersion { get; set; }

	[Preserve]
	public string latestUpdate { get; set; }

	[Preserve]
	public ThirdPartyLinksResponse[] thirdPartyLinks { get; set; }

	[Preserve]
	public string author { get; set; }

	[Preserve]
	public bool hasLiked { get; set; }

	[Preserve]
	public string creationDate { get; set; }
}
