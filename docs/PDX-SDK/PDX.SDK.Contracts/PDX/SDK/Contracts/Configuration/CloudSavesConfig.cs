namespace PDX.SDK.Contracts.Configuration;

public class CloudSavesConfig
{
	public string RootPath { get; set; }

	public void SetIfNotNull(CloudSavesConfig cfg)
	{
		if (cfg.RootPath != null)
		{
			RootPath = cfg.RootPath;
		}
	}
}
