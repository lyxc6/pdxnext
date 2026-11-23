namespace PDX.SDK.Contracts.Credential;

public class StadiaCredential : ICredential
{
	public string IdToken { get; set; }

	public StadiaCredential()
	{
	}

	public StadiaCredential(string idToken)
	{
		IdToken = idToken;
	}
}
