using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PublishWipData
{
	public string wipGuid { get; set; }

	public string recommendedGameVersion { get; set; }

	public ModPlatform os { get; set; }

	public List<string> tags { get; set; }

	public string backgroundColor { get; set; }

	public List<ModDependency> dependencies { get; set; }

	public string forumLink { get; set; }

	public List<string> forumLinks { get; set; }

	public List<ExternalLink> externalLinks { get; set; }

	public string userModVersion { get; set; }
}
