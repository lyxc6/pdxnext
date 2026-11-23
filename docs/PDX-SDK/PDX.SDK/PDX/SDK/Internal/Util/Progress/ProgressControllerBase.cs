using System;
using System.Collections.Generic;
using System.Linq;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util.Progress;

internal class ProgressControllerBase : IProgressController
{
	private readonly List<ISubProgressChunk> _subProgresses = new List<ISubProgressChunk>();

	private float _lastProgress;

	public string Reference { get; protected set; }

	public event EventHandler<float> ProgressChanged;

	public virtual float GetTotalProgress()
	{
		if (_subProgresses.Count == 0)
		{
			return 0f;
		}
		float chunkWorth = 1f / (float)_subProgresses.Count;
		float val = _subProgresses.Where((ISubProgressChunk x) => x.Max != 0).Sum((ISubProgressChunk x) => chunkWorth * ((float)x.Current / (float)x.Max));
		_lastProgress = Math.Max(_lastProgress, val);
		return _lastProgress;
	}

	public ISubProgressChunk GetSubProcessChunk(ulong max)
	{
		SubProgressChunk subProgressChunk = new SubProgressChunk(max, OnProgressChanged);
		_subProgresses.Add(subProgressChunk);
		return subProgressChunk;
	}

	public virtual TransferStatus GetTransferStatus(ETransferType transferType = ETransferType.Download, ETransferServiceType serviceType = ETransferServiceType.NotSpecified, string id = null)
	{
		TransferStatus transferStatus = new TransferStatus(transferType, serviceType, id);
		AddSubProgressChunk(new TransferStatusToSubProgressChunk(transferStatus));
		return transferStatus;
	}

	protected void AddSubProgressChunk(ISubProgressChunk subProgressChunk)
	{
		_subProgresses.Add(subProgressChunk);
	}

	private void OnProgressChanged()
	{
		if (this.ProgressChanged != null)
		{
			this.ProgressChanged(this, GetTotalProgress());
		}
	}
}
