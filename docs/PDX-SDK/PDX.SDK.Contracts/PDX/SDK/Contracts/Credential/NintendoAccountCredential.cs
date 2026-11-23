namespace PDX.SDK.Contracts.Credential;

public class NintendoAccountCredential : ICredential
{
	public string Token { get; set; }

	public NintendoAccountCredential()
	{
	}

	public NintendoAccountCredential(string token)
	{
		Token = token;
	}
}
