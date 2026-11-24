namespace PDX.SDK.Internal.Credential.Models;

internal class NintendoAccountAuthModel
{
	public NintendoAccountTokenModel nintendo;

	public NintendoAccountAuthModel(string token)
	{
		nintendo = new NintendoAccountTokenModel
		{
			token = token
		};
	}
}
