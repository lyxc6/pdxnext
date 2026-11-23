using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.StandardConstantValues;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Credential;

internal class RequestBuilderCredentialBridge : IRequestBuilderCredentialBridge
{
	public virtual RequestBuilder SetAuthHeader(RequestBuilder builder, ICredential credential)
	{
		if (credential is EmailAndPasswordCredential credential2)
		{
			return RequestBuilderEmailPasswordExtensions.SetAuthHeader(builder, credential2);
		}
		if (credential is RefreshToken credential3)
		{
			return RequestBuilderRefreshTokenExtensions.SetAuthHeader(builder, credential3);
		}
		if (credential is SessionToken credential4)
		{
			return RequestBuilderSessionTokenExtensions.SetAuthHeader(builder, credential4);
		}
		if (credential is PsnCredential credential5)
		{
			return RequestBuilderPsnExtensions.SetAuthHeader(builder, credential5);
		}
		if (credential is XboxLiveCredential credential6)
		{
			return RequestBuilderXboxLiveExtensions.SetAuthHeader(builder, credential6);
		}
		if (credential is NintendoAccountCredential credential7)
		{
			return RequestBuilderNintendoAccountExtensions.SetAuthHeader(builder, credential7);
		}
		if (credential is AppleSignInCredential credential8)
		{
			return RequestBuilderAppleSignInExtensions.SetAuthHeader(builder, credential8);
		}
		if (credential is GoogleCredential credential9)
		{
			return RequestBuilderGoogleExtensions.SetAuthHeader(builder, credential9);
		}
		if (credential is StadiaCredential credential10)
		{
			return RequestBuilderStadiaExtensions.SetAuthHeader(builder, credential10);
		}
		if (credential is SteamCredential credential11)
		{
			return RequestBuilderSteamExtensions.SetAuthHeader(builder, credential11);
		}
		if (credential == null)
		{
			builder._context.Log(string.Format(Defaults.Culture, "Credential is null in SetAuthHeader"));
		}
		else
		{
			builder._context.Log(string.Format(Defaults.Culture, "Unknown Credential type set in Auth header: \"{0}\", the type has not been implemented in PDX.SDK.RequestBuilder.SetAuthHeader(Credential)", credential.ToString()));
		}
		return builder;
	}

	public virtual RequestBuilder SetBody(RequestBuilder builder, ICredential credential)
	{
		if (credential is PsnCredential credential2)
		{
			return RequestBuilderPsnExtensions.SetBody(builder, credential2);
		}
		if (credential is XboxLiveCredential credential3)
		{
			return RequestBuilderXboxLiveExtensions.SetBody(builder, credential3);
		}
		if (credential is NintendoAccountCredential credential4)
		{
			return RequestBuilderNintendoAccountExtensions.SetBody(builder, credential4);
		}
		if (credential is AppleSignInCredential credential5)
		{
			return RequestBuilderAppleSignInExtensions.SetBody(builder, credential5);
		}
		if (credential is GoogleCredential credential6)
		{
			return RequestBuilderGoogleExtensions.SetBody(builder, credential6);
		}
		if (credential is StadiaCredential credential7)
		{
			return RequestBuilderStadiaExtensions.SetBody(builder, credential7);
		}
		if (credential is SteamCredential credential8)
		{
			return RequestBuilderSteamExtensions.SetBody(builder, credential8);
		}
		if (credential == null)
		{
			builder._context.Log(string.Format(Defaults.Culture, "Credential is null in SetBody"));
		}
		else
		{
			builder._context.Log(string.Format(Defaults.Culture, "Unknown Credential type set in Body: \"{0}\", the type has not been implemented in PDX.SDK.RequestBuilder.SetBody(Credential)", credential.ToString()));
		}
		return builder;
	}
}
