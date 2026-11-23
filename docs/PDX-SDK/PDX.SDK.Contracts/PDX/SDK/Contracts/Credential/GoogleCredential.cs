namespace PDX.SDK.Contracts.Credential;

public class GoogleCredential : ICredential
{
	public string IdToken { get; set; }

	public GoogleCredential()
	{
	}

	public GoogleCredential(string idToken)
	{
		IdToken = idToken;
	}
}
