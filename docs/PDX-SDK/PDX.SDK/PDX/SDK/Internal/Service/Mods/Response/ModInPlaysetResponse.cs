using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Response;

[Preserve]
internal class ModInPlaysetResponse : HTTPBodyBase
{
	[Preserve]
	public int modId { get; set; }

	[Preserve]
	public string shortDescription { get; set; }

	[Preserve]
	public string requiredVersion { get; set; }

	[Preserve]
	public string state { get; set; }

	[Preserve]
	public bool enabled { get; set; }

	[Preserve]
	public string displayName { get; set; }

	[Preserve]
	public int? preferredVersion { get; set; }

	[Preserve]
	public string latestVersion { get; set; }

	[Preserve]
	public int loadOrder { get; set; }

	[Preserve]
	public ulong size { get; set; }

	[Preserve]
	public string installed { get; set; }

	[Preserve]
	public string updated { get; set; }

	[Preserve]
	public string[] tags { get; set; }

	[Preserve]
	public string displayImagePath { get; set; }

	[Preserve]
	public ThirdPartyLinksResponse[] thirdPartyLinks { get; set; }

	[Preserve]
	public string userModVersion { get; set; }

	[Preserve]
	public int ratingsTotal { get; set; }

	[Preserve]
	public string author { get; set; }

	[Preserve]
	public bool hasLiked { get; set; }

	[Preserve]
	public string creationDate { get; set; }
}
