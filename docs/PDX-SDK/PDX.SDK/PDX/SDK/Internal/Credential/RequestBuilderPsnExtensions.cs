using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderPsnExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, PsnCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.AuthCode))
		{
			return builder.SetAuthHeader(new PsnAuthModel(credential.AuthCode, credential.IssuerId).ToJson());
		}
		ThirdPartyErrorMatcher.Match(Provider.PSN, out var outErrorMessage, isProviderCorrect: false);
		builder._context.Log(outErrorMessage, LogLevel.L3_Error);
		builder.SetError(outErrorMessage);
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, PsnCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.AuthCode))
		{
			return builder.SetBody(new PsnAuthModel(credential.AuthCode, credential.IssuerId).psn);
		}
		ThirdPartyErrorMatcher.Match(Provider.PSN, out var outErrorMessage, isProviderCorrect: false);
		builder._context.Log(outErrorMessage, LogLevel.L3_Error);
		builder.SetError(outErrorMessage);
		return builder;
	}
}
