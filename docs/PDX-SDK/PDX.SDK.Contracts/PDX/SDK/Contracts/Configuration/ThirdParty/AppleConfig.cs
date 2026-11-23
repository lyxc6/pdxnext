namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class AppleConfig
{
	public bool? Enabled { get; set; }

	public void SetIfNotNull(AppleConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
	}
}
