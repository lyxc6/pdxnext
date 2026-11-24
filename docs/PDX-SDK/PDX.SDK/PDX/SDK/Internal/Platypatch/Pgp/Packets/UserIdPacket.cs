using System.IO;
using System.Text;
using PDX.SDK.Internal.Platypatch.Pgp.Enums;

namespace PDX.SDK.Internal.Platypatch.Pgp.Packets;

internal class UserIdPacket : Packet
{
	public string UserIdString { get; set; }

	public UserIdPacket(TagType type, uint length, byte[] body)
		: base(type, length, body)
	{
	}

	protected override void ParseInternal(BinaryReader reader)
	{
		byte[] bytes = reader.ReadBytes((int)base.Length);
		UserIdString = Encoding.UTF8.GetString(bytes);
	}
}
