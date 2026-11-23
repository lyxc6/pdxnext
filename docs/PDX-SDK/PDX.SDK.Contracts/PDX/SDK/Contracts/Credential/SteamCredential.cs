namespace PDX.SDK.Contracts.Credential;

public class SteamCredential : ICredential
{
	public string Ticket { get; set; }

	public uint AppId { get; set; }

	public SteamCredential(string ticket, uint appId)
	{
		Ticket = ticket;
		AppId = appId;
	}
}
