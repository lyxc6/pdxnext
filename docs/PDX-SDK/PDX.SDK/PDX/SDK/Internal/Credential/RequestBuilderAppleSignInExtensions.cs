using System.Collections.Generic;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderAppleSignInExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, AppleSignInCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Id_Token))
		{
			builder.SetAuthHeader(new AppleSignInAuthModel(credential.Id_Token).ToJson());
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.AppleSignIn, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, AppleSignInCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Id_Token))
		{
			builder.SetBody(new Dictionary<string, string> { { "id_token", credential.Id_Token } });
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.AppleSignIn, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}
}
