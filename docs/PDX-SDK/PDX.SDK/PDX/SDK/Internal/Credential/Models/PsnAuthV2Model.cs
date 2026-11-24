namespace PDX.SDK.Internal.Credential.Models;

internal class PsnAuthV2Model
{
	public Psnv2DataModel psnv2;

	public PsnAuthV2Model(string code, int issuer)
	{
		psnv2 = new Psnv2DataModel
		{
			authorization_code = code,
			redirect_uri = "orbis://games",
			issuer = issuer.ToString()
		};
	}
}
