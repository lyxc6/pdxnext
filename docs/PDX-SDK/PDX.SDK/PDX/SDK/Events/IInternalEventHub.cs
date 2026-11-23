using System;
using PDX.SDK.Contracts.Events;

namespace PDX.SDK.Events;

public interface IInternalEventHub : IEventHub, IDisposable
{
	void RegisterGlobalHandler(Action<Type, object> onMessage);

	void RegisterGlobalErrorHandler(Action<Guid, Exception> onError);

	void Publish<T>(T message);

	void ClearSubscriptions();
}
