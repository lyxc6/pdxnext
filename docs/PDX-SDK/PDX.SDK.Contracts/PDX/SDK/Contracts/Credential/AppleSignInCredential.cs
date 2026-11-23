namespace PDX.SDK.Contracts.Credential;

public class AppleSignInCredential : ICredential
{
	public string Id_Token { get; set; }

	public AppleSignInCredential()
	{
	}

	public AppleSignInCredential(string id_token)
	{
		Id_Token = id_token;
	}
}
