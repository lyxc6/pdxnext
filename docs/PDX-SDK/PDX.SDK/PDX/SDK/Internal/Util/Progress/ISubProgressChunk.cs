namespace PDX.SDK.Internal.Util.Progress;

internal interface ISubProgressChunk
{
	ulong Max { get; }

	ulong Current { get; }

	void RecordProgress(ulong increment);
}
