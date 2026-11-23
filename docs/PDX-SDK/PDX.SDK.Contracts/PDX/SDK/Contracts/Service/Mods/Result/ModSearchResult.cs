using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ModSearchResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModSearch> Mods { get; set; }

	[Preserve]
	public int TotalCount { get; set; }
}
