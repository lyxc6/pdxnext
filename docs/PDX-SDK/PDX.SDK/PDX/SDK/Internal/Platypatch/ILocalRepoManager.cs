using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Platypatch;

internal interface ILocalRepoManager
{
	Task<OperationResult<string>> GetInstalledVersion(FlowData flowData = null);

	Task<Result> WriteCompleteManifest(FolderManifest manifest, FlowData flowData = null);

	Task<Result> WriteDependenciesManifest(FolderManifest manifest, FlowData flowData = null);

	Task<Result> WriteVersionFile(FlowData flowData = null);

	Task<Result> Uninstall(bool clear = true, FlowData flowData = null);

	Task<PatchSizeResult> CalculateSizeOfPatch(RepoMetaData repoMetaData, string versionToInstall, FlowData flowData = null);
}
