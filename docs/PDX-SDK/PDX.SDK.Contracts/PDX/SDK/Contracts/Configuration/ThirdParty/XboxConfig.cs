namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class XboxConfig
{
	public bool? Enabled { get; set; }

	public void SetIfNotNull(XboxConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
	}
}
