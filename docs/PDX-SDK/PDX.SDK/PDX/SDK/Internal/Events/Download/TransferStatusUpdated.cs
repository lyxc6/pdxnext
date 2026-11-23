using System;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Contracts.Events.Download;
using PDX.SDK.Contracts.Network;

namespace PDX.SDK.Internal.Events.Download;

public class TransferStatusUpdated : ITransferStatusUpdated, IEventBase
{
	public Guid EventId { get; set; }

	public ITransferStatus TransferStatus { get; set; }
}
