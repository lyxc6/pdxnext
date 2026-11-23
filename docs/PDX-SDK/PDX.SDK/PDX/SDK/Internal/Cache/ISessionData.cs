using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Internal.Cache;

internal interface ISessionData
{
	string SessionToken { get; }

	string RefreshToken { get; }

	TrustLevel TrustLevel { get; }

	string Namespace { get; }
}
