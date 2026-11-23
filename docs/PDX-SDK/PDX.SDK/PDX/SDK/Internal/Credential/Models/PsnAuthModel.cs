namespace PDX.SDK.Internal.Credential.Models;

internal class PsnAuthModel
{
	public PsnDataModel psn;

	public PsnAuthModel(string code, int issuer)
	{
		psn = new PsnDataModel
		{
			code = code,
			redirect_uri = "orbis://games",
			issuer = issuer.ToString()
		};
	}
}
