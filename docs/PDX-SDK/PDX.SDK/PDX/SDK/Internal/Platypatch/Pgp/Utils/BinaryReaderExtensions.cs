using System;
using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Models;

namespace PDX.SDK.Internal.Platypatch.Pgp.Utils;

internal static class BinaryReaderExtensions
{
	public static int ReadInt32BigEndian(this BinaryReader reader)
	{
		byte[] array = reader.ReadBytes(4);
		Array.Reverse(array);
		return BitConverter.ToInt32(array, 0);
	}

	public static ulong ReadUInt64BigEndian(this BinaryReader reader)
	{
		byte[] array = reader.ReadBytes(8);
		Array.Reverse(array);
		return BitConverter.ToUInt64(array, 0);
	}

	public static DateTime ReadDateTime(this BinaryReader reader)
	{
		int num = reader.ReadInt32BigEndian();
		return new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddSeconds(num);
	}

	public static MultiPrecisionInteger ReadMultiPrecisionInteger(this BinaryReader reader)
	{
		return new MultiPrecisionInteger(reader);
	}
}
