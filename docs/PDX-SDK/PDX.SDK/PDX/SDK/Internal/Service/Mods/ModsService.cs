using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Contracts.Service.Profile.Result;
using PDX.SDK.Internal.Errors;
using PDX.SDK.Internal.Events;
using PDX.SDK.Internal.Events.Internal.Mods;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Service.ThirdParty;
using PDX.SDK.Internal.Util;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsService : InternalService, IModsService
{
	private readonly IPathFactory _pathFactory;

	private readonly IPlaysetsAndModsOperationCoordinator _syncCoordinator;

	internal IModsDownloaderService ModsDownloader { get; }

	internal IModsSubscriptionService ModsSubscriptionService { get; }

	internal IPlaysetAndModsSyncService PlaysetAndModSyncService { get; }

	internal IModsPublishService ModPublishService { get; }

	internal IModsBackendApiService ModsBackendApiService { get; }

	internal IModsCache ModsCache { get; }

	[Preserve]
	internal ModsService(IContext context, IPathFactory pathFactory, IModsDownloaderService modsDownloader, IModsSubscriptionService modSubscriptionService, IPlaysetAndModsSyncService playsetAndModSyncService, IModsPublishService modPublishService, IModsCache modsCache, IModsBackendApiService modsBackendApiService, IPlaysetsAndModsOperationCoordinator syncCoordinator)
		: base("Mods", context as Context)
	{
		ModsDownloader = modsDownloader;
		_pathFactory = pathFactory;
		_syncCoordinator = syncCoordinator;
		ModsSubscriptionService = modSubscriptionService;
		PlaysetAndModSyncService = playsetAndModSyncService;
		ModPublishService = modPublishService;
		ModsBackendApiService = modsBackendApiService;
		ModsCache = modsCache;
	}

	public bool SyncOngoing()
	{
		return _syncCoordinator.IsSyncOngoing;
	}

	public async Task<GetModVersionsResult> GetVersions(int modId)
	{
		return await GetVersions(modId, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ListOfFeaturedModsResult> GetFeaturedModsLists(int? page = null, int? limit = null)
	{
		return await GetFeaturedModsLists(page, limit, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Sync(SyncDirection syncDirection = SyncDirection.Default)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => PlaysetAndModSyncService.Sync(syncDirection)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> SyncPlayset(int playsetId, SyncDirection syncDirection)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => PlaysetAndModSyncService.SyncPlayset(playsetId, syncDirection)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<RegisterResult> SyncWIPMod(int modId, string version = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => SyncWIPMod(modId, version, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<SubscribeResult> Subscribe(int modId, int? playsetId = null, string version = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => Subscribe(modId, playsetId, version, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<SubscribeResult> Subscribe(string modName, int? playsetId = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => Subscribe(modName, playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<SubscribeResult> SubscribeBulk(IEnumerable<KeyValuePair<int, string>> mods, int? playsetId = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => SubscribeBulk(mods.Select((KeyValuePair<int, string> m) => new Mod
		{
			Id = m.Key,
			Version = m.Value,
			LocalData = new LocalData
			{
				LocalType = LocalType.Subscribed
			}
		}), playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<SubscribeResult> SubscribeBulk(IEnumerable<IMod> mods, int? playsetId = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => SubscribeBulk(mods, playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unsubscribe(int modId, int playsetId)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsSubscriptionService.Unsubscribe(modId, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unsubscribe(int modId)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsSubscriptionService.Unsubscribe(modId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unsubscribe(string modName, int playsetId)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsSubscriptionService.Unsubscribe(modName, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unsubscribe(string modName)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsSubscriptionService.Unsubscribe(modName)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModSearchResult> Search(SearchData searchData)
	{
		return await Search(searchData, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Report(int modId, ReportReason reason, string description = null)
	{
		return await Report(modId, reason, description, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<RegisterResult> RegisterWIP(string displayName, string shortDescription = null, string longDescription = null, ulong size = 0uL)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsCache.RegisterWIP(displayName, shortDescription, longDescription, size)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<RegisterResult> RegisterExistingWIP(UpdateWipData wipData, string path, int? modId = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsCache.RegisterExistingWIP(wipData, path, modId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> UnregisterWIP(string guid)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsCache.UnregisterWIP(guid)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> UpdateWIP(UpdateWipData updateWipData)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsCache.UpdateWIP(updateWipData)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<RateResult> Rate(int modId, int rating)
	{
		return await Rate(modId, rating, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetUserRatingResult> GetUserRating(int modId)
	{
		return await GetUserRating(modId, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PublishResult> PublishWIP(PublishWipData publishWipData)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => PublishWIP(publishWipData, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PublishUpdateResult> PublishNewModVersion(PublishUpdateData publishNewVersionData)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => PublishNewModVersion(publishNewVersionData, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<PublishUpdateResult> UpdatePublishedModVersion(PublishUpdateData publishUpdateData, string version = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => UpdatePublishedModVersion(publishUpdateData, version, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Unpublish(int modId)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModPublishService.Unpublish(modId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModListResult> List(string searchQuery = null, InstalledModsSortMethod sortMethod = InstalledModsSortMethod.RecentFirst, List<string> filterTags = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => List(searchQuery, sortMethod, filterTags, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetGameDataResult> GetGameData()
	{
		GetGameDataResult gameDataResult = await ModsBackendApiService.GetGameData().ConfigureAwait(continueOnCapturedContext: false);
		if (!gameDataResult.Success)
		{
			return gameDataResult;
		}
		GetModsGameDataResult getModsGameDataResult = await ModsBackendApiService.GetModsGameData().ConfigureAwait(continueOnCapturedContext: false);
		if (!getModsGameDataResult.Success)
		{
			gameDataResult.Success = false;
			gameDataResult.Error = getModsGameDataResult.Error;
			return gameDataResult;
		}
		gameDataResult.Filters = getModsGameDataResult.Filters;
		return gameDataResult;
	}

	public async Task<Result> Enable(int modId, int playsetId, bool enableOnBackend = true)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.Enable(modId, playsetId, enableOnBackend)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Enable(string modName, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.Enable(modName, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> EnableBulk(List<string> modNames, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.EnableBulk(modNames, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> EnableBulk(List<int> modIds, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.EnableBulk(modIds, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> EnableBulk(int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.EnableBulk(playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Disable(int modId, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.Disable(modId, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> Disable(string modName, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.Disable(modName, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> DisableBulk(List<int> modIds, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.DisableBulk(modIds, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> DisableBulk(List<string> modNames, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.DisableBulk(modNames, playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<BulkResult> DisableBulk(int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ModsSubscriptionService.DisableBulk(playsetId)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDetailsResult> GetDetails(IMod mod, ModPlatform? os = null)
	{
		if (mod.Id <= 0)
		{
			return await GetDetails(mod.Name).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await GetDetails(mod.Id, mod.Version, os).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDetailsResult> GetDetails(int modId, string version = null, ModPlatform? os = null)
	{
		return await GetDetails(modId, version, os, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetDetailsResult> GetDetails(string modName)
	{
		return await GetDetails(modName, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetForumThreadResult> GetForumThread(int modId, string modVersion, int threadId, int page, int limit)
	{
		return await GetForumThread(modId, modVersion, threadId, page, limit, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<CreateForumPostResult> CreateForumPost(int modId, string modVersion, int threadId, string message)
	{
		return await CreateForumPost(modId, modVersion, threadId, message, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string versionToInstall)
	{
		return await ModsDownloader.GetRequiredInstallSize(modDetails, versionToInstall, (FlowData)null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModInstallSizeResult> GetRequiredInstallSize(ModDetails modDetails, string currentlyInstalledVersion, string versionToInstall)
	{
		return await ModsDownloader.GetRequiredInstallSize(modDetails, currentlyInstalledVersion, versionToInstall).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModDependenciesResult> GetAllDependencies(List<int> mods, int? playsetId = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => GetAllDependencies(mods, playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModDependenciesResult> GetDependencyCandidates(int modId, string searchQuery = null, int? amount = null)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => GetDependencyCandidates(modId, searchQuery, amount, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModDependantsResult> GetModDependants(List<int> modIds, int playsetId = 0)
	{
		return await GetModDependants(modIds, playsetId, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoadOrderResult> SetLoadOrder(List<ModLoadOrder> modLoadOrderList, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => SetLoadOrder(modLoadOrderList, playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoadOrderResult> SetLoadOrder(SearchOrder orderBy, int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => SetLoadOrder(orderBy, playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoadOrderResult> ResetLoadOrder(int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ResetLoadOrder(playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public bool HasLocalChanges()
	{
		return HasLocalChanges(null);
	}

	public async Task<PlaysetSyncConflict[]> GetSyncConflicts()
	{
		return await GetSyncConflicts(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<CreatePlaysetResult> CreatePlayset(string playsetName)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => CreatePlayset(playsetName, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<RenamePlaysetResult> RenamePlayset(int playsetId, string playsetName)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => RenamePlayset(playsetId, playsetName, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> DeletePlayset(int playsetId, bool abortOnLocalFailure)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => DeletePlayset(playsetId, abortOnLocalFailure, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ListAllPlaysetsResult> ListAllPlaysets(bool includeOnline = false)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ListAllPlaysets(includeOnline, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ListModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, bool includeOnline = false)
	{
		return await _syncCoordinator.ExecuteIfNotSyncing(() => ModsSubscriptionService.ListModsInPlayset(playsetId, limit, page, includeOnline)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<CreatePlaysetResult> ClonePlayset(int playsetId)
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => ClonePlayset(playsetId, null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> ActivatePlayset(int playsetId)
	{
		return await ActivatePlayset(playsetId, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> DeactivateActivePlayset()
	{
		return await DeactivateActivePlayset(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetActivePlaysetResult> GetActivePlayset()
	{
		return await GetActivePlayset(null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModListResult> GetActivePlaysetEnabledMods()
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => GetActivePlaysetEnabledMods(null)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ReportModCreatorResult> ReportModCreator(string userName, ReportReason reportReason, string description)
	{
		return await ReportModCreator(userName, reportReason, description, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetFollowedModCreatorsResult> GetFollowedModCreators(int? limit = 20, int? page = 1)
	{
		return await GetFollowedModCreators(limit, page, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetModCreatorsProfileResult> GetModCreatorProfile(string userName)
	{
		return await GetModCreatorProfile(userName, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> FollowModCreator(string userName)
	{
		return await FollowModCreator(userName, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> UnfollowModCreator(string userName)
	{
		return await UnfollowModCreator(userName, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> RefreshUnmanagedMods()
	{
		FlowData flowData = new FlowData();
		Log("ModsService.RefreshUnmanagedMods", LogLevel.L1_Debug, flowData);
		return await ModsCache.RefreshUnmanagedModsCache().ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> CleanupReportedMods()
	{
		return await _syncCoordinator.ExecuteWithSyncLock(() => PlaysetAndModSyncService.CleanupReportedMods()).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> CleanupAllPDXMods()
	{
		return await _syncCoordinator.ExecuteWithSyncLock(async delegate
		{
			FlowData fd = new FlowData();
			Log("Mods.CleanupAllPDXMods", LogLevel.L1_Debug, fd);
			FileIOStatus fileIOStatus = await CleanupSubscribed.CleanupSubscribedModsFolder(_context, _pathFactory.GetModsPath(), fd);
			if (!fileIOStatus.Success)
			{
				Enum arg = fileIOStatus.ErrorCode.ToInternalError();
				Log($"Cleaning up the Subscribed mods folder failed with error: {arg}", LogLevel.L2_Warning, fd);
			}
			return await ModsCache.CleanUpSyncedModState();
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<GetThirdPartyProfilesResult> GetThirdPartyProfiles(IEnumerable<string> userIds)
	{
		return await GetThirdPartyProfiles(userIds, null).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal bool HasLocalChanges(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.HasLocalChanges", LogLevel.L1_Debug, flowData2);
		return ModsCache.HasAnyDirty();
	}

	internal async Task<PlaysetSyncConflict[]> GetSyncConflicts(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetSyncConflicts", LogLevel.L1_Debug, flowData2);
		ListAllPlaysetsResult onlinePlaysets = await ModsBackendApiService.ListAllPlaysets(flowData2).ConfigureAwait(continueOnCapturedContext: false);
		return await PlaysetAndModSyncService.DetectSyncConflicts(onlinePlaysets).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetModVersionsResult> GetVersions(int modId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetVersions", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetVersions(modId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<ListOfFeaturedModsResult> GetFeaturedModsLists(int? page, int? limit, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetFeaturedModsLists", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetFeaturedModsLists(page, limit, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ModSearchResult> Search(SearchData searchData, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Search", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.Search(searchData, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> Report(int modId, ReportReason reason, string description, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Report", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.Report(modId, reason, description, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<RateResult> Rate(int modId, int rating, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Rate", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.Rate(modId, rating, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetUserRatingResult> GetUserRating(int modId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetUserRating", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetUserRating(modId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ModDependenciesResult> GetAllDependencies(List<int> mods, int? playsetId = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetDependencies", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetAllDependencies(mods, playsetId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ModDependenciesResult> GetDependencyCandidates(int modId, string searchQuery = null, int? amount = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetDependencyCandidates", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetDependencyCandidates(modId, searchQuery, amount, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<ModDependantsResult> GetModDependants(List<int> modIds, int playsetId = 0, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetModDependants", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetModDependants(modIds, playsetId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetDetailsResult> GetDetails(int modId, string version = null, ModPlatform? os = null, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetDetails", LogLevel.L1_Debug, flowData2);
		GetDetailsResult onlineDetails = await ModsBackendApiService.GetDetails(modId, version, os.HasValue ? os.Value.ToModsPlatformString() : null).ConfigureAwait(continueOnCapturedContext: false);
		if (onlineDetails.Success)
		{
			List<PlaysetInMod> collection = await GetSubscribedModLocalPlaysets(modId, version ?? onlineDetails.Mod.LatestVersion).ConfigureAwait(continueOnCapturedContext: false);
			onlineDetails.Mod.Playsets.AddRange(collection);
			return onlineDetails;
		}
		if (string.IsNullOrEmpty(version))
		{
			List<IMod> list = (await ModsCache.GetAllMods().ConfigureAwait(continueOnCapturedContext: false)).Where((IMod m) => m.Id == modId).ToList();
			if (list.Count == 0)
			{
				return new GetDetailsResult
				{
					Success = false,
					Error = new Error(NotFound.NoMod, $"Details for the mod {modId} could not be retrieved", "")
				};
			}
			version = list.OrderBy((IMod m) => m.Version).Last().Version;
		}
		return await GetLocalModDetails(modId.GetKey(version)).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetDetailsResult> GetDetails(string modName, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetDetails", LogLevel.L1_Debug, flowData2);
		return await GetLocalModDetails(modName).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetForumThreadResult> GetForumThread(int modId, string modVersion, int threadId, int page, int limit, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetForumThreadResult", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetForumThreadResult(modId, modVersion, threadId, page, limit).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<CreateForumPostResult> CreateForumPost(int modId, string modVersion, int threadId, string message, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.CreateForumPost", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.CreateForumPost(modId, modVersion, threadId, message, flowData);
	}

	private async Task<List<PlaysetInMod>> GetSubscribedModLocalPlaysets(int modId, string version)
	{
		List<CachedPlaysetMetaData> allModPlaysets = await ModsCache.GetSubscribedModPlaysets(modId, version).ConfigureAwait(continueOnCapturedContext: false);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return allModPlaysets.Select((CachedPlaysetMetaData p) => new PlaysetInMod
			{
				PlaysetId = p.GeneralDataTracked.TrackedObject.Id,
				Name = p.GeneralDataTracked.TrackedObject.Name,
				Version = version,
				ModIsEnabled = p.SubscribedModsTracked.First((PlaysetSubscribedModMetaData m) => m.Id == modId).IsEnabled,
				LoadOrder = p.SubscribedModsTracked.First((PlaysetSubscribedModMetaData m) => m.Id == modId).LoadOrder
			}).ToList();
		}
		return (from p in allModPlaysets
			where p.GetKey().Contains("local_")
			select new PlaysetInMod
			{
				PlaysetId = p.GeneralDataTracked.TrackedObject.Id,
				Name = p.GeneralDataTracked.TrackedObject.Name,
				Version = version,
				ModIsEnabled = p.SubscribedModsTracked.First((PlaysetSubscribedModMetaData m) => m.Id == modId).IsEnabled,
				LoadOrder = p.SubscribedModsTracked.First((PlaysetSubscribedModMetaData m) => m.Id == modId).LoadOrder
			}).ToList();
	}

	private async Task<GetDetailsResult> GetLocalModDetails(string modKey)
	{
		Mod mod = await ModsCache.GetModOrNull(modKey).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null)
		{
			return new GetDetailsResult
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "Details for the mod " + modKey + " could not be found", "")
			};
		}
		return new GetDetailsResult
		{
			Success = true,
			Mod = mod.ToModDetails()
		};
	}

	internal async Task<LoadOrderResult> SetLoadOrder(List<ModLoadOrder> modLoadOrderList, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.SetLoadOrder", LogLevel.L1_Debug, flowData2);
		if (modLoadOrderList.Any((ModLoadOrder x) => x.LoadOrder < 0))
		{
			return new LoadOrderResult
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, "The load order is invalid", "")
			};
		}
		Result result = await PerformSetLoadOrder(modLoadOrderList, playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<LoadOrderResult>();
		}
		return new LoadOrderResult
		{
			Success = true,
			OrderList = modLoadOrderList
		};
	}

	internal async Task<LoadOrderResult> SetLoadOrder(SearchOrder orderBy, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.SetLoadOrder", LogLevel.L1_Debug, flowData2);
		List<IMod> list = await ModsCache.GetModsInPlayset(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (list == null)
		{
			return new LoadOrderResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, "The playset id is invalid", "")
			};
		}
		List<ModLoadOrder> modLoadOrderList = SetLoadOrderFromPredefinedOrder(list, orderBy);
		Result result = await PerformSetLoadOrder(modLoadOrderList, playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<LoadOrderResult>();
		}
		return new LoadOrderResult
		{
			Success = true,
			OrderList = modLoadOrderList
		};
	}

	private async Task<Result> PerformSetLoadOrder(List<ModLoadOrder> modLoadOrderList, int playsetId)
	{
		CachedPlaysetMetaData cachedPlayset = await ModsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlayset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		await ModsCache.UpdateModsLoadOrderInPlayset(cachedPlayset, modLoadOrderList).ConfigureAwait(continueOnCapturedContext: false);
		List<ModLoadOrder> modActivationOrderList = modLoadOrderList.Where(delegate(ModLoadOrder m)
		{
			LocalData localData = m.Mod.LocalData;
			return localData != null && localData.LocalType == LocalType.Subscribed;
		}).ToList();
		Result setActivationOrderResult = await ModsBackendApiService.SetLoadOrder(modActivationOrderList, playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (setActivationOrderResult.Success)
		{
			cachedPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		}
		await ModsCache.UpdatePlayset(cachedPlayset.GetKey(), cachedPlayset).ConfigureAwait(continueOnCapturedContext: false);
		if (setActivationOrderResult.Success)
		{
			await ModsCache.ResetAllDirtyInPlayset(cachedPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new Result
		{
			Success = true
		};
	}

	internal List<ModLoadOrder> SetLoadOrderFromPredefinedOrder(List<IMod> mods, SearchOrder orderBy)
	{
		IEnumerable<IMod> source = ((orderBy != SearchOrder.Ascending) ? mods.OrderByDescending((IMod m) => m.DisplayName) : mods.OrderBy((IMod m) => m.DisplayName));
		int loadOrder = 0;
		return source.Select((IMod m) => new ModLoadOrder
		{
			Mod = m,
			LoadOrder = loadOrder++
		}).ToList();
	}

	internal async Task<LoadOrderResult> ResetLoadOrder(int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.ResetLoadOrder", LogLevel.L1_Debug, flowData2);
		LoadOrderResult loadOrderResult = await ModsBackendApiService.ResetLoadOrder(playsetId, flowData2).ConfigureAwait(continueOnCapturedContext: false);
		if (!loadOrderResult.Success)
		{
			return loadOrderResult;
		}
		bool resetResult = await ModsCache.ResetLoadOrderInPlayset(playsetId, loadOrderResult.OrderList).ConfigureAwait(continueOnCapturedContext: false);
		if (!resetResult)
		{
			return new LoadOrderResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The load order could not be set locally", "")
			};
		}
		List<ModLoadOrder> orderList = await GetCompleteLoadOrder(await ModsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		return new LoadOrderResult
		{
			Success = resetResult,
			OrderList = orderList
		};
	}

	internal async Task<List<ModLoadOrder>> GetCompleteLoadOrder(CachedPlaysetMetaData playset)
	{
		List<ModLoadOrder> modLoadOrderList = new List<ModLoadOrder>();
		foreach (PlaysetSubscribedModMetaData mod in playset.SubscribedModsTracked)
		{
			Mod mod2 = await ModsCache.GetModOrNull(mod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
			modLoadOrderList.Add(new ModLoadOrder
			{
				Mod = mod2,
				LoadOrder = mod.LoadOrder
			});
		}
		foreach (PlaysetWipModMetaData mod3 in playset.WipModsTracked)
		{
			Mod mod4 = await ModsCache.GetModOrNull(mod3.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
			modLoadOrderList.Add(new ModLoadOrder
			{
				Mod = mod4,
				LoadOrder = mod3.LoadOrder
			});
		}
		foreach (PlaysetUnmanagedModMetaData mod5 in playset.UnmanagedModsTracked)
		{
			Mod mod6 = await ModsCache.GetModOrNull(mod5.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
			modLoadOrderList.Add(new ModLoadOrder
			{
				Mod = mod6,
				LoadOrder = mod5.LoadOrder
			});
		}
		return modLoadOrderList.OrderBy((ModLoadOrder m) => m.LoadOrder).ToList();
	}

	internal async Task<CreatePlaysetResult> CreatePlayset(string playsetName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.CreatePlayset", LogLevel.L1_Debug, fd);
		if (!(await ModsCache.CreateLocalPlayset(playsetName, fd).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new CreatePlaysetResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The playset could not be created locally", "")
			};
		}
		await PlaysetAndModSyncService.AutomaticUpstreamSync(fd).ConfigureAwait(continueOnCapturedContext: false);
		int playsetId = (await ModsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).LastOrDefault((CachedPlaysetMetaData p) => p.GeneralDataTracked.TrackedObject.Name == playsetName).ToPlayset().PlaysetId;
		return new CreatePlaysetResult
		{
			Success = true,
			Name = playsetName,
			PlaysetId = playsetId
		};
	}

	internal async Task<CreatePlaysetResult> ClonePlayset(int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.ClonePlayset", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData playset = await ModsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new CreatePlaysetResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset {playsetId} does not exist", "")
			};
		}
		string playsetName = "Clone - " + playset.GeneralDataTracked.TrackedObject.Name;
		CreatePlaysetResult createResult = await CreatePlayset(playsetName, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!createResult.Success)
		{
			return createResult;
		}
		Result result = await CopyPlaysetModsList(playset, createResult.PlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<CreatePlaysetResult>();
		}
		return createResult;
	}

	internal async Task<Result> CopyPlaysetModsList(CachedPlaysetMetaData playsetToClone, int clonedPlaysetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.CopyPlaysetModsList", LogLevel.L0_Info, fd);
		CachedPlaysetMetaData clonedPlayset = await ModsCache.GetPlaysetOrNull(clonedPlaysetId).ConfigureAwait(continueOnCapturedContext: false);
		List<IMod> mods = await ModsCache.GetModsInPlayset(playsetToClone.GeneralDataTracked.TrackedObject.Id).ConfigureAwait(continueOnCapturedContext: false);
		SubscribeResult subscribeResult = await ModsSubscriptionService.Subscribe(mods, clonedPlaysetId, fd);
		if (!subscribeResult.Success)
		{
			return subscribeResult;
		}
		foreach (PlaysetSubscribedModMetaData subscribedMod in playsetToClone.SubscribedModsTracked)
		{
			clonedPlayset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.GetKey() == subscribedMod.GetKey());
			if (subscribedMod.IsEnabled)
			{
				await ModsSubscriptionService.Enable(subscribedMod.Id, clonedPlaysetId, enableOnBackend: true, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await ModsSubscriptionService.Disable(subscribedMod.Id, clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		foreach (PlaysetWipModMetaData wipMod in playsetToClone.WipModsTracked)
		{
			clonedPlayset.WipModsTracked.FirstOrDefault((PlaysetWipModMetaData m) => m.GetKey() == wipMod.GetKey());
			if (wipMod.IsEnabled)
			{
				await ModsSubscriptionService.Enable(wipMod.Id, clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await ModsSubscriptionService.Disable(wipMod.Id, clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		foreach (PlaysetUnmanagedModMetaData unmanagedMod in playsetToClone.UnmanagedModsTracked)
		{
			PlaysetUnmanagedModMetaData playsetUnmanagedModMetaData = clonedPlayset.UnmanagedModsTracked.FirstOrDefault((PlaysetUnmanagedModMetaData m) => m.GetKey() == unmanagedMod.GetKey());
			if (unmanagedMod.IsEnabled)
			{
				await ModsSubscriptionService.Enable(playsetUnmanagedModMetaData.Path, clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
			else
			{
				await ModsSubscriptionService.Disable(playsetUnmanagedModMetaData.Path, clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return await SetLoadOrder(await GetCompleteLoadOrder(playsetToClone).ConfigureAwait(continueOnCapturedContext: false), clonedPlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<RenamePlaysetResult> RenamePlayset(int playsetId, string playsetName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.RenamePlayset", LogLevel.L1_Debug, fd);
		if (!(await ModsCache.RenameLocalPlayset(playsetId, playsetName, fd).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new RenamePlaysetResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The playset could not be renamed locally", "")
			};
		}
		await PlaysetAndModSyncService.AutomaticUpstreamSync(fd).ConfigureAwait(continueOnCapturedContext: false);
		return new RenamePlaysetResult
		{
			Success = true,
			Name = playsetName,
			PlaysetId = playsetId
		};
	}

	internal async Task<Result> DeletePlayset(int playsetId, bool abortOnLocalFailure = true, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DeletePlayset", LogLevel.L1_Debug, fd);
		List<IMod> modsInPlayset = await ModsCache.GetModsInPlayset(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		ModsCacheError modsCacheError = await ModsCache.DeleteLocalPlayset(playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (abortOnLocalFailure && !modsCacheError.Success)
		{
			return modsCacheError;
		}
		await ModsBackendApiService.DeletePlayset(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		foreach (IMod modInPlayset in modsInPlayset)
		{
			if (!(await ModsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Any((CachedPlaysetMetaData p) => p.SubscribedModsTracked.GetAllWithoutTracking().Any((KeyValuePair<string, PlaysetSubscribedModMetaData> sm) => sm.Key == modInPlayset.GetKey())))
			{
				await ModsCache.CleanUpSubscribedMod(modInPlayset.Id, modInPlayset.Version).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return new Result
		{
			Success = true
		};
	}

	internal async Task<ListAllPlaysetsResult> ListAllPlaysets(bool includeOnline = false, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.ListAllPlaysets", LogLevel.L1_Debug, fd);
		List<Playset> onlinePlaysets = null;
		ListAllPlaysetsResult listAllPlaysetsResult = new ListAllPlaysetsResult
		{
			Success = true,
			AllPlaysets = new List<Playset>()
		};
		if (includeOnline)
		{
			ListAllPlaysetsResult listAllPlaysetsResult2 = await ModsBackendApiService.ListAllPlaysets(fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!listAllPlaysetsResult2.Success)
			{
				string text = "Unable to include online results";
				Log(text, LogLevel.L2_Warning, fd);
				listAllPlaysetsResult.Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.UnableToComplete, text, listAllPlaysetsResult2.Error.Raw);
			}
			else
			{
				onlinePlaysets = listAllPlaysetsResult2.AllPlaysets;
			}
		}
		List<Playset> localPlaysets = (await ModsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Select((CachedPlaysetMetaData p) => p.ToPlayset()).ToList();
		if (onlinePlaysets != null && onlinePlaysets.Count > 0)
		{
			listAllPlaysetsResult.AllPlaysets.AddRange(onlinePlaysets.Select(delegate(Playset op)
			{
				Playset playset = localPlaysets.FirstOrDefault((Playset p) => p.PlaysetId == op.PlaysetId);
				if (playset != null)
				{
					op.ModsSize = ((playset.ModsCount > op.ModsCount) ? playset.ModsSize : op.ModsSize);
					op.ModsCount = ((playset.ModsCount > op.ModsCount) ? playset.ModsCount : op.ModsCount);
					localPlaysets.Remove(playset);
				}
				return op;
			}));
		}
		listAllPlaysetsResult.AllPlaysets.AddRange(localPlaysets);
		return listAllPlaysetsResult;
	}

	internal async Task<ModListResult> List(string searchQuery = null, InstalledModsSortMethod sortMethod = InstalledModsSortMethod.RecentFirst, List<string> filterTags = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.List", LogLevel.L1_Debug, fd);
		List<Mod> mods = new List<Mod>();
		ListSubscribedResult onlineModsResult = await ModsBackendApiService.GetAllSubscribed().ConfigureAwait(continueOnCapturedContext: false);
		Log("Mods.List -> ModsBackendApiService.GetAllSubscribed() count: " + (onlineModsResult.Mods?.Count ?? 0), LogLevel.L0_Info, fd);
		List<Mod> localMods = await ModsCache.GetAllModsWithPlaysetData().ConfigureAwait(continueOnCapturedContext: false);
		if (onlineModsResult.Success && onlineModsResult.Mods.Count > 0)
		{
			mods.AddRange(onlineModsResult.Mods.Select(delegate(ModDetails om)
			{
				Mod mod = om.ToMod();
				Mod mod2 = localMods.FirstOrDefault((Mod m) => m.GetKey() == om.GetKey());
				if (mod2 != null)
				{
					mod.LocalData = mod2.LocalData;
					mod.LongDescription = mod2.LongDescription;
					if (mod2.Playsets.Count > mod.Playsets.Count)
					{
						IEnumerable<PlaysetInMod> collection = mod2.Playsets.Where((PlaysetInMod lp) => !mod.Playsets.Any((PlaysetInMod op) => op.PlaysetId == lp.PlaysetId));
						mod.Playsets.AddRange(collection);
					}
					localMods.Remove(mod2);
				}
				return mod;
			}));
		}
		mods.AddRange(localMods);
		if (!string.IsNullOrEmpty(searchQuery))
		{
			mods = mods.Where((Mod m) => CultureInfo.InvariantCulture.CompareInfo.IndexOf(m.DisplayName, searchQuery, CompareOptions.IgnoreCase) >= 0).ToList();
		}
		List<string> list = filterTags;
		if (list != null && list.Count > 0)
		{
			mods = mods.Where(delegate(Mod m)
			{
				List<ModTag> tags = m.Tags;
				return tags != null && tags.Select((ModTag t) => t.Id).Intersect(filterTags).Count() > 0;
			}).ToList();
		}
		mods = await SortInstalledModsList(mods, sortMethod).ConfigureAwait(continueOnCapturedContext: false);
		return new ModListResult
		{
			Success = true,
			Mods = mods
		};
	}

	internal async Task<List<Mod>> SortInstalledModsList(List<Mod> modsList, InstalledModsSortMethod sortMethod)
	{
		List<Mod> sortedMods = new List<Mod>();
		ChangeTrackedConcurrentDictionary<CachedPlaysetMetaData> allPlaysets = await ModsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false);
		switch (sortMethod)
		{
		case InstalledModsSortMethod.RecentFirst:
			sortedMods = modsList.OrderByDescending((Mod m) => m.InstalledDate).ToList();
			break;
		case InstalledModsSortMethod.OldestFirst:
			sortedMods = modsList.OrderBy((Mod m) => m.InstalledDate).ToList();
			break;
		case InstalledModsSortMethod.AscendingName:
			sortedMods = modsList.OrderBy((Mod m) => m.DisplayName).ToList();
			break;
		case InstalledModsSortMethod.DescendingName:
			sortedMods = modsList.OrderByDescending((Mod m) => m.DisplayName).ToList();
			break;
		case InstalledModsSortMethod.MostPlaysets:
			sortedMods = modsList.OrderByDescending((Mod mod) => allPlaysets.Count((CachedPlaysetMetaData p) => ModsCache.IsModInPlayset(p, mod.GetKey()))).ToList();
			break;
		case InstalledModsSortMethod.LeastPlaysets:
			sortedMods = modsList.OrderBy((Mod mod) => allPlaysets.Count((CachedPlaysetMetaData p) => ModsCache.IsModInPlayset(p, mod.GetKey()))).ToList();
			break;
		}
		return sortedMods;
	}

	internal async Task<SubscribeResult> Subscribe(int modId, int? playsetId = null, string version = null, FlowData flowData = null)
	{
		return await SubscribeBulk(new List<IMod>
		{
			new Mod
			{
				Id = modId,
				Version = version,
				LocalData = new LocalData
				{
					LocalType = LocalType.Subscribed
				}
			}
		}, playsetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<SubscribeResult> Subscribe(string modName, int? playsetId = null, FlowData flowData = null)
	{
		return await SubscribeBulk(new List<IMod>
		{
			new Mod
			{
				Name = modName
			}
		}, playsetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<SubscribeResult> SubscribeBulk(IEnumerable<IMod> mods, int? playsetId = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SubscribeBulk", LogLevel.L1_Debug, fd);
		if (playsetId.HasValue)
		{
			return await ModsSubscriptionService.Subscribe(mods, playsetId.Value, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		if ((await ModsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Count() > 0)
		{
			return new SubscribeResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "Specify a playset id to add the mod to", "")
			};
		}
		CreatePlaysetResult createPlaysetResult = await CreatePlayset("Initial playset", fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!createPlaysetResult.Success)
		{
			return createPlaysetResult.CastTo<SubscribeResult>();
		}
		return await ModsSubscriptionService.Subscribe(mods, createPlaysetResult.PlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> ActivatePlayset(int playsetId, FlowData flowData)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.ActivatePlayset", LogLevel.L1_Debug, flowData2);
		if (!(await ModsCache.SetActivePlayset(playsetId).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The active playset {playsetId} does not exist locally", string.Empty)
			};
		}
		(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
		{
			EventId = Guid.NewGuid(),
			IsActivePlaysetUpdated = true
		});
		return new Result
		{
			Success = true
		};
	}

	internal async Task<Result> DeactivateActivePlayset(FlowData flowData)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.DeactivateActivePlayset", LogLevel.L1_Debug, flowData2);
		bool success = await ModsCache.SetActivePlayset(null).ConfigureAwait(continueOnCapturedContext: false);
		(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
		{
			EventId = Guid.NewGuid(),
			IsActivePlaysetUpdated = true
		});
		return new Result
		{
			Success = success
		};
	}

	internal async Task<GetActivePlaysetResult> GetActivePlayset(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetActivePlayset", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData cachedPlaysetMetaData = await ModsCache.GetActivePlayset().ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlaysetMetaData == null)
		{
			return new GetActivePlaysetResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "There is no active playset", string.Empty)
			};
		}
		return new GetActivePlaysetResult
		{
			Success = true,
			PlaysetId = cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Id
		};
	}

	internal async Task<ModListResult> GetActivePlaysetEnabledMods(FlowData flowData)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetActivePlaysetEnabledMods", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData activePlayset = await ModsCache.GetActivePlayset().ConfigureAwait(continueOnCapturedContext: false);
		if (activePlayset == null)
		{
			return new ModListResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "There is no active playset", string.Empty)
			};
		}
		List<string> enabledModsIds = new List<string>();
		enabledModsIds.AddRange(from m in activePlayset.SubscribedModsTracked
			where m.IsEnabled
			select m.GetKey());
		enabledModsIds.AddRange(from m in activePlayset.WipModsTracked
			where m.IsEnabled
			select m.GetKey());
		enabledModsIds.AddRange(from m in activePlayset.UnmanagedModsTracked
			where m.IsEnabled
			select m.GetKey());
		IEnumerable<IMod> enabledMods = (await ModsCache.GetModsInPlayset(activePlayset.GeneralDataTracked.TrackedObject.Id)).Where((IMod m) => enabledModsIds.Contains(m.GetKey()));
		List<ModLoadOrder> loadOrder = await GetCompleteLoadOrder(activePlayset).ConfigureAwait(continueOnCapturedContext: false);
		List<Mod> mods = (from m in enabledMods
			orderby loadOrder.Find((ModLoadOrder order) => order.Mod != null && order.Mod.GetKey() == m.GetKey())?.LoadOrder ?? int.MaxValue
			select m as Mod).ToList();
		return new ModListResult
		{
			Success = true,
			Mods = mods
		};
	}

	internal async Task<ReportModCreatorResult> ReportModCreator(string userName, ReportReason reportReason, string description, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.ReportModCreator", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.ReportModCreator(userName, reportReason, description, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetFollowedModCreatorsResult> GetFollowedModCreators(int? limit = 20, int? page = 1, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetFollowedModCreators", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetFollowedModCreators(limit, page, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<GetModCreatorsProfileResult> GetModCreatorProfile(string userName, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetModCreatorProfile", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.GetModCreatorProfile(userName, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> FollowModCreator(string userName, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.FollowModCreator", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.FollowModCreator(userName, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> UnfollowModCreator(string userName, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.UnfollowModCreator", LogLevel.L1_Debug, flowData2);
		return await ModsBackendApiService.UnfollowModCreator(userName, flowData2).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PublishResult> PublishWIP(PublishWipData publishWipData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishWIP", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishResult>("publish a WIP mod");
		}
		IMod mod = await ModsCache.GetWipMod(publishWipData.wipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData == null || string.IsNullOrEmpty(mod.LocalData.FolderAbsolutePath))
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The wip Guid " + publishWipData.wipGuid + " does not exist in the cache", "")
			};
		}
		GetProfileResult getProfileResult = await _context.Profile.Get().ConfigureAwait(continueOnCapturedContext: false);
		if (!getProfileResult.Success)
		{
			return getProfileResult.CastTo<PublishResult>();
		}
		if (string.IsNullOrEmpty(getProfileResult.Social.DisplayName))
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(NotFound.NoProfile, "No social profile exists for the account", "")
			};
		}
		GetGameDataResult getGameDataResult = await GetGameData().ConfigureAwait(continueOnCapturedContext: false);
		if (!getGameDataResult.Success)
		{
			return new PublishResult
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "Could not retrieve game's enabled actions", string.Empty)
			};
		}
		if (getGameDataResult.Actions.LooseModFilesSupport)
		{
			return await ModPublishService.PublishV2WIP(publishWipData, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await ModPublishService.PublishWIP(publishWipData, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PublishUpdateResult> PublishNewModVersion(PublishUpdateData publishNewModVersionData, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PublishNewVersion", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishUpdateResult>("create a new mod version");
		}
		IMod mod = await ModsCache.GetWipMod(publishNewModVersionData.WipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData == null || string.IsNullOrEmpty(mod.LocalData.FolderAbsolutePath))
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The wip Guid " + publishNewModVersionData.WipGuid + " does not exist", "")
			};
		}
		if (mod.Id <= 0)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The wip mod " + publishNewModVersionData.WipGuid + " has never been published", "")
			};
		}
		GetGameDataResult getGameDataResult = await GetGameData().ConfigureAwait(continueOnCapturedContext: false);
		if (!getGameDataResult.Success)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "Could not retrieve game's enabled actions", string.Empty)
			};
		}
		if (getGameDataResult.Actions.LooseModFilesSupport)
		{
			return await ModPublishService.PublishNewModVersionV2(publishNewModVersionData, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await ModPublishService.PublishUpdateV1(publishNewModVersionData, isNewVersion: true, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<PublishUpdateResult> UpdatePublishedModVersion(PublishUpdateData publishUpdateData, string version = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UpdatePublishedModVersion", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn<PublishUpdateResult>("update a mod version");
		}
		IMod mod = await ModsCache.GetWipMod(publishUpdateData.WipGuid).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData == null)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The wip Guid " + publishUpdateData.WipGuid + " does not exist", "")
			};
		}
		if (mod.Id <= 0)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The wip mod " + publishUpdateData.WipGuid + " has never been published", "")
			};
		}
		GetDetailsResult getDetails = await ModsBackendApiService.GetDetails(mod.Id, version, publishUpdateData.Os.ToModsPlatformString(), fd);
		if (!string.IsNullOrEmpty(version) && version.ParseIntOrDefault() > getDetails.Mod.LatestVersion.ParseIntOrDefault())
		{
			PublishUpdateResult publishUpdateResult = new PublishUpdateResult();
			publishUpdateResult.Success = false;
			publishUpdateResult.Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The version " + version + " of wip mod " + publishUpdateData.WipGuid + " does not exist", "");
			return publishUpdateResult;
		}
		GetGameDataResult getGameDataResult = await GetGameData().ConfigureAwait(continueOnCapturedContext: false);
		if (!getGameDataResult.Success)
		{
			return new PublishUpdateResult
			{
				Success = false,
				Error = new Error(InternalError.InternalError, "Could not retrieve game's enabled actions", string.Empty)
			};
		}
		if (getGameDataResult.Actions.LooseModFilesSupport)
		{
			return await ModPublishService.UpdatePublishedModVersionV2(publishUpdateData, version ?? getDetails.Mod.LatestVersion, fd).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await ModPublishService.PublishUpdateV1(publishUpdateData, isNewVersion: false, fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<RegisterResult> SyncWIPMod(int modId, string version = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SyncWipMod", LogLevel.L1_Debug, fd);
		ModPlatform modsPlatform = _context.Platform.GetModsPlatform();
		GetDetailsResult getModDetails = await ModsBackendApiService.GetDetails(modId, version, modsPlatform.ToModsPlatformString(), fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!getModDetails.Success)
		{
			return getModDetails.CastTo<RegisterResult>();
		}
		Mod mod = getModDetails.Mod.ToMod();
		IMod cachedWipMod = await ModsCache.GetWipMod(mod.Name).ConfigureAwait(continueOnCapturedContext: false);
		_context.HTTP.RemoveOngoingTransfer(ETransferType.Download, ETransferServiceType.Mods, modId.ToString());
		if (cachedWipMod == null)
		{
			DownloadResult downloadResult = await ModsDownloader.DownloadToWip(mod.Name, getModDetails.Mod.ToModDownloadMetadata(), doNotSetTransferStateToSuccessful: true, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!downloadResult.Success)
			{
				return downloadResult.CastTo<RegisterResult>();
			}
			mod.LocalData = new LocalData
			{
				LocalType = LocalType.WorkInProgress,
				FolderAbsolutePath = _pathFactory.GetModsPath(mod.Name, LocalType.WorkInProgress),
				ContentFileOrFolder = downloadResult.ContentFileOrFolder,
				ThumbnailFilename = downloadResult.ThumbnailFilename,
				ScreenshotsFilenames = downloadResult.ScreenshotsFilenames
			};
			mod.LatestUpdate = DateTime.Now;
			ModsCacheError modsCacheError = await ModsCache.AddMod(mod).ConfigureAwait(continueOnCapturedContext: false);
			if (!modsCacheError.Success)
			{
				return new RegisterResult
				{
					Success = false,
					Error = modsCacheError.Error
				};
			}
		}
		else
		{
			DownloadResult downloadResult2 = await OverwriteWipModFolder(mod.Name, getModDetails.Mod.ToModDownloadMetadata(), cachedWipMod.LocalData.FolderAbsolutePath, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!downloadResult2.Success)
			{
				return downloadResult2.CastTo<RegisterResult>();
			}
			mod.LocalData = new LocalData
			{
				LocalType = LocalType.WorkInProgress,
				FolderAbsolutePath = cachedWipMod.LocalData.FolderAbsolutePath,
				ContentFileOrFolder = downloadResult2.ContentFileOrFolder,
				ThumbnailFilename = downloadResult2.ThumbnailFilename,
				ScreenshotsFilenames = downloadResult2.ScreenshotsFilenames
			};
			mod.LatestUpdate = DateTime.Now;
			ModsCacheError modsCacheError2 = await ModsCache.UpdateMod(mod).ConfigureAwait(continueOnCapturedContext: false);
			if (!modsCacheError2.Success)
			{
				return new RegisterResult
				{
					Success = false,
					Error = modsCacheError2.Error
				};
			}
		}
		return new RegisterResult
		{
			Success = true,
			Guid = mod.Name,
			Path = mod.LocalData.FolderAbsolutePath
		};
	}

	private async Task<DownloadResult> OverwriteWipModFolder(string modGuid, ModDownloadMetadata modDownloadMetadata, string pathToOverwrite, FlowData flowData = null)
	{
		string tempModFolder = "Temp_" + modGuid;
		DownloadResult downloadResult = await ModsDownloader.DownloadToWip(tempModFolder, modDownloadMetadata, doNotSetTransferStateToSuccessful: true, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadResult.Success)
		{
			return downloadResult;
		}
		FileIOStatus fileIOStatus = await _context.FileIO.DeleteDirectory(pathToOverwrite, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			Log("The previous version of the WIP mod could not be deleted", LogLevel.L3_Error, flowData);
			return fileIOStatus.ToResult<DownloadResult>();
		}
		string modsPath = _pathFactory.GetModsPath(tempModFolder, LocalType.WorkInProgress);
		FileIOStatus fileIOStatus2 = await _context.FileIO.Move(modsPath, pathToOverwrite).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus2.Success)
		{
			Log("The WIP mod was successfully downloaded, but there are issues renaming the folder name", LogLevel.L3_Error, flowData);
			return fileIOStatus2.ToResult<DownloadResult>();
		}
		return downloadResult;
	}

	private async Task<GetThirdPartyProfilesResult> GetThirdPartyProfiles(IEnumerable<string> userIds, FlowData flowData = null)
	{
		return await (_context.ThirdParty as ThirdPartyService).GetGamerDisplayNames(userIds.Select((string id) => ulong.Parse(id)).ToList(), flowData);
	}
}
