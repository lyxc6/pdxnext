using System;

namespace PDX.SDK.Contracts.Events;

public interface IEventBase
{
	Guid EventId { get; }
}
