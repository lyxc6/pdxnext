namespace PDX.SDK.Internal.Service.Mods.Models;

public interface IPlaysetModMetaData
{
	int LoadOrder { get; set; }

	bool IsEnabled { get; set; }
}
