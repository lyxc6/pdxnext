using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Platypatch.Pgp;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

internal class RepoPgpContext : IRepoPgpContext
{
	public KeyRing KeyRing { get; }

	public RepoPgpContext(BackendEnvironment environment, IMessageValidator messageValidator)
	{
		KeyRing = new KeyRing(messageValidator);
		KeyRing.Add(environment.GetPlatypatchRootKey(), isRootKey: true);
	}

	public void Initialize(PgpKeyMessage repoKey)
	{
		KeyRing.Add(repoKey, isRootKey: false);
	}
}
