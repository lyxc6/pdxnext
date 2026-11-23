using System.Collections.Generic;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderStadiaExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, StadiaCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.IdToken))
		{
			builder.SetAuthHeader(new StadiaAuthModel(credential.IdToken).ToJson());
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.Stadia, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, StadiaCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.IdToken))
		{
			builder.SetBody(new Dictionary<string, string> { { "id_token", credential.IdToken } });
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.Stadia, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}
}
