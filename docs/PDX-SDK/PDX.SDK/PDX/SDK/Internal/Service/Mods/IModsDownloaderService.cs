using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Util.Progress;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IModsDownloaderService
{
	Task<DownloadResult> Download(ModDownloadMetadata modDownloadMetadata, bool doNotSetTransferStateToSuccessful = false, bool useFinalDirAsTemp = false, FlowData flowData = null);

	Task<DownloadResult> DownloadToWip(string folderName, ModDownloadMetadata modDownloadMetadata, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null);

	Task<DownloadResult> DownloadAllImages(ModDownloadMetadata modDownloadMetadata, string modFolderPath, IProgressController progressController = null, FlowData flowData = null);

	Task<Result> DownloadContent(ModDownloadMetadata modDownloadMetadata, string contentFolderPath, IProgressController progressController = null, bool doNotSetTransferStateToSuccessful = false, FlowData flowData = null);

	Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string versionToInstall, FlowData flowData);

	Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string currentlyInstalledVersion, string versionToInstall, FlowData flowData = null);
}
