using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public interface IMod
{
	int Id { get; set; }

	string Version { get; set; }

	string Name { get; set; }

	string DisplayName { get; set; }

	string Author { get; set; }

	string ShortDescription { get; set; }

	string LongDescription { get; set; }

	string RequiredGameVersion { get; set; }

	string UserModVersion { get; set; }

	string LatestVersion { get; set; }

	string ThumbnailPath { get; set; }

	ulong Size { get; set; }

	List<ModTag> Tags { get; set; }

	int Rating { get; set; }

	int RatingsTotal { get; set; }

	ModState State { get; set; }

	DateTime? LatestUpdate { get; set; }

	DateTime? InstalledDate { get; set; }

	LocalData LocalData { get; set; }
}
