using System.Collections.Generic;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderNintendoAccountExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, NintendoAccountCredential credential)
	{
		builder._context.Log("SetAuthHeader for NintendoAccount, has token " + credential.Token, LogLevel.L0_Info);
		if (!string.IsNullOrEmpty(credential.Token))
		{
			builder.SetAuthHeader(new NintendoAccountAuthModel(credential.Token).ToJson());
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.NintendoAccount, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, NintendoAccountCredential credential)
	{
		Dictionary<string, string> dictionary = new Dictionary<string, string>();
		if (!string.IsNullOrEmpty(credential.Token))
		{
			dictionary.Add("token", credential.Token);
			builder.SetBody(dictionary);
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.NintendoAccount, out var outErrorMessage, isProviderCorrect: false);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}
}
