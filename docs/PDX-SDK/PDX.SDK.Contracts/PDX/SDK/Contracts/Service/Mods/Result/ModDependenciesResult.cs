using System.Collections.Generic;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Contracts.Service.Mods.Result;

[Preserve]
public class ModDependenciesResult : PDX.SDK.Contracts.Result
{
	[Preserve]
	public List<ModDependencyResult> Dependencies { get; set; }

	[Preserve]
	public ModDependenciesResult()
	{
		Dependencies = new List<ModDependencyResult>();
	}
}
