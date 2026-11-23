namespace PDX.SDK.Contracts.Service.Mods.Models;

public class ExternalLink
{
	public string Type { get; set; }

	public string URL { get; set; }

	public string DisplayName { get; set; }

	public ExternalLink()
	{
	}

	public ExternalLink(string type, string url, string displayName = null)
	{
		Type = type;
		URL = url;
		DisplayName = displayName;
	}
}
