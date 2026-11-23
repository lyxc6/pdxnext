using System.Collections.Generic;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderXboxLiveExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, XboxLiveCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Token) && !string.IsNullOrEmpty(credential.Signature))
		{
			builder.SetAuthHeader(credential.Token);
			builder.AddHeader("Signature", credential.Signature);
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.XboxLive, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, XboxLiveCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Token) && !string.IsNullOrEmpty(credential.Signature))
		{
			builder.SetBody(new Dictionary<string, string>
			{
				{ "xsts_token", credential.Token },
				{ "xsts_signature", credential.Signature }
			});
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.XboxLive, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}
}
