using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

public abstract class SignatureSubPacket
{
	public SignatureSubPacketType Type { get; }

	public uint Length { get; }

	public byte[] Body { get; }

	public SignatureSubPacket(SignatureSubPacketType type, uint length, byte[] body)
	{
		Type = type;
		Length = length;
		Body = body;
		using MemoryStream input = new MemoryStream(Body);
		using BinaryReader reader = new BinaryReader(input);
		ParseInternal(reader);
	}

	protected abstract void ParseInternal(BinaryReader reader);
}
