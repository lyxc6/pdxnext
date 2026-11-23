using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class ChangeTrackedConcurrentDictionary<TValue> : IEnumerable<TValue>, IEnumerable where TValue : class
{
	private readonly ConcurrentDictionary<string, ChangeTracked<TValue>> _changeTrackedConcurrentDictionary = new ConcurrentDictionary<string, ChangeTracked<TValue>>();

	private readonly JournalManager _journalManager;

	public ChangeTrackedConcurrentDictionary(JournalManager journalManager)
	{
		_journalManager = journalManager;
	}

	public ChangeTrackedConcurrentDictionary(Dictionary<string, TValue> dictionary, JournalManager journalManager)
	{
		_changeTrackedConcurrentDictionary = new ConcurrentDictionary<string, ChangeTracked<TValue>>(dictionary.Select((KeyValuePair<string, TValue> d) => new KeyValuePair<string, ChangeTracked<TValue>>(d.Key, new ChangeTracked<TValue>(d.Value, (bool isDirty) => OnIsDirtyChanged(d.Key, isDirty)))));
		_journalManager = journalManager;
	}

	public async Task Initialize()
	{
		await _journalManager.Init().ConfigureAwait(continueOnCapturedContext: false);
		foreach (KeyValuePair<string, bool> item in await _journalManager.GetEntries().ConfigureAwait(continueOnCapturedContext: false))
		{
			if (_changeTrackedConcurrentDictionary.TryGetValue(item.Key, out var value))
			{
				value.SetInitialState(item.Value);
			}
		}
	}

	public async Task UpdateJournalName(string journalName)
	{
		await _journalManager.UpdateJournalName(journalName).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<TValue> AddOrUpdate(string key, TValue value)
	{
		ChangeTracked<TValue> addedValue = _changeTrackedConcurrentDictionary.GetOrAdd(key, new ChangeTracked<TValue>(value, (bool isDirty) => OnIsDirtyChanged(key, isDirty)));
		await addedValue.SetTrackedObject(value).ConfigureAwait(continueOnCapturedContext: false);
		return addedValue.TrackedObject;
	}

	public async Task<bool> TryAdd(string key, TValue value)
	{
		Func<bool, Task> onIsDirtyChanged = (bool isDirty) => OnIsDirtyChanged(key, isDirty);
		ChangeTracked<TValue> value2 = new ChangeTracked<TValue>(value, onIsDirtyChanged);
		bool added = _changeTrackedConcurrentDictionary.TryAdd(key, value2);
		if (added)
		{
			try
			{
				await OnIsDirtyChanged(key, isDirty: true).ConfigureAwait(continueOnCapturedContext: false);
			}
			catch (Exception)
			{
				return false;
			}
		}
		return added;
	}

	public async Task<bool> TryUpdate(string key, TValue value)
	{
		if (!_changeTrackedConcurrentDictionary.TryGetValue(key, out var value2))
		{
			return false;
		}
		await value2.SetTrackedObject(value).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public TValue GetValueOrNull(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return null;
		}
		if (!_changeTrackedConcurrentDictionary.TryGetValue(key, out var value))
		{
			return null;
		}
		return value.TrackedObject;
	}

	public async Task<bool> TryRemove(string key)
	{
		if (!_changeTrackedConcurrentDictionary.TryRemove(key, out var _))
		{
			return false;
		}
		return await _journalManager.Remove(key).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task ResetDirty(string key)
	{
		await _changeTrackedConcurrentDictionary[key].ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task ResetAllDirty()
	{
		foreach (KeyValuePair<string, ChangeTracked<TValue>> item in _changeTrackedConcurrentDictionary)
		{
			await item.Value.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public void Clear()
	{
		_changeTrackedConcurrentDictionary.Clear();
	}

	public async Task CleanUp()
	{
		Clear();
		await _journalManager.Clear().ConfigureAwait(continueOnCapturedContext: false);
		await _journalManager.CleanUp().ConfigureAwait(continueOnCapturedContext: false);
	}

	public List<TValue> ToList()
	{
		return _changeTrackedConcurrentDictionary.Values.Select((ChangeTracked<TValue> trackedValue) => trackedValue.TrackedObject).ToList();
	}

	public TValue[] ToArray()
	{
		return _changeTrackedConcurrentDictionary.Values.Select((ChangeTracked<TValue> trackedValue) => trackedValue.TrackedObject).ToArray();
	}

	public bool HasDirty()
	{
		return _changeTrackedConcurrentDictionary.Any((KeyValuePair<string, ChangeTracked<TValue>> x) => x.Value.IsDirty);
	}

	public Dictionary<string, ChangeTracked<TValue>> GetAllDirtyWithTracking()
	{
		return _changeTrackedConcurrentDictionary.Where((KeyValuePair<string, ChangeTracked<TValue>> x) => x.Value.IsDirty).ToDictionary((KeyValuePair<string, ChangeTracked<TValue>> e) => e.Key, (KeyValuePair<string, ChangeTracked<TValue>> e) => e.Value);
	}

	public Dictionary<string, TValue> GetAllDirtyWithoutTracking()
	{
		return _changeTrackedConcurrentDictionary.Where((KeyValuePair<string, ChangeTracked<TValue>> x) => x.Value.IsDirty).ToDictionary((KeyValuePair<string, ChangeTracked<TValue>> e) => e.Key, (KeyValuePair<string, ChangeTracked<TValue>> e) => e.Value.TrackedObject);
	}

	public Dictionary<string, TValue> GetAllWithoutTracking()
	{
		return _changeTrackedConcurrentDictionary.ToDictionary((KeyValuePair<string, ChangeTracked<TValue>> e) => e.Key, (KeyValuePair<string, ChangeTracked<TValue>> e) => e.Value.TrackedObject);
	}

	public IEnumerator<TValue> GetEnumerator()
	{
		return ToList().GetEnumerator();
	}

	IEnumerator IEnumerable.GetEnumerator()
	{
		return GetEnumerator();
	}

	private async Task OnIsDirtyChanged(string key, bool isDirty)
	{
		if (isDirty)
		{
			await _journalManager.SetDirty(key).ConfigureAwait(continueOnCapturedContext: false);
		}
		else
		{
			await _journalManager.ResetDirty(key).ConfigureAwait(continueOnCapturedContext: false);
		}
	}
}
