using System;
using System.IO;
using PDX.SDK.Contracts.Network;
using PDX.SDK.Contracts.Util;

namespace PDX.SDK.Internal.Util;

internal abstract class ChunkHandler : IChunkHandler
{
	public ITransferStatus TransferStatus { get; set; }

	public int BufferSize { get; }

	public Stream Stream { get; }

	public string FileAbsolutePath { get; }

	public Exception Error { get; protected set; }

	protected ChunkHandler(ITransferStatus transfer, int bufSize, Stream fileStream, string absolutePath)
	{
		TransferStatus = transfer;
		BufferSize = ((bufSize < 32768) ? 32768 : bufSize);
		Stream = fileStream;
		FileAbsolutePath = absolutePath;
	}

	public abstract void Initialize();

	public abstract bool HandleBytes(byte[] chunk, int size);

	public abstract void Done();
}
