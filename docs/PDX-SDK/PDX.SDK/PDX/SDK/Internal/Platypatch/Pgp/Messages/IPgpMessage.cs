using PDX.SDK.Internal.Platypatch.Pgp.Packets;

namespace PDX.SDK.Internal.Platypatch.Pgp.Messages;

public interface IPgpMessage
{
	Packet[] Packets { get; }
}
