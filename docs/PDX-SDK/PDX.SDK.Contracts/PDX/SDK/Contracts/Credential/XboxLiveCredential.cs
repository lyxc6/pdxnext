namespace PDX.SDK.Contracts.Credential;

public class XboxLiveCredential : ICredential
{
	public string Token { get; set; }

	public string Signature { get; set; }

	public XboxLiveCredential()
	{
	}

	public XboxLiveCredential(string token, string signature)
	{
		Token = token;
		Signature = signature;
	}
}
