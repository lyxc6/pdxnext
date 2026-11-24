using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ListAllPlaysetsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Playset> AllPlaysets { get; set; }
}
