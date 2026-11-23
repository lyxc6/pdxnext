using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ModListResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<Mod> Mods { get; set; }

	[Preserve]
	public ModListResult()
	{
		Mods = new List<Mod>();
	}
}
