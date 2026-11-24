namespace PDX.SDK.Contracts.Credential;

public class SessionToken : ICredential
{
	public string Session { get; set; }

	public SessionToken()
	{
	}

	public SessionToken(string sessionToken)
	{
		Session = sessionToken;
	}
}
