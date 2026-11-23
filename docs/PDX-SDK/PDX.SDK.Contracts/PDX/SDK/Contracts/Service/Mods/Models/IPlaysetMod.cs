namespace PDX.SDK.Contracts.Service.Mods.Models;

public interface IPlaysetMod
{
	bool IsEnabled { get; set; }

	string DisplayName { get; set; }

	int LoadOrder { get; set; }

	LocalData LocalData { get; set; }
}
