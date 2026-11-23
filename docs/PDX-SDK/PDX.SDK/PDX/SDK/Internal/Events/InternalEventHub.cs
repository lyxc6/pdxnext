using System;
using System.Threading;
using Easy.MessageHub;
using PDX.SDK.Contracts.Events;
using PDX.SDK.Events;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Events;

public class InternalEventHub : IInternalEventHub, IEventHub, IDisposable
{
	private readonly SynchronizationContext _mainThreadSynchronizationContext;

	private readonly MessageHub _messageHub;

	public InternalEventHub(SynchronizationContext mainThreadSynchronizationContext)
	{
		_mainThreadSynchronizationContext = mainThreadSynchronizationContext;
		_messageHub = new MessageHub();
	}

	public void Dispose()
	{
		_messageHub.Dispose();
	}

	public Guid Subscribe<T>(Action<T> action)
	{
		return _messageHub.Subscribe(action);
	}

	public Guid Subscribe<T>(Action<T> action, TimeSpan throttleBy)
	{
		return _messageHub.Subscribe(action, throttleBy);
	}

	public void Unsubscribe(Guid token)
	{
		_messageHub.Unsubscribe(token);
	}

	public bool IsSubscribed(Guid token)
	{
		return _messageHub.IsSubscribed(token);
	}

	public void RegisterGlobalHandler(Action<Type, object> onMessage)
	{
		_messageHub.RegisterGlobalHandler(onMessage);
	}

	public void RegisterGlobalErrorHandler(Action<Guid, Exception> onError)
	{
		_messageHub.RegisterGlobalErrorHandler(onError);
	}

	public void Publish<T>(T message)
	{
		_mainThreadSynchronizationContext.OnMainThread(delegate
		{
			_messageHub.Publish(message);
		});
	}

	public void ClearSubscriptions()
	{
		_messageHub.ClearSubscriptions();
	}
}
