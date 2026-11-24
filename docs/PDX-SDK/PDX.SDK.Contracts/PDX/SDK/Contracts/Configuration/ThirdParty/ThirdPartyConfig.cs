namespace PDX.SDK.Contracts.Configuration.ThirdParty;

public class ThirdPartyConfig
{
	public XboxConfig Xbox { get; set; }

	public PsnConfig Psn { get; set; }

	public NintendoConfig Nintendo { get; set; }

	public AppleConfig Apple { get; set; }

	public GoogleConfig Google { get; set; }

	public StadiaConfig Stadia { get; set; }

	public SteamConfig Steam { get; set; }

	public bool? AutoLogin { get; set; }

	public bool? CheckForThirdPartyAutoLoginPrompt { get; set; }

	public ThirdPartyConfig()
	{
		Xbox = new XboxConfig();
		Psn = new PsnConfig();
		Nintendo = new NintendoConfig();
		Apple = new AppleConfig();
		Google = new GoogleConfig();
		Stadia = new StadiaConfig();
		Steam = new SteamConfig();
	}

	public void SetIfNotNull(ThirdPartyConfig cfg)
	{
		if (cfg.AutoLogin.HasValue)
		{
			AutoLogin = cfg.AutoLogin;
		}
		if (cfg.CheckForThirdPartyAutoLoginPrompt.HasValue)
		{
			CheckForThirdPartyAutoLoginPrompt = cfg.CheckForThirdPartyAutoLoginPrompt;
		}
		if (cfg.Xbox != null)
		{
			Xbox.SetIfNotNull(cfg.Xbox);
		}
		if (cfg.Psn != null)
		{
			Psn.SetIfNotNull(cfg.Psn);
		}
		if (cfg.Nintendo != null)
		{
			Nintendo.SetIfNotNull(cfg.Nintendo);
		}
		if (cfg.Apple != null)
		{
			Apple.SetIfNotNull(cfg.Apple);
		}
		if (cfg.Google != null)
		{
			Google.SetIfNotNull(cfg.Google);
		}
		if (cfg.Stadia != null)
		{
			Stadia.SetIfNotNull(cfg.Stadia);
		}
		if (cfg.Steam != null)
		{
			Steam.SetIfNotNull(cfg.Steam);
		}
	}
}
