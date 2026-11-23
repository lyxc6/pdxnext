using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Platypatch.Pgp;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IRepoPgpContext
{
	KeyRing KeyRing { get; }

	void Initialize(PgpKeyMessage repoKey);
}
