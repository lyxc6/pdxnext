using System;
using System.Threading.Tasks;

namespace PDX.SDK.Internal.Service.Mods.Models;

public class ChangeTracked<T> where T : class
{
	private readonly Func<bool, Task> _onIsDirtyChanged;

	internal bool IsDirty { get; private set; }

	internal T TrackedObject { get; private set; }

	internal ChangeTracked(T trackedObject, Func<bool, Task> onIsDirtyChanged = null)
	{
		IsDirty = true;
		TrackedObject = trackedObject;
		_onIsDirtyChanged = onIsDirtyChanged;
	}

	internal async Task ResetIsDirty()
	{
		IsDirty = false;
		if (_onIsDirtyChanged != null)
		{
			await _onIsDirtyChanged(arg: false).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal async Task SetTrackedObject(T trackedObject)
	{
		TrackedObject = trackedObject;
		IsDirty = true;
		if (_onIsDirtyChanged != null)
		{
			await _onIsDirtyChanged(arg: true).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public void SetInitialState(bool dirty)
	{
		IsDirty = dirty;
	}
}
