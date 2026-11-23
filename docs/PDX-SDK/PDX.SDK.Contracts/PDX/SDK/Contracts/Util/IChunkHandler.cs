using System;
using System.IO;
using PDX.SDK.Contracts.Network;

namespace PDX.SDK.Contracts.Util;

public interface IChunkHandler
{
	ITransferStatus TransferStatus { get; set; }

	int BufferSize { get; }

	Stream Stream { get; }

	string FileAbsolutePath { get; }

	Exception Error { get; }

	void Initialize();

	bool HandleBytes(byte[] chunk, int size);

	void Done();
}
