namespace PDX.SDK.Contracts.Credential;

public class RefreshToken : ICredential
{
	public string Refresh { get; set; }

	public RefreshToken()
	{
	}

	public RefreshToken(string refreshToken)
	{
		Refresh = refreshToken;
	}
}
