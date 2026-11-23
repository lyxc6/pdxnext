namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModSubscribeStatus
{
	public IMod Mod { get; set; }

	public bool IsSubscribed { get; set; }
}
