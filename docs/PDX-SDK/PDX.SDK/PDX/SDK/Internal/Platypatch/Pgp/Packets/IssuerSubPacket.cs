using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Utils;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

public class IssuerSubPacket : SignatureSubPacket
{
	public ulong SignerKeyId { get; set; }

	public string SignerkeyIdString { get; set; }

	public IssuerSubPacket(SignatureSubPacketType type, uint length, byte[] body)
		: base(type, length, body)
	{
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		SignerKeyId = reader.ReadUInt64BigEndian();
		SignerkeyIdString = SignerKeyId.ToString("X");
	}
}
