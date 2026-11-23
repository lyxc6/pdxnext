namespace PDX.SDK.Internal.Credential.Models;

internal class RenewalAuthModel
{
	public RenewalTokenModel renewal;

	public RenewalAuthModel(string token)
	{
		renewal = new RenewalTokenModel();
		renewal.token = token;
	}
}
