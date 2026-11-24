using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Network;

namespace PDX.SDK.Network.Models;

public class TransferStatus : ITransferStatus
{
	public ETransferType Type { get; }

	public ETransferServiceType ServiceType { get; }

	public ETransferState State { get; set; }

	public string Id { get; }

	public ulong ProcessedBytes { get; private set; }

	public ulong Size { get; private set; }

	public float Progress
	{
		get
		{
			float num = 0f;
			if (Size != 0)
			{
				num = (float)ProcessedBytes / (float)Size;
			}
			if (num < 0f)
			{
				return 0f;
			}
			if (num > 1f)
			{
				return 1f;
			}
			return num;
		}
	}

	public DateTime StartTime { get; private set; }

	public TransferStatus(ETransferType type, ETransferServiceType service, string id)
	{
		Type = type;
		ServiceType = service;
		State = ETransferState.Ongoing;
		Id = id;
		ProcessedBytes = 0uL;
	}

	public void Start()
	{
		StartTime = DateTime.UtcNow;
	}

	public void SetSize(ulong size)
	{
		if (Size == 0L)
		{
			Size = size;
		}
	}

	public void AddToProcessedBytes(ulong add)
	{
		ProcessedBytes += add;
	}
}
