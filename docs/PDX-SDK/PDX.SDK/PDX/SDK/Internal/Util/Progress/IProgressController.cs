using System;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util.Progress;

internal interface IProgressController
{
	string Reference { get; }

	event EventHandler<float> ProgressChanged;

	float GetTotalProgress();

	ISubProgressChunk GetSubProcessChunk(ulong max);

	TransferStatus GetTransferStatus(ETransferType transferType = ETransferType.Download, ETransferServiceType serviceType = ETransferServiceType.NotSpecified, string id = null);
}
