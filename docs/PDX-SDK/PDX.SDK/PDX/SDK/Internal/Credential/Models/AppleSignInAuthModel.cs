namespace PDX.SDK.Internal.Credential.Models;

internal class AppleSignInAuthModel
{
	public IdTokenModel apple;

	public AppleSignInAuthModel(string token)
	{
		apple = new IdTokenModel
		{
			id_token = token
		};
	}
}
