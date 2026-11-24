using System;
using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ListSubscribedResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModDetails> Mods { get; set; }

	[Preserve]
	public DateTime? LatestUpdate { get; set; }
}
