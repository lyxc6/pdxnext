using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Models;
using PDX.SDK.Internal.Platypatch.Pgp.Utils;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

public class PublicKeyPacket : Packet
{
	public int Version { get; private set; }

	public DateTime TimeCreated { get; private set; }

	public PublicKeyEncryptionAlgorithm EncryptionAlgorithm { get; private set; }

	public RSAParameters RsaParameters { get; private set; }

	public bool IsSubKey { get; }

	public string KeyId { get; private set; }

	public string Fingerprint { get; private set; }

	public PublicKeyPacket(TagType type, uint length, byte[] body, bool isSubkey)
		: base(type, length, body)
	{
		IsSubKey = isSubkey;
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		Version = reader.ReadByte();
		if (Version == 4)
		{
			TimeCreated = reader.ReadDateTime();
			EncryptionAlgorithm = (PublicKeyEncryptionAlgorithm)reader.ReadByte();
			if (EncryptionAlgorithm == PublicKeyEncryptionAlgorithm.RSASignOnly || EncryptionAlgorithm == PublicKeyEncryptionAlgorithm.RSAEncryptOnly || EncryptionAlgorithm == PublicKeyEncryptionAlgorithm.RSAEncryptOrSign)
			{
				MultiPrecisionInteger multiPrecisionInteger = reader.ReadMultiPrecisionInteger();
				MultiPrecisionInteger multiPrecisionInteger2 = reader.ReadMultiPrecisionInteger();
				RsaParameters = new RSAParameters
				{
					Modulus = multiPrecisionInteger.RawValue,
					Exponent = multiPrecisionInteger2.RawValue
				};
				List<byte> list = new List<byte> { 153 };
				list.Add((byte)((base.Length >> 8) & 0xFF));
				list.Add((byte)(base.Length & 0xFF));
				list.AddRange(base.Body);
				byte[] array = SHA1.Create().ComputeHash(list.ToArray());
				byte[] array2 = array.Skip(array.Length - 8).ToArray();
				Fingerprint = BitConverter.ToString(array).Replace("-", "");
				KeyId = BitConverter.ToString(array2).Replace("-", "");
				return;
			}
			throw new NotImplementedException("Only RSA is supported at this time");
		}
		throw new NotImplementedException($"Version {Version} is not supported");
	}
}
