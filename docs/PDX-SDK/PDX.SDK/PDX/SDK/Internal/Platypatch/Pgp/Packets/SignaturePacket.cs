using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Models;
using PDX.SDK.Internal.Platypatch.Pgp.Utils;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

public class SignaturePacket : Packet
{
	public int Version { get; private set; }

	public SignatureType SignatureType { get; private set; }

	public PublicKeyEncryptionAlgorithm PublicKeyAlgorithm { get; private set; }

	public HashAlgorithm HashAlgorithm { get; private set; }

	public SignatureSubPacket[] HashedSubpacketDataSet { get; private set; }

	public SignatureSubPacket[] UnhashedSubpacketDataSet { get; private set; }

	public MultiPrecisionInteger[] Signature { get; private set; }

	public byte[] Fingerprint { get; private set; }

	public string IssuerKey { get; private set; }

	public uint HashedLength { get; set; }

	public SignaturePacket(TagType type, uint length, byte[] body)
		: base(type, length, body)
	{
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		Version = reader.ReadByte();
		if (Version != 3)
		{
			if (Version != 4)
			{
				throw new Exception("Only version 3 or 4 is supported");
			}
			SignatureType = (SignatureType)reader.ReadByte();
			PublicKeyAlgorithm = (PublicKeyEncryptionAlgorithm)reader.ReadByte();
			HashAlgorithm = (HashAlgorithm)reader.ReadByte();
			int num = (reader.ReadByte() << 8) | reader.ReadByte();
			if (num > 0)
			{
				HashedSubpacketDataSet = ProcessSubpackets(reader, num);
			}
			HashedLength = (uint)reader.BaseStream.Position;
			num = (reader.ReadByte() << 8) | reader.ReadByte();
			if (num > 0)
			{
				UnhashedSubpacketDataSet = ProcessSubpackets(reader, num);
			}
			Fingerprint = new byte[2]
			{
				reader.ReadByte(),
				reader.ReadByte()
			};
			if (PublicKeyAlgorithm != PublicKeyEncryptionAlgorithm.RSAEncryptOrSign && PublicKeyAlgorithm != PublicKeyEncryptionAlgorithm.RSASignOnly)
			{
				throw new NotImplementedException("Only RSA is supported at this time");
			}
			List<MultiPrecisionInteger> list = new List<MultiPrecisionInteger>();
			while (reader.PeekChar() != -1)
			{
				list.Add(reader.ReadMultiPrecisionInteger());
			}
			Signature = list.ToArray();
			IssuerKey = (HashedSubpacketDataSet.OfType<IssuerSubPacket>().FirstOrDefault() ?? UnhashedSubpacketDataSet.OfType<IssuerSubPacket>().FirstOrDefault())?.SignerkeyIdString;
		}
	}

	private SignatureSubPacket[] ProcessSubpackets(BinaryReader reader, int count)
	{
		List<SignatureSubPacket> list = new List<SignatureSubPacket>();
		int num = 0;
		while (num < count)
		{
			byte b = reader.ReadByte();
			num++;
			SignatureSubPacketType signatureSubPacketType = (SignatureSubPacketType)reader.ReadByte();
			num++;
			num += b - 1;
			byte[] body = reader.ReadBytes(b - 1);
			switch (signatureSubPacketType)
			{
			case SignatureSubPacketType.SignatureCreationTime:
				list.Add(new SignatureCreationTimeSubPacket(signatureSubPacketType, b, body));
				break;
			case SignatureSubPacketType.Issuer:
				list.Add(new IssuerSubPacket(signatureSubPacketType, b, body));
				break;
			case SignatureSubPacketType.EmbeddedSignature:
				list.Add(new EmbeddedSignatureSubPacket(signatureSubPacketType, b, body));
				break;
			}
		}
		return list.ToArray();
	}
}
