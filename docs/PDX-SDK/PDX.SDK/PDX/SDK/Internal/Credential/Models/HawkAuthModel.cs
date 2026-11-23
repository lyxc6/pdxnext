namespace PDX.SDK.Internal.Credential.Models;

internal class HawkAuthModel
{
	public HawkDataModal hawk;

	public HawkAuthModel(string email, string hash)
	{
		hawk = new HawkDataModal();
		hawk.email = email;
		hawk.hash = hash;
	}
}
