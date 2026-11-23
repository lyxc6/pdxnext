using PDX.SDK.Contracts.Network;

namespace PDX.SDK.Internal.Util.Progress;

internal class TransferStatusToSubProgressChunk : ISubProgressChunk
{
	private readonly ITransferStatus _transferStatus;

	public ulong Max => _transferStatus.Size;

	public ulong Current => (ulong)(_transferStatus.Progress * (float)_transferStatus.Size);

	public TransferStatusToSubProgressChunk(ITransferStatus transferStatus)
	{
		_transferStatus = transferStatus;
	}

	public void RecordProgress(ulong increment)
	{
	}
}
