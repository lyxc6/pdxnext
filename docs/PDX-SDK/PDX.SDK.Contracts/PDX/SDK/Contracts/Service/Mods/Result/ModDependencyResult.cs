using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;

namespace PDX.SDK.Contracts.Service.Mods.Result;

public class ModDependencyResult : ModDependency
{
	public ulong Size { get; set; }

	public int LoadOrder { get; set; }

	public bool IsSubscribed { get; set; }

	public ModState State { get; set; }
}
