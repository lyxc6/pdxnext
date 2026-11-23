namespace PDX.SDK.Internal.Credential.Models;

internal class SteamAuthModel
{
	public SteamDataModel steam;

	public SteamAuthModel(string ticket, string appId)
	{
		steam = new SteamDataModel
		{
			ticket = ticket,
			appId = appId
		};
	}
}
