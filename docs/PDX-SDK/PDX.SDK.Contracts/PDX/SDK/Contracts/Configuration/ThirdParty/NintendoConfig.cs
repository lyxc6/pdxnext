namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class NintendoConfig
{
	public bool? Enabled { get; set; }

	public void SetIfNotNull(NintendoConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
	}
}
