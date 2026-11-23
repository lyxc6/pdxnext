using PDX.SDK.Contracts.Credential;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Credential;

internal interface IRequestBuilderCredentialBridge
{
	RequestBuilder SetAuthHeader(RequestBuilder builder, ICredential credential);

	RequestBuilder SetBody(RequestBuilder builder, ICredential credential);
}
