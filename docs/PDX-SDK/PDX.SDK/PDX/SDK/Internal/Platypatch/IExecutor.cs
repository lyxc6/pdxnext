using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Internal.Platypatch.Models;
using PDX.SDK.Internal.Platypatch.Results;
using PDX.SDK.Internal.Util.Progress;

namespace PDX.SDK.Internal.Platypatch;

[Preserve]
internal interface IExecutor
{
	Task<bool> CopyRepoFiles(string sourcepath, string targetpath, IEnumerable<string> filesInFinal = null);

	Task<RepositoryMetaDataResult> GetRepositoryMetaData(string repositoryName, FlowData flowData = null);

	Task<PatchSizeResult> CalculateSizeOfPatch(RepoMetaData repositoryMetadata, string appName, string absoluteDestination, string versionToInstall, FlowData flowData = null);

	Task<Result> InstallToFolder(RepoMetaData repositoryMetadata, string repositoryName, string appName, string absoluteDestination, string version = null, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null);

	Task<Result> Uninstall(string absoluteInstallationPath, string appName, bool clean, FlowData flowData = null);
}
