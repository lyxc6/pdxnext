using System;

namespace PDX.SDK.Contracts.Events;

public interface IEventWithAcknowledgementBase
{
	Guid EventId { get; }

	Action<EventAcknowledgement> Acknowledgement { get; }
}
