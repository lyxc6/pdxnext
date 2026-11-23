using System;
using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Utils;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

internal class SignatureCreationTimeSubPacket : SignatureSubPacket
{
	public DateTime SigningTime { get; set; }

	public SignatureCreationTimeSubPacket(SignatureSubPacketType type, uint length, byte[] body)
		: base(type, length, body)
	{
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		SigningTime = reader.ReadDateTime();
	}
}
