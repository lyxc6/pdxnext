using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;
using PDX.SDK.Internal.Platypatch.Pgp.Packets;
using PDX.SDK.Internal.Platypatch.Pgp.Utils;

namespace PDX.SDK.Internal.Platypatch.Pgp;

[Preserve]
public class MessageValidator : IMessageValidator
{
	public bool IsValid(PgpSignatureMessage message, byte[] data, KeyRing keyRing)
	{
		SignaturePacket signaturePacket = message.Packets.OfType<SignaturePacket>().FirstOrDefault();
		if (signaturePacket == null || signaturePacket.SignatureType != SignatureType.SignatureOfBinaryDocument)
		{
			throw new Exception($"Unsupported signature type {signaturePacket?.SignatureType}");
		}
		RSA rSA = FindInKeyring(message.SignedBy, keyRing).Packets.OfType<PublicKeyPacket>().First((PublicKeyPacket p) => p.KeyId == message.SignedBy).GetRSA();
		List<byte> list = data.ToList();
		if (signaturePacket.Version == 4)
		{
			AddV4Trailer(signaturePacket, list);
		}
		return rSA.VerifyData(list.ToArray(), GetRSASignatureWithPkcs1Padding(signaturePacket.Signature[0].RawValue, rSA.KeySize), signaturePacket.HashAlgorithm.GetHashAlgorithmName(), RSASignaturePadding.Pkcs1);
	}

	private static byte[] GetRSASignatureWithPkcs1Padding(byte[] rsaSignature, int rsaKeyLength)
	{
		int num = rsaKeyLength / 8;
		if (rsaSignature.Length == num)
		{
			return rsaSignature;
		}
		if (rsaSignature.Length > num)
		{
			throw new Exception($"Signature length {rsaSignature.Length} is too long for key length {rsaKeyLength}");
		}
		byte[] array = new byte[num];
		int num2 = num - rsaSignature.Length;
		if (num2 > 0)
		{
			array[0] = 0;
		}
		if (num2 > 1)
		{
			array[1] = 1;
		}
		if (num2 > 2)
		{
			for (int i = 2; i < num2 - 1; i++)
			{
				array[i] = byte.MaxValue;
			}
			array[num2 - 1] = 0;
		}
		rsaSignature.CopyTo(array, num2);
		return array;
	}

	public bool IsValid(PgpKeyMessage message, KeyRing keyRing)
	{
		string selfKeyid = message.KeyId;
		SignaturePacket parentSignature = message.Packets.OfType<SignaturePacket>().FirstOrDefault((SignaturePacket p) => p.IssuerKey != selfKeyid);
		if (parentSignature == null)
		{
			throw new Exception("The key with id " + selfKeyid + " does not have a signature, unable to validate the signature since it is not signed.");
		}
		PgpKeyMessage pgpKeyMessage = FindInKeyring(parentSignature.IssuerKey, keyRing);
		SignatureType signatureType = parentSignature.SignatureType;
		if ((uint)(signatureType - 16) <= 1u || signatureType == SignatureType.PositiveCertificationOfUserIdAndPublicKeyPacket)
		{
			PublicKeyPacket publicKeyPacket = message.Packets.OfType<PublicKeyPacket>().First();
			UserIdPacket userIdPacket = message.Packets.OfType<UserIdPacket>().First();
			List<byte> list = new List<byte> { 153 };
			list.Add((byte)((publicKeyPacket.Length >> 8) & 0xFF));
			list.Add((byte)(publicKeyPacket.Length & 0xFF));
			list.AddRange(publicKeyPacket.Body);
			if (parentSignature.Version == 4)
			{
				list.Add(180);
				list.Add((byte)((userIdPacket.Length >> 24) & 0xFF));
				list.Add((byte)((userIdPacket.Length >> 16) & 0xFF));
				list.Add((byte)((userIdPacket.Length >> 8) & 0xFF));
				list.Add((byte)(userIdPacket.Length & 0xFF));
				list.AddRange(userIdPacket.Body);
				AddV4Trailer(parentSignature, list);
				RSA rSA = pgpKeyMessage.Packets.OfType<PublicKeyPacket>().First((PublicKeyPacket p) => p.KeyId == parentSignature.IssuerKey).GetRSA();
				return rSA.VerifyData(list.ToArray(), GetRSASignatureWithPkcs1Padding(parentSignature.Signature[0].RawValue, rSA.KeySize), parentSignature.HashAlgorithm.GetHashAlgorithmName(), RSASignaturePadding.Pkcs1);
			}
			throw new NotImplementedException("Version 3 signatures not supported....yet");
		}
		throw new NotImplementedException($"Unsupported signature type {parentSignature.SignatureType}");
	}

	private void AddV4Trailer(SignaturePacket signature, List<byte> hashedData)
	{
		hashedData.AddRange(signature.Body.Take((int)signature.HashedLength));
		hashedData.Add(4);
		hashedData.Add(byte.MaxValue);
		hashedData.Add((byte)((signature.HashedLength >> 24) & 0xFF));
		hashedData.Add((byte)((signature.HashedLength >> 16) & 0xFF));
		hashedData.Add((byte)((signature.HashedLength >> 8) & 0xFF));
		hashedData.Add((byte)(signature.HashedLength & 0xFF));
	}

	private PgpKeyMessage FindInKeyring(string keyId, KeyRing keyRing)
	{
		return keyRing[keyId] ?? throw new Exception("Message was signed with key " + keyId + " that is not in keyring, can't validate");
	}
}
