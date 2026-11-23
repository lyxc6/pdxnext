using PDX.SDK.Contracts.Enums;
using PDX.SDK.Internal.Util.Progress;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class ModsUploadProgressController : ProgressControllerBase
{
	private readonly TransferStatus _uploadTransferStatus;

	public ModsUploadProgressController(string id)
	{
		_uploadTransferStatus = new TransferStatus(ETransferType.Upload, ETransferServiceType.Mods, id);
		AddSubProgressChunk(new TransferStatusToSubProgressChunk(_uploadTransferStatus));
		base.Reference = id;
	}

	public void MarkAsSuccess()
	{
		_uploadTransferStatus.State = ETransferState.Successful;
	}

	public void MarkAsFailed()
	{
		_uploadTransferStatus.State = ETransferState.Failed;
	}

	public override TransferStatus GetTransferStatus(ETransferType transferType = ETransferType.Download, ETransferServiceType serviceType = ETransferServiceType.NotSpecified, string id = null)
	{
		return _uploadTransferStatus;
	}
}
