using System.IO;

namespace PDX.SDK.Internal.Platypatch.Pgp.Models;

public class MultiPrecisionInteger
{
	public byte[] RawValue { get; set; }

	public MultiPrecisionInteger(BinaryReader reader)
	{
		int num = (reader.ReadByte() << 8) | reader.ReadByte();
		num = (num + 7) / 8;
		byte[] rawValue = reader.ReadBytes(num);
		RawValue = rawValue;
	}
}
