using System;

namespace PDX.SDK.Contracts.Events;

public interface IEventHub : IDisposable
{
	Guid Subscribe<T>(Action<T> action);

	Guid Subscribe<T>(Action<T> action, TimeSpan throttleBy);

	void Unsubscribe(Guid token);

	bool IsSubscribed(Guid token);
}
