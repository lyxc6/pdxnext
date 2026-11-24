using System;
using System.IO;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Events;
using PDX.SDK.Internal.Events.Download;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

internal class DownloadChunkHandler : ChunkHandler
{
	private readonly bool _doNotSetTransferStateToSuccessful;

	private readonly bool _leaveStreamOpen;

	private readonly IInternalEventHub _internalEventHub;

	private int _currentWriteSize;

	private byte[] _writeBuf;

	public DownloadChunkHandler(TransferStatus transfer, int bufSize, Stream fileStream = null, string fileAbsolutePath = null, bool doNotSetTransferStateToSuccessful = false, bool leaveStreamOpen = false, IInternalEventHub internalEventHub = null)
		: base(transfer, bufSize, fileStream, fileAbsolutePath)
	{
		_currentWriteSize = 0;
		_doNotSetTransferStateToSuccessful = doNotSetTransferStateToSuccessful;
		_leaveStreamOpen = leaveStreamOpen;
		_internalEventHub = internalEventHub;
	}

	public override void Initialize()
	{
		_writeBuf = new byte[base.BufferSize];
		if (base.TransferStatus != null)
		{
			(base.TransferStatus as TransferStatus).State = ETransferState.Ongoing;
			(base.TransferStatus as TransferStatus).Start();
			PublishTransferStatusUpdateEvent();
		}
	}

	public override bool HandleBytes(byte[] chunk, int size)
	{
		try
		{
			if (_currentWriteSize + size <= base.BufferSize)
			{
				Buffer.BlockCopy(chunk, 0, _writeBuf, _currentWriteSize, size);
				_currentWriteSize += size;
			}
			else
			{
				if (_currentWriteSize > 0)
				{
					if (base.Stream != null)
					{
						base.Stream.Write(_writeBuf, 0, _currentWriteSize);
					}
					_currentWriteSize = 0;
				}
				if (size >= _writeBuf.Length)
				{
					if (base.Stream != null)
					{
						base.Stream.Write(chunk, 0, size);
					}
				}
				else
				{
					Buffer.BlockCopy(chunk, 0, _writeBuf, 0, size);
					_currentWriteSize = size;
				}
			}
		}
		catch (Exception error)
		{
			if (base.TransferStatus != null)
			{
				(base.TransferStatus as TransferStatus).State = ETransferState.Failed;
				PublishTransferStatusUpdateEvent();
			}
			base.Error = error;
			return false;
		}
		if (base.TransferStatus != null)
		{
			(base.TransferStatus as TransferStatus).AddToProcessedBytes((ulong)((size < 0) ? 0 : size));
			PublishTransferStatusUpdateEvent();
		}
		return true;
	}

	public override void Done()
	{
		if (_currentWriteSize > 0)
		{
			if (base.Stream != null)
			{
				base.Stream.Write(_writeBuf, 0, _currentWriteSize);
				if (!_leaveStreamOpen)
				{
					base.Stream.Dispose();
				}
			}
			_currentWriteSize = 0;
		}
		if (base.TransferStatus != null && !_doNotSetTransferStateToSuccessful)
		{
			(base.TransferStatus as TransferStatus).State = ETransferState.Successful;
			PublishTransferStatusUpdateEvent();
		}
	}

	private void PublishTransferStatusUpdateEvent()
	{
		_internalEventHub?.Publish(new TransferStatusUpdated
		{
			EventId = Guid.NewGuid(),
			TransferStatus = base.TransferStatus
		});
	}
}
