using System.IO;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Network;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

internal class UploadChunkHandler : ChunkHandler
{
	public UploadChunkHandler(ITransferStatus transfer, int bufSize, Stream fileStream, string fileAbsolutePath)
		: base(transfer, bufSize, fileStream, fileAbsolutePath)
	{
	}

	public override void Initialize()
	{
		if (base.TransferStatus != null)
		{
			(base.TransferStatus as TransferStatus).State = ETransferState.Ongoing;
			(base.TransferStatus as TransferStatus).Start();
		}
	}

	public override bool HandleBytes(byte[] chunk, int size)
	{
		if (base.TransferStatus != null)
		{
			(base.TransferStatus as TransferStatus).AddToProcessedBytes((ulong)((size < 0) ? 0 : size));
		}
		return true;
	}

	public override void Done()
	{
		if (base.TransferStatus != null)
		{
			(base.TransferStatus as TransferStatus).State = ETransferState.Successful;
		}
	}
}
