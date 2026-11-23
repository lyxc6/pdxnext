using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ListOfFeaturedModsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<FeaturedModsList> Lists { get; set; }

	[Preserve]
	public int Count { get; set; }

	[Preserve]
	public int Page { get; set; }

	[Preserve]
	public int Limit { get; set; }
}
