using System;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

internal class PacketFactory
{
	public static Packet Create(TagType type, uint length, byte[] body)
	{
		return type switch
		{
			TagType.Signature => new SignaturePacket(type, length, body), 
			TagType.PublicKey => new PublicKeyPacket(type, length, body, isSubkey: false), 
			TagType.PublicSubkey => new PublicKeyPacket(type, length, body, isSubkey: true), 
			TagType.UserID => new UserIdPacket(type, length, body), 
			_ => throw new NotImplementedException($"Unsupported packet type {type}"), 
		};
	}
}
