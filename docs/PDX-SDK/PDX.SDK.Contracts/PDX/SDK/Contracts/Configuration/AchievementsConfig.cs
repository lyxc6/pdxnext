namespace PDX.SDK.Contracts.Configuration;

public class AchievementsConfig
{
	public string RootPath { get; set; }

	public void SetIfNotNull(AchievementsConfig cfg)
	{
		if (cfg.RootPath != null)
		{
			RootPath = cfg.RootPath;
		}
	}
}
