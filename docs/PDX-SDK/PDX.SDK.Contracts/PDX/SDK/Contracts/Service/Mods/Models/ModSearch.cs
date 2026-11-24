using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModSearch : Mod
{
	public bool IsSubscribed { get; set; }

	public DateTime? SubscriptionDate { get; set; }

	public bool IsEnabled { get; set; }

	public int InstalledCount { get; set; }

	public List<ThirdPartyProfile> ThirdPartyProfiles { get; set; }
}
