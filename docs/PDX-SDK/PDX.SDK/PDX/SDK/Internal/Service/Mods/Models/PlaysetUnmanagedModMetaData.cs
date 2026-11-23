namespace PDX.SDK.Internal.Service.Mods.Models;

public class PlaysetUnmanagedModMetaData : IPlaysetModMetaData
{
	public string Path { get; set; }

	public int LoadOrder { get; set; }

	public bool IsEnabled { get; set; }
}
