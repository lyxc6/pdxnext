using System.Collections.Generic;
using PDX.SDK.Contracts.Credential;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Service.ThirdParty.Enums;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderSteamExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, SteamCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Ticket) && credential.AppId != 0)
		{
			builder.SetAuthHeader(new SteamAuthModel(credential.Ticket, credential.AppId.ToString()).ToJson());
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.Steam, out var outErrorMessage, isProviderCorrect: true, isConfigCorrect: false, isUserActionRequired: true);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}

	internal static RequestBuilder SetBody(this RequestBuilder builder, SteamCredential credential)
	{
		if (!string.IsNullOrEmpty(credential.Ticket) && credential.AppId != 0)
		{
			builder.SetBody(new Dictionary<string, string>
			{
				{
					"appId",
					credential.AppId.ToString()
				},
				{ "ticket", credential.Ticket }
			});
		}
		else
		{
			ThirdPartyErrorMatcher.Match(Provider.Steam, out var outErrorMessage, isProviderCorrect: true, isConfigCorrect: false, isUserActionRequired: true);
			builder._context.Log(outErrorMessage, LogLevel.L3_Error);
			builder.SetError(outErrorMessage);
		}
		return builder;
	}
}
