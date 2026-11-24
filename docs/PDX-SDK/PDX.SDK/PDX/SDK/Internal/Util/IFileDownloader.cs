using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Network.Models;

namespace PDX.SDK.Internal.Util;

[Preserve]
internal interface IFileDownloader
{
	Task<Result> PerformDownload(string remoteUrl, string localAbsolutePath, TransferStatus downloadTransfer = null, FlowData flowData = null, bool doNotSetTransferStateToSuccessful = false, CancellationToken cancellationToken = default(CancellationToken));
}
