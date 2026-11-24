using System.IO;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

public abstract class Packet
{
	public TagType Tag { get; }

	public uint Length { get; }

	public byte[] Body { get; }

	public Packet(TagType type, uint length, byte[] body)
	{
		Tag = type;
		Length = length;
		Body = body;
		using MemoryStream input = new MemoryStream(Body);
		using BinaryReader reader = new BinaryReader(input);
		ParseInternal(reader);
	}

	protected abstract void ParseInternal(BinaryReader reader);
}
