using System.Collections.Generic;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModCreator
{
	public string Username { get; set; }

	public List<ExternalLink> ExternalLinks { get; set; }

	public string Bio { get; set; }

	public Avatar Avatar { get; set; }

	public int FollowerCount { get; set; }

	public List<ThirdPartyProfile> ThirdPartyLinks { get; set; }

	public bool IsFollowing { get; set; }
}
