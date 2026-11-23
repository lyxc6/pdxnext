using System.Linq;
using PDX.SDK.Internal.Platypatch.Pgp.Packets;

namespace PDX.SDK.Internal.Platypatch.Pgp.Messages;

public class PgpSignatureMessage : IPgpMessage
{
	public Packet[] Packets { get; }

	public string SignedBy { get; private set; }

	public PgpSignatureMessage(Packet[] packets)
	{
		Packets = packets;
		SignedBy = packets.OfType<SignaturePacket>().FirstOrDefault()?.IssuerKey;
	}
}
