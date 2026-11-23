using System.Collections.Generic;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModDetails : Mod
{
	public string RepositoryPath { get; set; }

	public List<Screenshot> Screenshots { get; set; }

	public string Background { get; set; }

	public List<ModDependency> Dependencies { get; set; }

	public List<Change> Changelog { get; set; }

	public string[] ForumLinks { get; set; }

	public ActionsAllowed Actions { get; set; }

	public int SubscriptionsTotal { get; set; }

	public List<ThirdPartyProfile> ThirdPartyProfiles { get; set; }

	public List<ExternalLink> ExternalLinks { get; set; }
}
