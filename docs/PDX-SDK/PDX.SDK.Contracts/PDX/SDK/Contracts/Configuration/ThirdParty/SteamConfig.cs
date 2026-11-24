namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class SteamConfig
{
	public bool? Enabled { get; set; }

	public string Ticket { get; set; }

	public uint? AppId { get; set; }

	public void SetIfNotNull(SteamConfig cfg)
	{
		if (cfg.Enabled.HasValue)
		{
			Enabled = cfg.Enabled;
		}
		if (cfg.Ticket != null)
		{
			Ticket = cfg.Ticket;
		}
		if (cfg.AppId.HasValue)
		{
			AppId = cfg.AppId;
		}
	}
}
