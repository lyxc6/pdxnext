using System;
using System.Threading;
using System.Threading.Tasks;

namespace PDX.SDK.Internal.Util;

public static class SynchronizationContextExtensions
{
	public static Task<T> OnMainThread<T>(this SynchronizationContext unityMainThreadSynchronizationContext, Func<Task<T>> func)
	{
		if (unityMainThreadSynchronizationContext == null)
		{
			return func();
		}
		TaskCompletionSource<T> taskCompletionSource = new TaskCompletionSource<T>();
		unityMainThreadSynchronizationContext.Send(async delegate
		{
			TaskCompletionSource<T> taskCompletionSource2 = taskCompletionSource;
			taskCompletionSource2.SetResult(await func());
		}, null);
		return taskCompletionSource.Task;
	}

	public static Task OnMainThread(this SynchronizationContext unityMainThreadSynchronizationContext, Action action)
	{
		if (unityMainThreadSynchronizationContext == null)
		{
			action();
			return Task.CompletedTask;
		}
		TaskCompletionSource<bool> taskCompletionSource = new TaskCompletionSource<bool>();
		unityMainThreadSynchronizationContext.Send(delegate
		{
			action();
			taskCompletionSource.SetResult(result: true);
		}, null);
		return taskCompletionSource.Task;
	}
}
