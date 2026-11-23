using System.Threading.Tasks;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Platypatch.Results;
using PDX.SDK.Internal.Util.Progress;

namespace PDX.SDK.Internal.Platypatch;

internal interface IRemoteRepository
{
	Task Initialize(FlowData flowData = null);

	Task<RepoMetaData> GetRepositoryMetaData(FlowData flowData = null);

	Task<FolderManifest> GetCompleteManifest(string version, FlowData flowData = null);

	Task<FolderManifest> GetDependenciesManifest(string version, FlowData flowData = null);

	Task<FolderManifest> GetPatchManifest(string version, string fromVersion, FlowData flowData = null);

	Task<DownloadFilesResult> DownloadFilesInManifest(FolderManifest manifest, string absoluteLocalRootPath, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null);
}
