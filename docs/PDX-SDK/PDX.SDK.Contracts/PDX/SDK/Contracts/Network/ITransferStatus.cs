using System;
using PDX.SDK.Contracts.Enums;

namespace PDX.SDK.Contracts.Network;

public interface ITransferStatus
{
	ETransferType Type { get; }

	ETransferServiceType ServiceType { get; }

	ETransferState State { get; }

	string Id { get; }

	ulong ProcessedBytes { get; }

	ulong Size { get; }

	float Progress { get; }

	DateTime StartTime { get; }
}
