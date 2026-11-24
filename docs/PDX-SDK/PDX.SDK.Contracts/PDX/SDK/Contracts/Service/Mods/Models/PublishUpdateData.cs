using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PublishUpdateData
{
	public string WipGuid { get; set; }

	public string DisplayName { get; set; }

	public string RecommendedGameVersion { get; set; }

	public ModPlatform Os { get; set; }

	public string Changelog { get; set; }

	public List<string> Tags { get; set; }

	public List<ModDependency> Dependencies { get; set; }

	public List<string> ForumLinks { get; set; }

	public List<ExternalLink> ExternalLinks { get; set; }

	public List<string> ScreenshotNames { get; set; }

	public string UserModVersion { get; set; }
}
