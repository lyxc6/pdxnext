namespace PDX.SDK.Contracts.Service.Mods.Models;

public class PlaysetUnmanagedMod : IPlaysetMod
{
	public string Id { get; set; }

	public bool IsEnabled { get; set; }

	public string DisplayName { get; set; }

	public int LoadOrder { get; set; }

	public LocalData LocalData { get; set; }
}
