using PDX.SDK.Internal.Platypatch.Pgp.Messages;

namespace PDX.SDK.Internal.Platypatch.Pgp;

public class BypassMessageValidator : IMessageValidator
{
	public bool IsValid(PgpSignatureMessage message, byte[] data, KeyRing keyRing)
	{
		return true;
	}

	public bool IsValid(PgpKeyMessage message, KeyRing keyRing)
	{
		return true;
	}
}
