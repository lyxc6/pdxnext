using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class ModsDownloadProgressController : ProgressControllerBase
{
	private readonly TransferStatus _downloadTransferStatus;

	public ModsDownloadProgressController(int modId)
	{
		_downloadTransferStatus = new TransferStatus(ETransferType.Download, ETransferServiceType.Mods, modId.ToString());
		AddSubProgressChunk(new TransferStatusToSubProgressChunk(_downloadTransferStatus));
		base.Reference = modId.ToString();
	}

	public void MarkAsSuccess()
	{
		_downloadTransferStatus.State = ETransferState.Successful;
	}

	public void MarkAsFailed()
	{
		_downloadTransferStatus.State = ETransferState.Failed;
	}

	public override TransferStatus GetTransferStatus(ETransferType transferType = ETransferType.Download, ETransferServiceType serviceType = ETransferServiceType.NotSpecified, string id = null)
	{
		return _downloadTransferStatus;
	}
}
