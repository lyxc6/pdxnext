using PDX.SDK.Contracts.Service.Mods.Enums;

namespace PDX.SDK.Contracts.Service.Mods.Models;

public class Dependant
{
	public bool Installed { get; set; }

	public int ModId { get; set; }

	public string DisplayName { get; set; }

	public bool Enabled { get; set; }

	public ModState State { get; set; }

	public int ActivationSetId { get; set; }
}
