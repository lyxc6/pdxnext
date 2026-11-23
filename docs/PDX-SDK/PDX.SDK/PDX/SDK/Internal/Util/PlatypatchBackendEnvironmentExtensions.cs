using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Platypatch;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;

namespace PDX.SDK.Internal.Util;

internal static class PlatypatchBackendEnvironmentExtensions
{
	internal static PgpKeyMessage GetPlatypatchRootKey(this BackendEnvironment environment)
	{
		return environment switch
		{
			BackendEnvironment.Live => PlatypatchRootKeys.GetLiveKey(), 
			BackendEnvironment.Sandbox => PlatypatchRootKeys.GetSandboxKey(), 
			_ => null, 
		};
	}
}
