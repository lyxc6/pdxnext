using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ListModsInPlaysetResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<IPlaysetMod> Mods { get; set; }

	[Preserve]
	public int TotalCount { get; set; }

	[Preserve]
	public int Page { get; set; }

	[Preserve]
	public int Limit { get; set; }
}
