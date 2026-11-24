using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModDependency
{
	public int? Id { get; set; }

	public string DisplayName { get; set; }

	public string Version { get; set; }

	public DependencyType Type { get; set; }
}
