using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods.Models;

[Preserve]
public class InternalListOfFeaturedModsItem
{
	public int id { get; set; }

	public string displayName { get; set; }

	public DateTime? created { get; set; }

	public DateTime? updated { get; set; }

	public DateTime? published { get; set; }

	public DateTime? archived { get; set; }

	public bool pinned { get; set; }

	[Preserve]
	public List<InternalFeaturedModsItem> mods { get; set; }
}
