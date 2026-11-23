using System.Collections.Generic;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModDependant
{
	public int ModId { get; set; }

	public List<Dependant> Dependants { get; set; }
}
