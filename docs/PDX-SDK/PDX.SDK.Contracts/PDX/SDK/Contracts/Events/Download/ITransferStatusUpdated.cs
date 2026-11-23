using PDX.SDK.Contracts.Network;

namespace PDX.SDK.Contracts.Events.Download;

public interface ITransferStatusUpdated : IEventBase
{
	ITransferStatus TransferStatus { get; }
}
