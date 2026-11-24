namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class GoogleConfig
{
	public bool? Enabled { get; set; }

	public string ClientId { get; set; }

	public bool? SilentSignInOnly { get; set; }

	public void SetIfNotNull(GoogleConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
		if (cfg.ClientId != null)
		{
			ClientId = cfg.ClientId;
		}
		if (cfg.SilentSignInOnly.HasValue)
		{
			SilentSignInOnly = cfg.SilentSignInOnly;
		}
	}
}
