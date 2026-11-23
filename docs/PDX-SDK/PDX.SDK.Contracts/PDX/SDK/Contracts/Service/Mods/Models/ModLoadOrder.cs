namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ModLoadOrder
{
	public IMod Mod { get; set; }

	public int LoadOrder { get; set; }

	public ModLoadOrder()
	{
		Mod = new Mod();
		LoadOrder = -1;
	}
}
