namespace PDX.SDK.Internal.Service.Mods.Models;

public class PlaysetWipModMetaData : IPlaysetModMetaData
{
	public string Id { get; set; }

	public int LoadOrder { get; set; }

	public bool IsEnabled { get; set; }
}
