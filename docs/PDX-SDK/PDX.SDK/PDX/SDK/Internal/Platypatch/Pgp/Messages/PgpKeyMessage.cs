using System;
using System.Linq;
using PDX.SDK.Internal.Platypatch.Pgp.Packets;

namespace PDX.SDK.Internal.Platypatch.Pgp.Messages;

public class PgpKeyMessage : IPgpMessage
{
	public Packet[] Packets { get; }

	public string KeyId { get; private set; }

	public DateTime Issued { get; set; }

	public PgpKeyMessage(Packet[] packets)
	{
		Packets = packets;
		PublicKeyPacket publicKeyPacket = packets.OfType<PublicKeyPacket>().FirstOrDefault();
		KeyId = publicKeyPacket.KeyId;
		Issued = publicKeyPacket.TimeCreated;
	}
}
