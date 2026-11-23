using PDX.SDK.Contracts.Credential;
using PDX.SDK.Internal.Credential.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Credential;

internal static class RequestBuilderRefreshTokenExtensions
{
	internal static RequestBuilder SetAuthHeader(this RequestBuilder builder, RefreshToken credential)
	{
		builder.RegisterOnSendCallback(async delegate
		{
			if (string.IsNullOrEmpty(credential.Refresh))
			{
				string refreshToken = (await builder._context.StateCache.GetSessionData().ConfigureAwait(continueOnCapturedContext: false)).RefreshToken;
				if (!string.IsNullOrEmpty(refreshToken))
				{
					builder.SetAuthHeader(new RenewalAuthModel(refreshToken).ToJson());
				}
			}
			else
			{
				builder.SetAuthHeader(new RenewalAuthModel(credential.Refresh).ToJson());
			}
		});
		return builder;
	}
}
