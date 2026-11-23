using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class Mod : IMod
{
	public int Id { get; set; }

	public string Name { get; set; }

	public string DisplayName { get; set; }

	public string Author { get; set; }

	public string ShortDescription { get; set; }

	public string LongDescription { get; set; }

	public string RequiredGameVersion { get; set; }

	public string UserModVersion { get; set; }

	public string LatestVersion { get; set; }

	public string Version { get; set; }

	public string ThumbnailPath { get; set; }

	public ulong Size { get; set; }

	public List<ModTag> Tags { get; set; }

	public int Rating { get; set; }

	public int RatingsTotal { get; set; }

	public ModState State { get; set; }

	public LocalData LocalData { get; set; }

	public DateTime? LatestUpdate { get; set; }

	public DateTime? InstalledDate { get; set; }

	public List<PlaysetInMod> Playsets { get; set; }

	public bool HasLiked { get; set; }

	public DateTime? CreationDate { get; set; }
}
