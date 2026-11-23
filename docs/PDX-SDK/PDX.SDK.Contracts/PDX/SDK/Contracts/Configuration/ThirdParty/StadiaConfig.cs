namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class StadiaConfig
{
	public bool? Enabled { get; set; }

	public void SetIfNotNull(StadiaConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
	}
}
