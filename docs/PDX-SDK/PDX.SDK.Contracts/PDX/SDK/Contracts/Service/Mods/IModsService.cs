using System.Collections.Generic;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;

namespace PDX.SDK.Contracts.Service.Mods;

[Preserve]
public interface IModsService
{
	bool SyncOngoing();

	Task<PDX.SDK.Contracts.Result> Disable(int modId, int playsetId);

	Task<PDX.SDK.Contracts.Result> Disable(string modName, int playsetId);

	Task<BulkResult> DisableBulk(List<int> modIds, int playsetId);

	Task<BulkResult> DisableBulk(List<string> modNames, int playsetId);

	Task<BulkResult> DisableBulk(int playsetId);

	Task<PDX.SDK.Contracts.Result> Enable(int modId, int playsetId, bool enableOnBackend = true);

	Task<PDX.SDK.Contracts.Result> Enable(string modName, int playsetId);

	Task<BulkResult> EnableBulk(List<string> modNames, int playsetId);

	Task<BulkResult> EnableBulk(List<int> modIds, int playsetId);

	Task<BulkResult> EnableBulk(int playsetId);

	Task<ModDependenciesResult> GetAllDependencies(List<int> mods, int? playsetId = null);

	Task<ModDependenciesResult> GetDependencyCandidates(int modId, string searchQuery = null, int? amount = null);

	Task<ModDependantsResult> GetModDependants(List<int> modIds, int playsetId = 0);

	Task<GetDetailsResult> GetDetails(int modId, string version = null, ModPlatform? os = null);

	Task<GetDetailsResult> GetDetails(IMod mod, ModPlatform? os = null);

	Task<GetDetailsResult> GetDetails(string modName);

	Task<GetForumThreadResult> GetForumThread(int modId, string modVersion, int threadId, int page, int limit);

	Task<CreateForumPostResult> CreateForumPost(int modId, string modVersion, int threadId, string message);

	Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string versionToInstall);

	Task<GetGameDataResult> GetGameData();

	Task<GetUserRatingResult> GetUserRating(int modId);

	Task<GetModVersionsResult> GetVersions(int modId);

	Task<ListOfFeaturedModsResult> GetFeaturedModsLists(int? page = null, int? limit = null);

	bool HasLocalChanges();

	Task<PlaysetSyncConflict[]> GetSyncConflicts();

	Task<ModListResult> List(string searchQuery = null, InstalledModsSortMethod sortMethod = InstalledModsSortMethod.RecentFirst, List<string> filterTags = null);

	Task<PublishResult> PublishWIP(PublishWipData publishWipData);

	Task<PublishUpdateResult> PublishNewModVersion(PublishUpdateData publishNewVersionData);

	Task<PublishUpdateResult> UpdatePublishedModVersion(PublishUpdateData publishUpdateData, string version = null);

	Task<RateResult> Rate(int modId, int rating);

	Task<RegisterResult> RegisterWIP(string displayName, string shortDescription = null, string longDescription = null, ulong size = 0uL);

	Task<RegisterResult> RegisterExistingWIP(UpdateWipData wipData, string path, int? modId = null);

	Task<PDX.SDK.Contracts.Result> Report(int modId, ReportReason reason, string description = null);

	Task<LoadOrderResult> ResetLoadOrder(int playsetId);

	Task<ModSearchResult> Search(SearchData searchData);

	Task<LoadOrderResult> SetLoadOrder(List<ModLoadOrder> modLoadOrderList, int playsetId);

	Task<LoadOrderResult> SetLoadOrder(SearchOrder orderBy, int playsetId);

	Task<SubscribeResult> Subscribe(int modId, int? playsetId = null, string version = null);

	Task<SubscribeResult> Subscribe(string modName, int? playsetId = null);

	Task<SubscribeResult> SubscribeBulk(IEnumerable<KeyValuePair<int, string>> mods, int? playsetId = null);

	Task<SubscribeResult> SubscribeBulk(IEnumerable<IMod> mods, int? playsetId = null);

	Task<PDX.SDK.Contracts.Result> Sync(SyncDirection syncDirection = SyncDirection.Default);

	Task<PDX.SDK.Contracts.Result> SyncPlayset(int playsetId, SyncDirection syncDirection);

	Task<RegisterResult> SyncWIPMod(int modId, string version = null);

	Task<PDX.SDK.Contracts.Result> Unpublish(int modId);

	Task<PDX.SDK.Contracts.Result> UnregisterWIP(string guid);

	Task<PDX.SDK.Contracts.Result> Unsubscribe(int modId, int playsetId);

	Task<PDX.SDK.Contracts.Result> Unsubscribe(int modId);

	Task<PDX.SDK.Contracts.Result> Unsubscribe(string modName, int playsetId);

	Task<PDX.SDK.Contracts.Result> Unsubscribe(string modName);

	Task<PDX.SDK.Contracts.Result> UpdateWIP(UpdateWipData updateWipData);

	Task<CreatePlaysetResult> CreatePlayset(string playsetName);

	Task<RenamePlaysetResult> RenamePlayset(int playsetId, string playsetName);

	Task<PDX.SDK.Contracts.Result> DeletePlayset(int playsetId, bool abortOnLocalFailure = true);

	Task<ListAllPlaysetsResult> ListAllPlaysets(bool includeOnline = false);

	Task<ListModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, bool includeOnline = false);

	Task<CreatePlaysetResult> ClonePlayset(int playsetId);

	Task<PDX.SDK.Contracts.Result> ActivatePlayset(int playsetId);

	Task<PDX.SDK.Contracts.Result> DeactivateActivePlayset();

	Task<GetActivePlaysetResult> GetActivePlayset();

	Task<ModListResult> GetActivePlaysetEnabledMods();

	Task<ReportModCreatorResult> ReportModCreator(string userName, ReportReason reportReason, string description);

	Task<GetFollowedModCreatorsResult> GetFollowedModCreators(int? limit = 20, int? page = 1);

	Task<GetModCreatorsProfileResult> GetModCreatorProfile(string userName);

	Task<PDX.SDK.Contracts.Result> FollowModCreator(string userName);

	Task<PDX.SDK.Contracts.Result> UnfollowModCreator(string userName);

	Task<PDX.SDK.Contracts.Result> RefreshUnmanagedMods();

	Task<PDX.SDK.Contracts.Result> CleanupReportedMods();

	Task<PDX.SDK.Contracts.Result> CleanupAllPDXMods();

	Task<GetThirdPartyProfilesResult> GetThirdPartyProfiles(IEnumerable<string> userIds);
}
