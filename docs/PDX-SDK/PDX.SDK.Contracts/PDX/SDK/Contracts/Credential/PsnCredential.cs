namespace PDX.SDK.Contracts.Credential;

public class PsnCredential : ICredential
{
	public string AuthCode { get; set; }

	public int IssuerId { get; set; }

	public PsnCredential()
	{
	}

	public PsnCredential(string authCode, int issuerId)
	{
		AuthCode = authCode;
		IssuerId = issuerId;
	}
}
