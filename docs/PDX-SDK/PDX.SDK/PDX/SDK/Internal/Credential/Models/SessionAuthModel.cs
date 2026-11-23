namespace PDX.SDK.Internal.Credential.Models;

internal class SessionAuthModel
{
	public SessionTokenModel session;

	public SessionAuthModel(string token)
	{
		session = new SessionTokenModel();
		session.token = token;
	}
}
