using PDX.SDK.Contracts.Credential;

namespace PDX.SDK.Internal.Credential;

internal interface ICredentialFactory
{
	ICredential GetEnabledThirdPartyCredential();
}
