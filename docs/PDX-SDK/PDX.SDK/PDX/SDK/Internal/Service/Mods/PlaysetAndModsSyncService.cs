using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Internal.Errors;
using PDX.SDK.Internal.Events;
using PDX.SDK.Internal.Events.Internal.Mods;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Util;
using PDX.SDK.Util;
using TinyJson;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class PlaysetAndModsSyncService : InternalService, IPlaysetAndModsSyncService
{
	private readonly IPathFactory _pathFactory;

	private readonly IModsDownloaderService _modsDownloader;

	private readonly IModsSubscriptionService _modSubscriptionService;

	private readonly IModsCache _modsCache;

	private readonly IModsBackendApiService _modsBackendApiService;

	private readonly AsyncReaderWriterLock _readWriteLock = new AsyncReaderWriterLock();

	[Preserve]
	public PlaysetAndModsSyncService(IContext context, IPathFactory pathFactory, IModsDownloaderService modsDownloader, IModsSubscriptionService modSubscriptionService, IModsCache modsCache, IModsBackendApiService modsBackendApiService)
		: base("PlaysetAndModsSyncService", context as Context)
	{
		_pathFactory = pathFactory;
		_modsDownloader = modsDownloader;
		_modSubscriptionService = modSubscriptionService;
		_modsCache = modsCache;
		_modsBackendApiService = modsBackendApiService;
	}

	public async Task<Result> Sync(SyncDirection syncDirection = SyncDirection.Default, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Sync", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("sync mods");
		}
		if (syncDirection == SyncDirection.Default && _context.Mods.HasLocalChanges())
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.PromptNeeded, "There has been local changes while the user was offline, prompt the user to decide which state to keep for conflicting playsets", "")
			};
		}
		Result syncResult = ((syncDirection != SyncDirection.Upstream) ? (await DownstreamSync(fd).ConfigureAwait(continueOnCapturedContext: false)) : (await UpstreamSync(fd).ConfigureAwait(continueOnCapturedContext: false)));
		Result modsSyncResult = await SyncModsData(syncDirection, fd).ConfigureAwait(continueOnCapturedContext: false);
		FileIOStatus cleanResult = await CleanupSubscribed.CleanupSubscribedModsFolder(_context, _pathFactory.GetModsPath(), fd).ConfigureAwait(continueOnCapturedContext: false);
		LoadUnmanagedModsResult loadUnmanagedModsResult = await _modsCache.RefreshUnmanagedModsCache().ConfigureAwait(continueOnCapturedContext: false);
		if (!modsSyncResult.Success)
		{
			return modsSyncResult;
		}
		if (!cleanResult.Success)
		{
			Enum arg = cleanResult.ErrorCode.ToInternalError();
			Log($"Cleaning up the Subscribed mods folder failed with error: {arg}", LogLevel.L2_Warning, fd);
		}
		if (!loadUnmanagedModsResult.Success)
		{
			Log("Loading unmanaged mods failed", LogLevel.L2_Warning, fd);
		}
		return syncResult;
	}

	public async Task<Result> SyncPlayset(int playsetId, SyncDirection syncDirection, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SyncPlayset", LogLevel.L1_Debug, fd);
		if (!(await _context.GetLoggedIn().ConfigureAwait(continueOnCapturedContext: false)))
		{
			return NotLoggedIn("sync mods");
		}
		Result result;
		switch (syncDirection)
		{
		case SyncDirection.Downstream:
		{
			ListAllPlaysetsResult listAllPlaysetsResult = await _modsBackendApiService.ListAllPlaysets(fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!listAllPlaysetsResult.Success)
			{
				return listAllPlaysetsResult;
			}
			Playset playset = listAllPlaysetsResult.AllPlaysets.Find((Playset p) => p.PlaysetId == playsetId);
			if (playset == null)
			{
				return new Result
				{
					Success = false,
					Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.UnableToComplete, $"An error occured while syncing the remote state of playset {playsetId} to the local state", "")
				};
			}
			result = await DownstreamSyncPlayset(playset, fd).ConfigureAwait(continueOnCapturedContext: false);
			break;
		}
		case SyncDirection.Upstream:
		{
			CachedPlaysetMetaData cachedPlaysetMetaData = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
			if (cachedPlaysetMetaData == null)
			{
				return new Result
				{
					Success = false,
					Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"An error occured while syncing the local state of playset {playsetId} to the backend", "")
				};
			}
			result = await UpstreamSyncPlayset(cachedPlaysetMetaData, fd).ConfigureAwait(continueOnCapturedContext: false);
			break;
		}
		default:
			result = new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.PromptNeeded, $"Prompt the user to decide which state to keep for the playset {playsetId}", "")
			};
			break;
		}
		return result;
	}

	internal async Task<Result> DownstreamSync(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DownstreamSync", LogLevel.L0_Info, fd);
		AggregateResult syncSuccess = new AggregateResult();
		ListAllPlaysetsResult remotePlaysets = await _modsBackendApiService.ListAllPlaysets(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!remotePlaysets.Success)
		{
			return remotePlaysets;
		}
		foreach (Playset allPlayset in remotePlaysets.AllPlaysets)
		{
			syncSuccess.Add(await DownstreamSyncPlayset(allPlayset, fd).ConfigureAwait(continueOnCapturedContext: false));
		}
		IEnumerable<CachedPlaysetMetaData> enumerable = (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Where((CachedPlaysetMetaData p) => !p.GetKey().Contains("local_") && !remotePlaysets.AllPlaysets.Exists((Playset rp) => rp.PlaysetId.ToString() == p.GetKey()));
		foreach (CachedPlaysetMetaData item in enumerable)
		{
			syncSuccess.Add(await _modsCache.DeleteLocalPlayset(item.GeneralDataTracked.TrackedObject.Id, fd));
		}
		return new Result
		{
			Success = syncSuccess.Success,
			Error = (syncSuccess.Success ? null : new Error(PDX.SDK.Contracts.Enums.Errors.Mods.UnableToComplete, "Failed to complete syncing the remote state to the local state, please retry at a later time", ""))
		};
	}

	internal async Task<Result> DownstreamSyncPlayset(Playset remotePlayset, FlowData flowData = null)
	{
		CachedPlaysetMetaData cachedPlaysetMetaData = await _modsCache.GetPlaysetOrNull(remotePlayset.PlaysetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlaysetMetaData == null)
		{
			return await SyncNewRemotePlayset(remotePlayset, flowData).ConfigureAwait(continueOnCapturedContext: false);
		}
		return await SyncExistingRemotePlayset(remotePlayset, cachedPlaysetMetaData, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> SyncNewRemotePlayset(Playset remotePlayset, FlowData flowData = null)
	{
		CachedPlaysetMetaData newPlayset = await CachedPlaysetMetaData.Create(remotePlayset.ToPlaysetGeneralMetadata(), _context.PathFactory, _context.FileIO, _context.Logger).ConfigureAwait(continueOnCapturedContext: false);
		List<PlaysetSubscribedMod> remotePlaysetMods = await PerformListAllModsInPlayset(remotePlayset.PlaysetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
		foreach (PlaysetSubscribedMod remotePlaysetMod in remotePlaysetMods)
		{
			if (!(await newPlayset.SubscribedModsTracked.TryAdd(remotePlaysetMod.GetKey(), remotePlaysetMod.ToPlaysetSubscribedModMetaData()).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return new Result
				{
					Success = false,
					Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "Could not sync the playset to local state", "")
				};
			}
			newPlayset.GeneralDataTracked.TrackedObject.ModsSize += remotePlaysetMod.Size;
		}
		newPlayset.GeneralDataTracked.TrackedObject.ModsCount = remotePlaysetMods.Count();
		newPlayset.GeneralDataTracked.TrackedObject.PlaysetModsLatestUpdated = remotePlayset.LatestModUpdate;
		newPlayset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		newPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		if (!(await _modsCache.AddPlayset(newPlayset.GetKey(), newPlayset).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "Could not sync the playset to local state", "")
			};
		}
		await _modsCache.ResetAllDirtyInPlayset(newPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		return Result.ResultSuccessful();
	}

	internal async Task<Result> SyncExistingRemotePlayset(Playset remotePlayset, CachedPlaysetMetaData cachedPlayset, FlowData flowData = null)
	{
		bool isActivePlayset = cachedPlayset == await _modsCache.GetActivePlayset();
		bool activePlaysetUpdated = false;
		cachedPlayset.GeneralDataTracked.TrackedObject.Name = remotePlayset.Name;
		cachedPlayset.GeneralDataTracked.TrackedObject.DisplayImagePath = remotePlayset.DisplayImagePath;
		List<PlaysetSubscribedMod> remotePlaysetMods = await PerformListAllModsInPlayset(remotePlayset.PlaysetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
		foreach (PlaysetSubscribedMod remoteMod in remotePlaysetMods)
		{
			if (cachedPlayset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.GetKey() == remoteMod.GetKey()) == null)
			{
				if (!(await cachedPlayset.SubscribedModsTracked.TryAdd(remoteMod.GetKey(), remoteMod.ToPlaysetSubscribedModMetaData()).ConfigureAwait(continueOnCapturedContext: false)))
				{
					return new Result
					{
						Success = false,
						Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "Could not sync the playset to local state", "")
					};
				}
				if (isActivePlayset && remoteMod.ToPlaysetSubscribedModMetaData().IsEnabled)
				{
					activePlaysetUpdated = true;
				}
				cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromAddedMod(remoteMod.Size);
			}
			else if (!cachedPlayset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.GetKey() == remoteMod.GetKey()).Equals(remoteMod.ToPlaysetSubscribedModMetaData()))
			{
				if (!(await cachedPlayset.SubscribedModsTracked.TryUpdate(remoteMod.GetKey(), remoteMod.ToPlaysetSubscribedModMetaData()).ConfigureAwait(continueOnCapturedContext: false)))
				{
					Log($"The mod {remoteMod.Id} could not be updated", LogLevel.L2_Warning);
				}
				if (isActivePlayset)
				{
					activePlaysetUpdated = true;
				}
			}
		}
		IEnumerable<PlaysetSubscribedModMetaData> enumerable = cachedPlayset.SubscribedModsTracked.Where((PlaysetSubscribedModMetaData cm) => !remotePlaysetMods.Exists((PlaysetSubscribedMod rm) => rm.GetKey() == cm.GetKey()));
		foreach (PlaysetSubscribedModMetaData cachedMod in enumerable)
		{
			Mod removedMod = await _modsCache.GetModOrNull(cachedMod.GetKey());
			if (!(await cachedPlayset.SubscribedModsTracked.TryRemove(cachedMod.GetKey()).ConfigureAwait(continueOnCapturedContext: false)))
			{
				return new Result
				{
					Success = false,
					Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "Could not sync the playset to local state", "")
				};
			}
			if (isActivePlayset && cachedMod.IsEnabled)
			{
				activePlaysetUpdated = true;
			}
			if (removedMod != null)
			{
				cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromRemovedMod(removedMod.Size);
			}
		}
		cachedPlayset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		cachedPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		cachedPlayset.GeneralDataTracked.TrackedObject.PlaysetModsLatestUpdated = remotePlayset.LatestModUpdate;
		if (!(await _modsCache.UpdatePlayset(cachedPlayset.GetKey(), cachedPlayset).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "Could not sync the playset to local state", "")
			};
		}
		await _modsCache.ResetAllDirtyInPlayset(cachedPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		if (isActivePlayset && activePlaysetUpdated)
		{
			(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				IsActivePlaysetUpdated = true
			});
		}
		return Result.ResultSuccessful();
	}

	internal async Task<Result> SyncModsData(SyncDirection syncDirection, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SyncModsMetadata", LogLevel.L0_Info, fd);
		ListSubscribedResult remoteMods = await _modsBackendApiService.GetAllSubscribed(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!remoteMods.Success)
		{
			return remoteMods;
		}
		AggregateResult syncResult = new AggregateResult();
		IEnumerable<IMod> localMods = (await _modsCache.GetAllMods().ConfigureAwait(continueOnCapturedContext: false)).Where((IMod m) => m.LocalData != null && m.LocalData.LocalType == LocalType.Subscribed);
		return await DownloadMissingMods(remoteMods, await SyncModsPresentOnDisk(syncDirection, remoteMods, localMods, syncResult, fd).ConfigureAwait(continueOnCapturedContext: false), fd).ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task<AggregateResult> SyncModsPresentOnDisk(SyncDirection syncDirection, ListSubscribedResult remoteMods, IEnumerable<IMod> localMods, AggregateResult syncResult, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SyncModsPresentOnDisk", LogLevel.L1_Debug, fd);
		foreach (IMod cachedMod in localMods)
		{
			ModDetails remoteMod;
			if ((syncDirection == SyncDirection.Downstream || syncDirection == SyncDirection.Default) && _context.Config.Mods.UsePatching.HasValue && _context.Config.Mods.UsePatching.Value)
			{
				ModDetails modDetails = remoteMods.Mods.Find((ModDetails x) => x.Id == cachedMod.Id && x.Version == cachedMod.Version);
				if (modDetails != null)
				{
					remoteMods.Mods.Remove(modDetails);
					continue;
				}
				remoteMod = remoteMods.Mods.Find((ModDetails x) => x.Id == cachedMod.Id);
				if (remoteMod != null && remoteMod.Version != cachedMod.Version)
				{
					Result result = await _modSubscriptionService.PatchMod(cachedMod, remoteMod, 0, fd).ConfigureAwait(continueOnCapturedContext: false);
					if (result.Success)
					{
						syncResult.Add(result);
						remoteMods.Mods.Remove(remoteMod);
					}
					else
					{
						Log("Patching unsuccessful, error " + result.Error.Category, LogLevel.L2_Warning, fd);
					}
				}
			}
			remoteMod = remoteMods.Mods.Find((ModDetails x) => x.Id.GetModKey(x.Version) == cachedMod.GetKey());
			if (remoteMod == null)
			{
				await _modsCache.CleanUpSubscribedMod(cachedMod.Id, cachedMod.Version).ConfigureAwait(continueOnCapturedContext: false);
				continue;
			}
			if (remoteMod.State != ModState.Published && remoteMod.State != ModState.Publishing && _context.Platform.IsValidModsConsolePlatform())
			{
				await _modsCache.CleanUpSubscribedMod(cachedMod.Id, cachedMod.Version).ConfigureAwait(continueOnCapturedContext: false);
				remoteMods.Mods.Remove(remoteMod);
				continue;
			}
			if (remoteMod.LatestUpdate.HasValue && (!cachedMod.LatestUpdate.HasValue || cachedMod.LatestUpdate.Value < remoteMod.LatestUpdate.Value))
			{
				syncResult.Add(await DownstreamSyncMod(cachedMod, fd).ConfigureAwait(continueOnCapturedContext: false));
			}
			remoteMods.Mods.Remove(remoteMod);
		}
		return syncResult;
	}

	private async Task<AggregateResult> DownloadMissingMods(ListSubscribedResult remoteMods, AggregateResult syncResult, FlowData flowData)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DownloadMissingMods", LogLevel.L1_Debug, fd);
		foreach (ModDetails remoteMod in remoteMods.Mods)
		{
			if (remoteMod.State == ModState.Published)
			{
				GetDetailsResult getDetails = await _modsBackendApiService.GetDetails(remoteMod.Id, remoteMod.Version, _context.Platform.GetModsPlatform().ToModsPlatformString(), fd).ConfigureAwait(continueOnCapturedContext: false);
				if (!getDetails.Success)
				{
					syncResult.Add(getDetails);
					break;
				}
				DownloadResult downloadResult = await _modsDownloader.Download(getDetails.Mod.ToModDownloadMetadata(), doNotSetTransferStateToSuccessful: true, useFinalDirAsTemp: false, fd).ConfigureAwait(continueOnCapturedContext: false);
				if (downloadResult.Success)
				{
					await _modsCache.CacheNewSubscribedMod(getDetails.Mod.ToMod(), downloadResult).ConfigureAwait(continueOnCapturedContext: false);
				}
				else
				{
					(_context.Events as InternalEventHub).Publish(new ModDownloadFailed
					{
						EventId = Guid.NewGuid(),
						ModId = remoteMod.Id
					});
				}
				syncResult.Add(downloadResult);
			}
		}
		return syncResult;
	}

	internal async Task<Result> DownstreamSyncMod(IMod cachedMod, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.DownstreamSyncMod", LogLevel.L1_Debug, flowData2);
		GetDetailsResult getDetailsResult = await _modsBackendApiService.GetDetails(cachedMod.Id, cachedMod.Version, _context.Platform.GetModsPlatform().ToModsPlatformString(), flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!getDetailsResult.Success)
		{
			return getDetailsResult;
		}
		cachedMod.DisplayName = getDetailsResult.Mod.DisplayName;
		cachedMod.ShortDescription = getDetailsResult.Mod.ShortDescription;
		cachedMod.LongDescription = getDetailsResult.Mod.LongDescription;
		cachedMod.Size = getDetailsResult.Mod.Size;
		cachedMod.Tags = getDetailsResult.Mod.Tags;
		cachedMod.LatestUpdate = DateTime.Now;
		Result result = await DownloadModToTempFolder(getDetailsResult.Mod.ToModDownloadMetadata(), cachedMod, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result;
		}
		FileIOStatus fileIOStatus = await MoveTempFilesToFinalFolder(cachedMod.Id, cachedMod.Version).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return fileIOStatus.ToResult();
		}
		FileIOStatus fileIOStatus2 = await DeleteOldFiles(cachedMod.LocalData).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus2.Success)
		{
			Log(fileIOStatus2.Message, LogLevel.L2_Warning);
		}
		ModsCacheError updated = await _modsCache.UpdateMod(cachedMod).ConfigureAwait(continueOnCapturedContext: false);
		if (updated.Success)
		{
			await _modsCache.ResetDirtyMod(cachedMod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return updated;
	}

	internal async Task<Result> DownloadModToTempFolder(ModDownloadMetadata modDownloadMetadata, IMod cachedMod, FlowData flowData = null)
	{
		string modsPath = _pathFactory.GetModsPath(ModPath.GetModFolderName(modDownloadMetadata.Id, modDownloadMetadata.Version, tempFolder: true, _context.Logger));
		DownloadResult downloadResult = await _modsDownloader.DownloadAllImages(modDownloadMetadata, modsPath, null, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (downloadResult.Success)
		{
			cachedMod.LocalData.ThumbnailFilename = downloadResult.ThumbnailFilename;
			cachedMod.LocalData.ScreenshotsFilenames = downloadResult.ScreenshotsFilenames;
		}
		return downloadResult;
	}

	internal async Task<FileIOStatus> MoveTempFilesToFinalFolder(int modId, string version)
	{
		string path = Path.Combine(_pathFactory.GetModsPath(ModPath.GetModFolderName(modId, version, tempFolder: true, _context.Logger)), ".metadata");
		OperationResult<List<string>> listFiles = await _context.FileIO.ListFiles(path).ConfigureAwait(continueOnCapturedContext: false);
		if (listFiles.Status.Success)
		{
			string modFolderName = ModPath.GetModFolderName(modId, version, tempFolder: false, _context.Logger);
			string finalModBaseDir = _pathFactory.GetModsPath(modFolderName);
			if (!_context.FileIO.PathExists(finalModBaseDir))
			{
				FileIOStatus fileIOStatus = await _context.FileIO.CreateDirectory(finalModBaseDir).ConfigureAwait(continueOnCapturedContext: false);
				if (!fileIOStatus.Success)
				{
					return fileIOStatus;
				}
				await _context.FileIO.CreateDirectory(Path.Combine(finalModBaseDir, ".metadata")).ConfigureAwait(continueOnCapturedContext: false);
			}
			foreach (string filePath in listFiles.Result)
			{
				string finalMetadataFilePath = _pathFactory.GetModsPath(Path.Combine(modFolderName, ".metadata", Path.GetFileName(filePath)));
				if (_context.FileIO.PathExists(finalMetadataFilePath))
				{
					FileIOStatus fileIOStatus2 = await _context.FileIO.Delete(finalMetadataFilePath).ConfigureAwait(continueOnCapturedContext: false);
					if (!fileIOStatus2.Success)
					{
						return fileIOStatus2;
					}
				}
				FileIOStatus fileIOStatus3 = await _context.FileIO.Move(filePath, finalMetadataFilePath).ConfigureAwait(continueOnCapturedContext: false);
				if (!fileIOStatus3.Success)
				{
					return fileIOStatus3;
				}
			}
		}
		return listFiles.Status;
	}

	internal async Task<FileIOStatus> DeleteOldFiles(LocalData modLocalData)
	{
		string path = Path.Combine(modLocalData.FolderAbsolutePath, ".metadata");
		OperationResult<List<string>> listFiles = await _context.FileIO.ListFiles(path).ConfigureAwait(continueOnCapturedContext: false);
		if (listFiles.Status.Success)
		{
			foreach (string item in listFiles.Result)
			{
				string fileName = Path.GetFileName(item);
				if (fileName != modLocalData.ContentFileOrFolder && fileName != Path.GetFileName(modLocalData.ThumbnailFilename) && !modLocalData.ScreenshotsFilenames.Any((string f) => fileName == Path.GetFileName(f)))
				{
					FileIOStatus fileIOStatus = await _context.FileIO.Delete(item).ConfigureAwait(continueOnCapturedContext: false);
					if (!fileIOStatus.Success)
					{
						Log(fileIOStatus.Message, LogLevel.L2_Warning);
					}
				}
			}
		}
		return listFiles.Status;
	}

	public async Task<Result> CleanupReportedMods(FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.CleanupReportedMods", LogLevel.L1_Debug, flowData2);
		ListSubscribedResult listSubscribedResult = await _modsBackendApiService.GetAllSubscribed(flowData2).ConfigureAwait(continueOnCapturedContext: false);
		if (!listSubscribedResult.Success)
		{
			return listSubscribedResult;
		}
		IEnumerable<ModDetails> enumerable = listSubscribedResult.Mods?.Where((ModDetails m) => m.State != ModState.Published && m.State != ModState.Publishing);
		if (enumerable.Count() == 0)
		{
			return Result.ResultSuccessful();
		}
		List<Task<ModsCacheError>> deleteTasks = new List<Task<ModsCacheError>>();
		foreach (ModDetails item in enumerable)
		{
			IMod mod = await _modsCache.GetSubscribedMod(item.Id, item.Version).ConfigureAwait(continueOnCapturedContext: false);
			if (mod != null)
			{
				deleteTasks.Add(_modsCache.CleanUpSubscribedMod(mod.Id, mod.Version));
			}
		}
		ModsCacheError[] source = await Task.WhenAll(deleteTasks).ConfigureAwait(continueOnCapturedContext: false);
		return new Result
		{
			Success = source.All((ModsCacheError r) => r.Success),
			Error = source.FirstOrDefault((ModsCacheError r) => !r.Success)?.Error
		};
	}

	internal async Task<Result> UpstreamSync(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.UpstreamSync", LogLevel.L0_Info, fd);
		AggregateResult syncSuccess = new AggregateResult();
		ListAllPlaysetsResult listAllPlaysetsResult = await _modsBackendApiService.ListAllPlaysets(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (listAllPlaysetsResult.Success)
		{
			foreach (Playset remotePlayset in listAllPlaysetsResult.AllPlaysets)
			{
				if (await _modsCache.GetPlaysetOrNull(remotePlayset.PlaysetId).ConfigureAwait(continueOnCapturedContext: false) == null)
				{
					syncSuccess.Add(await _modsBackendApiService.DeletePlayset(remotePlayset.PlaysetId, fd).ConfigureAwait(continueOnCapturedContext: false));
				}
			}
		}
		Dictionary<string, CachedPlaysetMetaData> allDirtyWithoutTracking = (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllDirtyWithoutTracking();
		foreach (CachedPlaysetMetaData localPlayset in allDirtyWithoutTracking.Values)
		{
			AggregateResult playsetSynced = new AggregateResult();
			if (!localPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend.HasValue)
			{
				CreatePlaysetResult createPlaysetResult = await PerformCreatePlayset(localPlayset, fd).ConfigureAwait(continueOnCapturedContext: false);
				playsetSynced.Add(createPlaysetResult);
				if (createPlaysetResult.Success)
				{
					playsetSynced.Add(await PerformSubscribeMods(createPlaysetResult.PlaysetId, localPlayset.SubscribedModsTracked.GetAllDirtyWithTracking(), fd).ConfigureAwait(continueOnCapturedContext: false));
				}
			}
			else
			{
				playsetSynced.Add(await UpstreamSyncPlayset(localPlayset, fd).ConfigureAwait(continueOnCapturedContext: false));
			}
			if (playsetSynced.Success)
			{
				await _modsCache.ResetAllDirtyInPlayset(localPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
			}
			syncSuccess.Add(playsetSynced);
		}
		return new Result
		{
			Success = syncSuccess.Success,
			Error = ((!syncSuccess.Success) ? new Error(PDX.SDK.Contracts.Enums.Errors.Mods.UnableToComplete, "Failed to complete syncing the local state to the backend, please retry at a later time", "") : null)
		};
	}

	public async Task<PlaysetSyncResult> AutomaticUpstreamSync(FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.AutomaticUpstreamSync", LogLevel.L1_Debug, fd);
		ListAllPlaysetsResult onlinePlaysets = await _modsBackendApiService.ListAllPlaysets(fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!onlinePlaysets.Success)
		{
			return new PlaysetSyncResult
			{
				Success = false
			};
		}
		PlaysetSyncConflict[] array = await DetectSyncConflicts(onlinePlaysets).ConfigureAwait(continueOnCapturedContext: false);
		if (array.Length != 0)
		{
			return new PlaysetSyncResult
			{
				Success = false,
				PlaysetSyncConflicts = array
			};
		}
		foreach (Playset onlinePlayset in onlinePlaysets.AllPlaysets)
		{
			CachedPlaysetMetaData value = (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllDirtyWithoutTracking().FirstOrDefault((KeyValuePair<string, CachedPlaysetMetaData> p) => p.Value.GetKey() == onlinePlayset.PlaysetId.ToString()).Value;
			if (value != null && value.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend.HasValue)
			{
				Result result = await UpstreamSyncPlayset(value, fd).ConfigureAwait(continueOnCapturedContext: false);
				if (!result.Success)
				{
					return result.CastTo<PlaysetSyncResult>();
				}
			}
		}
		IEnumerable<CachedPlaysetMetaData> enumerable = from np in (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllDirtyWithoutTracking()
			where np.Key.Contains("local_")
			select np.Value;
		foreach (CachedPlaysetMetaData newPlayset in enumerable)
		{
			CreatePlaysetResult createPlaysetResult = await PerformCreatePlayset(newPlayset, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!createPlaysetResult.Success)
			{
				return createPlaysetResult.CastTo<PlaysetSyncResult>();
			}
			Result result2 = await PerformSubscribeMods(createPlaysetResult.PlaysetId, newPlayset.SubscribedModsTracked.GetAllDirtyWithTracking(), fd).ConfigureAwait(continueOnCapturedContext: false);
			if (!result2.Success)
			{
				return result2.CastTo<PlaysetSyncResult>();
			}
			await _modsCache.ResetAllDirtyInPlayset(newPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new PlaysetSyncResult
		{
			Success = true
		};
	}

	internal async Task<Result> UpstreamSyncPlayset(CachedPlaysetMetaData localPlayset, FlowData flowData = null)
	{
		int onlinePlaysetId = localPlayset.GeneralDataTracked.TrackedObject.Id;
		Result result = await PerformRenamePlayset(onlinePlaysetId, localPlayset, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result;
		}
		List<PlaysetSubscribedMod> onlineSubscribedMods = await PerformListAllModsInPlayset(onlinePlaysetId).ConfigureAwait(continueOnCapturedContext: false);
		Result result2 = await PerformUnsubscribeMods(localPlayset, onlineSubscribedMods, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!result2.Success)
		{
			return result2;
		}
		Dictionary<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMods = localPlayset.SubscribedModsTracked.GetAllDirtyWithTracking();
		if (dirtyMods.Count > 0)
		{
			Result result3 = await PerformSubscribeMods(onlinePlaysetId, dirtyMods, onlineSubscribedMods, flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (!result3.Success)
			{
				return result3;
			}
			Result result4 = await PerformEnableMods(onlinePlaysetId, onlineSubscribedMods, dirtyMods, flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (!result4.Success)
			{
				return result4;
			}
			Result result5 = await PerformChangeLoadOrder(onlinePlaysetId, onlineSubscribedMods, dirtyMods, flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (!result5.Success)
			{
				return result5;
			}
			foreach (KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> item in dirtyMods)
			{
				await item.Value.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		localPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		await _modsCache.UpdatePlayset(localPlayset.GetKey(), localPlayset).ConfigureAwait(continueOnCapturedContext: false);
		await _modsCache.ResetAllDirtyInPlayset(localPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		return new Result
		{
			Success = true
		};
	}

	internal async Task<Result> PerformSubscribeMods(int playsetId, Dictionary<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMods, FlowData flowData)
	{
		IEnumerable<KeyValuePair<int, string>> enumerable = dirtyMods.Select((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> m) => new KeyValuePair<int, string>(m.Value.TrackedObject.Id, m.Value.TrackedObject.Version));
		if (enumerable.Count() == 0)
		{
			return Result.ResultSuccessful();
		}
		MarkAsSubscribedResult subscribeResult = await _modsBackendApiService.MarkAsSubscribed(enumerable, playsetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (subscribeResult.Success)
		{
			foreach (KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMod in dirtyMods)
			{
				await dirtyMod.Value.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return subscribeResult;
	}

	internal async Task<CreatePlaysetResult> PerformCreatePlayset(CachedPlaysetMetaData newPlayset, FlowData flowData = null)
	{
		CreatePlaysetResult createResult = await _modsBackendApiService.CreatePlayset(newPlayset.GeneralDataTracked.TrackedObject.Name, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!createResult.Success)
		{
			return createResult;
		}
		newPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		await _modsCache.UpdatePlaysetId(newPlayset.GeneralDataTracked.TrackedObject.Id.GetLocalPlaysetKey(), createResult.PlaysetId).ConfigureAwait(continueOnCapturedContext: false);
		await newPlayset.GeneralDataTracked.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
		return createResult;
	}

	internal async Task<Result> PerformRenamePlayset(int onlinePlaysetId, CachedPlaysetMetaData offlinePlayset, FlowData flowData = null)
	{
		RenamePlaysetResult renamePlaysetResult = await _modsBackendApiService.RenamePlayset(onlinePlaysetId, offlinePlayset.GeneralDataTracked.TrackedObject.Name, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!renamePlaysetResult.Success)
		{
			return renamePlaysetResult;
		}
		offlinePlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		await _modsCache.UpdatePlayset(offlinePlayset.GetKey(), offlinePlayset).ConfigureAwait(continueOnCapturedContext: false);
		await offlinePlayset.GeneralDataTracked.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
		return new Result
		{
			Success = true
		};
	}

	internal async Task<Result> PerformChangeLoadOrder(int onlinePlaysetId, IEnumerable<PlaysetSubscribedMod> onlineSubscribedMods, Dictionary<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMods, FlowData flowData = null)
	{
		IEnumerable<KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>>> source = dirtyMods.Where((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> dm) => onlineSubscribedMods.Any((PlaysetSubscribedMod om) => om.GetKey() == dm.Key && om.LoadOrder != dm.Value.TrackedObject.LoadOrder));
		if (source.Count() == 0)
		{
			return Result.ResultSuccessful();
		}
		List<ModLoadOrder> modActivationOrderList = source.Select((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> mod) => new ModLoadOrder
		{
			Mod = mod.Value.TrackedObject.ToMod(),
			LoadOrder = mod.Value.TrackedObject.LoadOrder
		}).ToList();
		return await _modsBackendApiService.SetLoadOrder(modActivationOrderList, onlinePlaysetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> PerformEnableMods(int onlinePlaysetId, IEnumerable<PlaysetSubscribedMod> onlineSubscribedMods, Dictionary<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMods, FlowData flowData = null)
	{
		IEnumerable<KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>>> stateChangedMods = dirtyMods.Where((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> dm) => onlineSubscribedMods.Any((PlaysetSubscribedMod om) => om.GetKey() == dm.Key && om.IsEnabled != dm.Value.TrackedObject.IsEnabled));
		if (stateChangedMods.Count() == 0)
		{
			return Result.ResultSuccessful();
		}
		Result resultEnable = Result.ResultSuccessful();
		List<int> list = (from x in stateChangedMods
			where x.Value.TrackedObject.IsEnabled
			select x.Value.TrackedObject.ToMod().Id).ToList();
		if (list.Count > 0)
		{
			resultEnable = await _modsBackendApiService.EnableBulk(list, onlinePlaysetId, flowData);
		}
		Result result = Result.ResultSuccessful();
		List<int> list2 = (from x in stateChangedMods
			where !x.Value.TrackedObject.IsEnabled
			select x.Value.TrackedObject.ToMod().Id).ToList();
		if (list2.Count > 0)
		{
			result = await _modsBackendApiService.DisableBulk(list2, onlinePlaysetId, flowData);
		}
		return new Result
		{
			Success = (resultEnable.Success && result.Success)
		};
	}

	internal async Task<Result> PerformSubscribeMods(int onlinePlaysetId, Dictionary<string, ChangeTracked<PlaysetSubscribedModMetaData>> dirtyMods, IEnumerable<PlaysetSubscribedMod> onlineSubscribedMods, FlowData flowData)
	{
		IEnumerable<KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>>> source = dirtyMods.Where((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> dm) => !onlineSubscribedMods.Any((PlaysetSubscribedMod om) => om.GetKey() == dm.Key));
		if (source.Count() == 0)
		{
			return Result.ResultSuccessful();
		}
		return await _modsBackendApiService.MarkAsSubscribed(source.Select((KeyValuePair<string, ChangeTracked<PlaysetSubscribedModMetaData>> m) => new KeyValuePair<int, string>(m.Value.TrackedObject.Id, m.Value.TrackedObject.Version)), onlinePlaysetId, flowData).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<Result> PerformUnsubscribeMods(CachedPlaysetMetaData localPlayset, IEnumerable<PlaysetSubscribedMod> onlineSubscribedMods, FlowData flowData = null)
	{
		Result[] source = await Task.WhenAll(from m in onlineSubscribedMods
			where localPlayset.SubscribedModsTracked.GetValueOrNull(m.GetKey()) == null
			select await _modsBackendApiService.MarkAsUnsubscribed(m.Id, localPlayset.GeneralDataTracked.TrackedObject.Id, flowData).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		return new Result
		{
			Success = !source.Any((Result r) => !r.Success)
		};
	}

	internal async Task<List<PlaysetSubscribedMod>> PerformListAllModsInPlayset(int onlinePlaysetId, FlowData flowData = null)
	{
		int page = 1;
		List<PlaysetSubscribedMod> onlineMods = new List<PlaysetSubscribedMod>();
		ListSubscribedModsInPlaysetResult getMods = await _modsBackendApiService.ListModsInPlayset(onlinePlaysetId, 100, page, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!getMods.Success)
		{
			return onlineMods;
		}
		onlineMods.AddRange(getMods.Mods);
		while (onlineMods.Count < getMods.TotalCount)
		{
			IModsBackendApiService modsBackendApiService = _modsBackendApiService;
			int? limit = 100;
			int num = page + 1;
			page = num;
			ListSubscribedModsInPlaysetResult listSubscribedModsInPlaysetResult = await modsBackendApiService.ListModsInPlayset(onlinePlaysetId, limit, num, flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (!listSubscribedModsInPlaysetResult.Success)
			{
				break;
			}
			onlineMods.AddRange(listSubscribedModsInPlaysetResult.Mods);
		}
		return onlineMods;
	}

	public async Task<PlaysetSyncConflict[]> DetectSyncConflicts(ListAllPlaysetsResult onlinePlaysets, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DetectSyncConflicts", LogLevel.L1_Debug, fd);
		return (await Task.WhenAll(onlinePlaysets.AllPlaysets.Select(async delegate(Playset op)
		{
			KeyValuePair<string, CachedPlaysetMetaData> keyValuePair = (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllDirtyWithoutTracking().FirstOrDefault((KeyValuePair<string, CachedPlaysetMetaData> p) => p.Key == op.PlaysetId.ToString());
			Log("Inside SyncConflicts -> LocalPlayset: " + keyValuePair.ToJson(), LogLevel.L0_Info, fd);
			if (keyValuePair.Key != null && op.Updated > keyValuePair.Value.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend)
			{
				Log("Inside If localPlayset.Key not null AND onlinePlayset.Updated > localPlayset.UpdatedSyncedWithBackend: " + keyValuePair.Key + " | " + op.Updated.ToJson() + " | " + keyValuePair.Value.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend.ToJson(), LogLevel.L0_Info, fd);
				return new PlaysetSyncConflict
				{
					OnlinePlayset = new PlaysetSyncConflictMetadata
					{
						Id = op.PlaysetId,
						Name = op.Name,
						UpdatedDate = op.Updated.Value
					},
					LocalPlayset = new PlaysetSyncConflictMetadata
					{
						Id = keyValuePair.Value.GeneralDataTracked.TrackedObject.Id,
						Name = keyValuePair.Value.GeneralDataTracked.TrackedObject.Name,
						UpdatedDate = keyValuePair.Value.GeneralDataTracked.TrackedObject.Updated
					}
				};
			}
			return (PlaysetSyncConflict)null;
		})).ConfigureAwait(continueOnCapturedContext: false)).Where((PlaysetSyncConflict psc) => psc != null).ToArray();
	}
}
