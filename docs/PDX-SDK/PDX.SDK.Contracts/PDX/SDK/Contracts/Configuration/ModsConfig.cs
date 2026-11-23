namespace PDX.SDK.Contracts.Configuration;

public class ModsConfig
{
	public string RootPath { get; set; }

	public bool? UsePatching { get; set; }

	public void SetIfNotNull(ModsConfig cfg)
	{
		if (cfg.RootPath != null)
		{
			RootPath = cfg.RootPath;
		}
	}
}
