using PDX.SDK.Contracts.Credential;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderSessionTokenExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, SessionToken credential)
	{
		builder.RegisterOnSendCallback(async delegate
		{
			if (string.IsNullOrEmpty(credential.Session))
			{
				string sessionToken = (await builder._context.StateCache.GetSessionData().ConfigureAwait(continueOnCapturedContext: false)).SessionToken;
				if (!string.IsNullOrEmpty(sessionToken))
				{
					builder.SetAuthHeader(new SessionAuthModel(sessionToken).ToJson());
				}
			}
			else
			{
				builder.SetAuthHeader(new SessionAuthModel(credential.Session).ToJson());
			}
		});
		return builder;
	}
}
