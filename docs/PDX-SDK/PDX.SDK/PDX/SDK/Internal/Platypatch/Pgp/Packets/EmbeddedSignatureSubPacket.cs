using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

internal class EmbeddedSignatureSubPacket : SignatureSubPacket
{
	public SignaturePacket Signature { get; private set; }

	public EmbeddedSignatureSubPacket(SignatureSubPacketType type, uint length, byte[] body)
		: base(type, length, body)
	{
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		TagType type = (TagType)reader.ReadByte();
		byte b = reader.ReadByte();
		byte[] body = reader.ReadBytes(b);
		Signature = new SignaturePacket(type, b, body);
	}
}
