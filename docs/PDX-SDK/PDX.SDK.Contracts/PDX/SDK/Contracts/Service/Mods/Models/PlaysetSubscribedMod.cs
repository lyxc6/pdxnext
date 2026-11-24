using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Models;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PlaysetSubscribedMod : IPlaysetMod
{
	public int Id { get; set; }

	public string ShortDescription { get; set; }

	public string RequiredGameVersion { get; set; }

	public ModState State { get; set; }

	public bool IsEnabled { get; set; }

	public string DisplayName { get; set; }

	public string Version { get; set; }

	public int LoadOrder { get; set; }

	public ulong Size { get; set; }

	public DateTime? Updated { get; set; }

	public ITag[] Tags { get; set; }

	public string DisplayImagePath { get; set; }

	public LocalData LocalData { get; set; }

	public List<ThirdPartyProfile> ThirdPartyProfiles { get; set; }

	public string UserModVersion { get; set; }

	public int RatingsTotal { get; set; }

	public string Author { get; set; }

	public bool HasLiked { get; set; }

	public DateTime? CreationDate { get; set; }
}
