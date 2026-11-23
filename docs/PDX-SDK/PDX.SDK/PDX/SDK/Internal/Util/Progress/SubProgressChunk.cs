using System;

namespace PDX.SDK.Internal.Util.Progress;

internal class SubProgressChunk : ISubProgressChunk
{
	private readonly Action _onProgressChanged;

	public ulong Max { get; }

	public ulong Current { get; private set; }

	public SubProgressChunk(ulong max, Action onProgressChanged = null)
	{
		_onProgressChanged = onProgressChanged;
		Max = max;
	}

	public void RecordProgress(ulong increment)
	{
		Current += increment;
		_onProgressChanged?.Invoke();
	}
}
