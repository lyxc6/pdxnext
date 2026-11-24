using System;
using System.Security.Cryptography;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Packets;

namespace PDX.SDK.Internal.Platypatch.Pgp.Utils;

internal static class NetCryptograhpyExtensions
{
	public static RSA GetRSA(this PublicKeyPacket packet)
	{
		RSA rSA = RSA.Create();
		rSA.ImportParameters(packet.RsaParameters);
		return rSA;
	}

	public static HashAlgorithmName GetHashAlgorithmName(this PDX.SDK.Internal.Platypatch.Pgp.Enums.HashAlgorithm algorithm)
	{
		return algorithm switch
		{
			PDX.SDK.Internal.Platypatch.Pgp.Enums.HashAlgorithm.SHA256 => HashAlgorithmName.SHA256, 
			PDX.SDK.Internal.Platypatch.Pgp.Enums.HashAlgorithm.SHA1 => HashAlgorithmName.SHA1, 
			PDX.SDK.Internal.Platypatch.Pgp.Enums.HashAlgorithm.MD5 => HashAlgorithmName.MD5, 
			PDX.SDK.Internal.Platypatch.Pgp.Enums.HashAlgorithm.SHA512 => HashAlgorithmName.SHA512, 
			_ => throw new NotImplementedException($"Hash algorithm {algorithm} not supported"), 
		};
	}
}
