using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

public static class CryptUtils
{
	private static Dictionary<HashingAlgorithm, Func<HashAlgorithm>> algoritmToHasherFunction = new Dictionary<HashingAlgorithm, Func<HashAlgorithm>>
	{
		{
			HashingAlgorithm.MD5,
			() => MD5.Create()
		},
		{
			HashingAlgorithm.SHA256,
			() => SHA256.Create()
		},
		{
			HashingAlgorithm.SHA512,
			() => SHA512.Create()
		}
	};

	private static void EncodeString(string toEncode, StringEncoding encoding, out byte[] data)
	{
		if (string.IsNullOrEmpty(toEncode))
		{
			data = new byte[0];
			return;
		}
		switch (encoding)
		{
		case StringEncoding.ASCII:
			data = Encoding.ASCII.GetBytes(toEncode);
			break;
		default:
			data = Encoding.UTF8.GetBytes(toEncode);
			break;
		case StringEncoding.UTF32:
			data = Encoding.UTF32.GetBytes(toEncode);
			break;
		}
	}

	public static string HashString(string toHash, HashingAlgorithm algorithm = HashingAlgorithm.SHA256, StringEncoding encoding = StringEncoding.UTF8)
	{
		if (string.IsNullOrEmpty(toHash))
		{
			return "";
		}
		EncodeString(toHash, encoding, out var data);
		if (data.Length == 0)
		{
			return "";
		}
		using HashAlgorithm hashAlgorithm = algoritmToHasherFunction[algorithm]();
		byte[] array = hashAlgorithm.ComputeHash(data);
		if (array != null && array.Length != 0)
		{
			return HashToReadableHexString(array);
		}
		return "";
	}

	private static string HashToReadableHexString(byte[] hash)
	{
		StringBuilder stringBuilder = new StringBuilder();
		foreach (byte b in hash)
		{
			stringBuilder.Append(b.ToString("x2"));
		}
		return stringBuilder.ToString();
	}
}
