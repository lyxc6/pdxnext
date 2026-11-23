using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;
using PDX.SDK.Internal.Platypatch.Pgp.Messages;
using PDX.SDK.Internal.Platypatch.Pgp.Packets;

namespace PDX.SDK.Internal.Platypatch.Pgp;

public static class AsciiArmourParser
{
	public static IPgpMessage GetMessage(string armour)
	{
		string[] array = armour.Split('\n');
		if (array.Length < 6)
		{
			throw new Exception("Bad armour, not enough lines");
		}
		_ = array[0];
		_ = array[1];
		int num = ((!string.IsNullOrWhiteSpace(array[2].Trim())) ? 2 : 3);
		string s = string.Concat(array.Skip(num).Take(array.Length - (num + 2)));
		_ = array[array.Length - 2];
		_ = array[array.Length - 1];
		return ConstructMessage(ParsePackets(Convert.FromBase64String(s)));
	}

	private static IPgpMessage ConstructMessage(Packet[] packets)
	{
		if (packets.OfType<PublicKeyPacket>().Any())
		{
			return new PgpKeyMessage(packets);
		}
		if (packets.OfType<SignaturePacket>().Any())
		{
			return new PgpSignatureMessage(packets);
		}
		throw new Exception("Unknown packet composition");
	}

	private static Packet[] ParsePackets(byte[] binaryData)
	{
		List<Packet> list = new List<Packet>();
		using (MemoryStream input = new MemoryStream(binaryData))
		{
			using BinaryReader binaryReader = new BinaryReader(input);
			while (binaryReader.BaseStream.Position != binaryReader.BaseStream.Length)
			{
				byte b = binaryReader.ReadByte();
				bool num = (b & 0x40) != 0;
				byte b2 = binaryReader.ReadByte();
				uint num2 = 0u;
				int type;
				if (num)
				{
					type = b & 0x3F;
					if (b2 < 192)
					{
						num2 = OneOctetLength(b2);
					}
					else if (b2 >= 192 && b2 <= 223)
					{
						num2 = TwoOctetLengthNewFormat(binaryReader, b2);
					}
					else
					{
						switch (b2)
						{
						case byte.MaxValue:
							num2 = FourOctetLength(binaryReader);
							break;
						case 224:
						case 225:
						case 226:
						case 227:
						case 228:
						case 229:
						case 230:
						case 231:
						case 232:
						case 233:
						case 234:
						case 235:
						case 236:
						case 237:
						case 238:
						case 239:
						case 240:
						case 241:
						case 242:
						case 243:
						case 244:
						case 245:
						case 246:
						case 247:
						case 248:
						case 249:
						case 250:
						case 251:
						case 252:
						case 253:
						case 254:
							throw new NotImplementedException();
						}
					}
				}
				else
				{
					type = (b & 0x7C) >> 2;
					switch (b & 3)
					{
					case 0:
						num2 = OneOctetLength(b2);
						break;
					case 1:
						num2 = TwoOctetLengthOldFormat(binaryReader, b2);
						break;
					case 2:
						num2 = FourOctetLength(binaryReader);
						break;
					case 3:
						num2 = (uint)(binaryReader.BaseStream.Length - binaryReader.BaseStream.Position);
						break;
					}
				}
				byte[] body = binaryReader.ReadBytes((int)num2);
				list.Add(PacketFactory.Create((TagType)type, num2, body));
			}
		}
		return list.ToArray();
	}

	private static uint FourOctetLength(BinaryReader reader)
	{
		byte num = reader.ReadByte();
		byte b = reader.ReadByte();
		byte b2 = reader.ReadByte();
		byte b3 = reader.ReadByte();
		return (uint)((num << 24) | (b << 16) | (b2 << 8) | b3);
	}

	private static uint TwoOctetLengthNewFormat(BinaryReader reader, int lengthPacket)
	{
		byte b = reader.ReadByte();
		return (uint)((lengthPacket - 192 << 8) + b + 192);
	}

	private static uint TwoOctetLengthOldFormat(BinaryReader reader, int lengthPacket)
	{
		byte b = reader.ReadByte();
		return (uint)((lengthPacket << 8) + b);
	}

	private static uint OneOctetLength(int lengthPacket)
	{
		return (uint)lengthPacket;
	}
}
