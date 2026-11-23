namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class PsnConfig
{
	public bool? Enabled { get; set; }

	public bool? DoNpInit { get; set; }

	public void SetIfNotNull(PsnConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
		if (cfg.DoNpInit.HasValue)
		{
			DoNpInit = cfg.DoNpInit;
		}
	}
}
