using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Models;

[Preserve]
public class FeaturedModsList
{
	[Preserve]
	public int Id { get; set; }

	[Preserve]
	public string DisplayName { get; set; }

	[Preserve]
	public DateTime? Created { get; set; }

	[Preserve]
	public DateTime? Updated { get; set; }

	[Preserve]
	public DateTime? Published { get; set; }

	[Preserve]
	public DateTime? Archived { get; set; }

	[Preserve]
	public bool Pinned { get; set; }

	[Preserve]
	public List<FeaturedModsItem> Mods { get; set; }
}
