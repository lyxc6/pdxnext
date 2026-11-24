namespace PDX.SDK.Internal.Credential.Models;

internal class StadiaAuthModel
{
	public IdTokenModel stadia;

	public StadiaAuthModel(string id_token)
	{
		stadia = new IdTokenModel
		{
			id_token = id_token
		};
	}
}
