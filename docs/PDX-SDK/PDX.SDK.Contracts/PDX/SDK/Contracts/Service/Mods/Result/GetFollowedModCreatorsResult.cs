using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class GetFollowedModCreatorsResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModCreator> Creators { get; set; }
}
