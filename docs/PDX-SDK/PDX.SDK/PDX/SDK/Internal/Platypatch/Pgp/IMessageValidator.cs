using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;

namespace PDX.SDK.Internal.Platypatch.Pgp;

[Preserve]
public interface IMessageValidator
{
	bool IsValid(PgpSignatureMessage message, byte[] data, KeyRing keyRing);

	bool IsValid(PgpKeyMessage message, KeyRing keyRing);
}
