namespace PDX.SDK.Internal.Credential.Models;

internal class GoogleAuthModel
{
	public IdTokenModel google;

	public GoogleAuthModel(string id_token)
	{
		google = new IdTokenModel
		{
			id_token = id_token
		};
	}
}
