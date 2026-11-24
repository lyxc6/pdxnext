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
using PDX.SDK.Events;
using PDX.SDK.Internal.Errors;
using PDX.SDK.Internal.Events;
using PDX.SDK.Internal.Events.Internal.Mods;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Util;
using PDX.SDK.Util;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsSubscriptionService : InternalService, IModsSubscriptionService
{
	private readonly IModsCache _modsCache;

	private readonly IPathFactory _pathFactory;

	private readonly IModsBackendApiService _modsBackendApiService;

	private readonly IModsDownloaderService _modsDownloaderService;

	private readonly IFileIO _fileIo;

	private readonly AsyncReaderWriterLock _readWriteLock = new AsyncReaderWriterLock();

	[Preserve]
	public ModsSubscriptionService(IContext context, IModsCache modsCache, IPathFactory pathFactory, IFileIO fileIO, IModsBackendApiService modsBackendApiService, IModsDownloaderService modsDownloaderService)
		: base("ModsSubscriptionService", context as Context)
	{
		_modsCache = modsCache;
		_pathFactory = pathFactory;
		_fileIo = fileIO;
		_modsBackendApiService = modsBackendApiService;
		_modsDownloaderService = modsDownloaderService;
	}

	public virtual async Task<ListModsInPlaysetResult> ListModsInPlayset(int playsetId, int? limit = null, int? page = null, bool includeOnline = false, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.GetModsInPlayset", LogLevel.L1_Debug, flowData2);
		List<IPlaysetMod> mods = new List<IPlaysetMod>();
		ListSubscribedModsInPlaysetResult onlineModsResult = new ListSubscribedModsInPlaysetResult
		{
			Success = includeOnline
		};
		if (includeOnline)
		{
			onlineModsResult = await _modsBackendApiService.ListModsInPlayset(playsetId, limit, page, flowData2).ConfigureAwait(continueOnCapturedContext: false);
		}
		CachedPlaysetMetaData offlineModsPlayset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (offlineModsPlayset == null)
		{
			if (onlineModsResult.Success)
			{
				return new ListModsInPlaysetResult
				{
					Success = onlineModsResult.Success,
					Page = onlineModsResult.Page,
					Limit = onlineModsResult.Limit,
					TotalCount = onlineModsResult.TotalCount,
					Mods = new List<IPlaysetMod>(onlineModsResult.Mods)
				};
			}
			return new ListModsInPlaysetResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The playset does not exist locally", "")
			};
		}
		List<PlaysetSubscribedMod> subscribedMods = (from x in await Task.WhenAll(offlineModsPlayset.SubscribedModsTracked.Select(async (PlaysetSubscribedModMetaData sm) => new
			{
				playsetSubscribedModMetaData = sm,
				iMod = await _modsCache.GetModOrNull(sm.GetKey()).ConfigureAwait(continueOnCapturedContext: false)
			})).ConfigureAwait(continueOnCapturedContext: false)
			where x.iMod != null
			select x.playsetSubscribedModMetaData.ToPlaysetMod(x.iMod)).ToList();
		if (onlineModsResult.Success && onlineModsResult.Mods.Count > 0)
		{
			mods.AddRange(onlineModsResult.Mods.Select(delegate(PlaysetSubscribedMod om)
			{
				PlaysetSubscribedMod playsetSubscribedMod = subscribedMods.FirstOrDefault((PlaysetSubscribedMod sm) => sm.GetKey() == om.GetKey());
				if (playsetSubscribedMod != null)
				{
					om.LocalData = playsetSubscribedMod.LocalData;
					om.LoadOrder = playsetSubscribedMod.LoadOrder;
					subscribedMods.Remove(playsetSubscribedMod);
				}
				return om;
			}));
		}
		mods.AddRange(subscribedMods);
		IEnumerable<PlaysetWipMod> collection = from x in await Task.WhenAll(offlineModsPlayset.WipModsTracked.Select(async (PlaysetWipModMetaData wip) => new
			{
				playsetWipModMetaData = wip,
				iMod = await _modsCache.GetModOrNull(wip.GetKey()).ConfigureAwait(continueOnCapturedContext: false)
			})).ConfigureAwait(continueOnCapturedContext: false)
			where x.iMod != null
			select x.playsetWipModMetaData.ToPlaysetMod(x.iMod);
		mods.AddRange(collection);
		IEnumerable<PlaysetUnmanagedMod> collection2 = from x in await Task.WhenAll(offlineModsPlayset.UnmanagedModsTracked.Select(async (PlaysetUnmanagedModMetaData unmanaged) => new
			{
				playsetUnmanagedModMetaData = unmanaged,
				iMod = await _modsCache.GetModOrNull(unmanaged.GetKey()).ConfigureAwait(continueOnCapturedContext: false)
			})).ConfigureAwait(continueOnCapturedContext: false)
			where x.iMod != null
			select x.playsetUnmanagedModMetaData.ToPlaysetMod(x.iMod);
		mods.AddRange(collection2);
		return new ListModsInPlaysetResult
		{
			Success = true,
			Limit = (limit ?? 10),
			Page = (page ?? 1),
			TotalCount = mods.Count,
			Mods = mods.OrderBy((IPlaysetMod m) => m.LoadOrder).GetPage(page ?? 1, limit ?? 10).ToList()
		};
	}

	public virtual async Task<SubscribeResult> Subscribe(IEnumerable<IMod> mods, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Subscribe", LogLevel.L1_Debug, fd);
		bool syncedOnline = true;
		CachedPlaysetMetaData cachedPlayset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlayset == null)
		{
			return new SubscribeResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		IMod[] onlineSubscribedMods = null;
		IEnumerable<IMod> enumerable = mods?.Where(delegate(IMod m)
		{
			LocalData localData3 = m.LocalData;
			if (localData3 == null || localData3.LocalType != LocalType.Subscribed)
			{
				if (m.Id > 0)
				{
					LocalData localData4 = m.LocalData;
					if (localData4 == null)
					{
						return true;
					}
					return localData4.LocalType != LocalType.WorkInProgress;
				}
				return false;
			}
			return true;
		});
		if (enumerable != null && enumerable.Count() > 0)
		{
			MarkAsSubscribedResult markAsSubscribedResult = await _modsBackendApiService.MarkAsSubscribed(enumerable.Select((IMod m) => new KeyValuePair<int, string>(m.Id, m.Version)), playsetId, fd);
			if (!markAsSubscribedResult.Success)
			{
				syncedOnline = false;
				_context.Log("The list of mods could not be subscribed to in the backend. They will be subscribed to locally if on disk.", LogLevel.L0_Info, fd);
			}
			else
			{
				onlineSubscribedMods = markAsSubscribedResult.ModsSubscribed;
			}
		}
		SubscribeResult finalSubscribeResult = new SubscribeResult
		{
			Success = true,
			ModsSubscribedStatus = new List<ModSubscribeStatus>(),
			PlaysetId = playsetId
		};
		foreach (IMod mod in mods)
		{
			bool isLocal = false;
			Result.ResultSuccessful();
			LocalData localData = mod.LocalData;
			if (localData != null && localData.LocalType == LocalType.Subscribed)
			{
				goto IL_0318;
			}
			if (mod.Id > 0)
			{
				LocalData localData2 = mod.LocalData;
				if (localData2 == null || localData2.LocalType != LocalType.WorkInProgress)
				{
					goto IL_0318;
				}
			}
			Result result = await SubscribeLocalMod(mod.Name, cachedPlayset, fd).ConfigureAwait(continueOnCapturedContext: false);
			isLocal = true;
			goto IL_04c9;
			IL_04c9:
			finalSubscribeResult.Success &= result.Success;
			finalSubscribeResult.ModsSubscribedStatus.Add(new ModSubscribeStatus
			{
				Mod = mod,
				IsSubscribed = result.Success
			});
			if (!result.Success)
			{
				finalSubscribeResult.Error = result.Error;
			}
			else if (isLocal)
			{
				await Enable(mod.Name, playsetId);
			}
			else
			{
				await Enable(mod.Id, playsetId, enableOnBackend: false);
			}
			continue;
			IL_0318:
			mod.Version = onlineSubscribedMods?.FirstOrDefault((IMod m) => m.Id == mod.Id)?.Version ?? mod.Version;
			result = await SubscribePdxMod(mod.Id, cachedPlayset, mod.Version, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (syncedOnline && result.Success)
			{
				cachedPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
			}
			goto IL_04c9;
		}
		Result result2 = await UpdateAndPersistPlaysetMetadata(cachedPlayset, syncedOnline).ConfigureAwait(continueOnCapturedContext: false);
		if (!result2.Success)
		{
			return result2.CastTo<SubscribeResult>();
		}
		return finalSubscribeResult;
	}

	internal async Task<Result> SubscribePdxMod(int modId, CachedPlaysetMetaData playset, string version = null, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.SubscribePdxMod", LogLevel.L0_Info, fd);
		if (string.IsNullOrEmpty(version))
		{
			GetModVersionsResult getModVersionsResult = await _modsBackendApiService.GetVersions(modId, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (getModVersionsResult.Success)
			{
				version = getModVersionsResult.Versions.OrderBy((ModVersionResult v) => v.CreatedDate).Last().Id;
			}
		}
		IMod cachedMod = await _modsCache.GetSubscribedMod(modId, version).ConfigureAwait(continueOnCapturedContext: false);
		ulong size = cachedMod?.Size ?? 0;
		GetDetailsResult modDetailsResult = new GetDetailsResult();
		if (cachedMod == null)
		{
			modDetailsResult = await _modsBackendApiService.GetDetails(modId, version, _context.Platform.GetModsPlatform().ToModsPlatformString(), flowData).ConfigureAwait(continueOnCapturedContext: false);
			if (!modDetailsResult.Success)
			{
				return modDetailsResult;
			}
			size = modDetailsResult.Mod.Size;
		}
		if (!(await _modsCache.AddSubscribedModToPlayset(modId, version, size, playset).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modId} could not be subscribed to the playset {playset.GeneralDataTracked.TrackedObject.Id}", "")
			};
		}
		(_context.Events as IInternalEventHub).Publish(new ModSubscribed
		{
			EventId = Guid.NewGuid(),
			ManagedModId = modId
		});
		IMod localMod = null;
		if (cachedMod == null)
		{
			if (_context.Config.Mods.UsePatching.HasValue && _context.Config.Mods.UsePatching.Value)
			{
				int result2;
				IOrderedEnumerable<IMod> source = from x in await _modsCache.GetAllMods()
					where x.Id == modId && x.Version != null
					orderby (!int.TryParse(x.Version, out result2)) ? int.MinValue : result2
					select x;
				if (source.Count() > 0)
				{
					localMod = source.Last();
					if (localMod.Version != version)
					{
						Result result = await PatchMod(localMod, modDetailsResult.Mod, playset.GeneralDataTracked.TrackedObject.Id, fd);
						if (result.Success)
						{
							return result;
						}
					}
				}
			}
			Result resultDownload = await DownloadNewPdxMod(modDetailsResult, modId, version, playset.GeneralDataTracked.TrackedObject.Id, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (resultDownload.Success && _context.Config.Mods.UsePatching.HasValue && _context.Config.Mods.UsePatching.Value)
			{
				await TryRemovePreviousVersion(localMod, fd);
			}
			return resultDownload;
		}
		return Result.ResultSuccessful();
	}

	public virtual async Task<Result> PatchMod(IMod cachedMod, IMod remoteMod, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.PatchMod", LogLevel.L1_Debug, fd);
		GetDetailsResult remoteModDetails = await _modsBackendApiService.GetDetails(remoteMod.Id, remoteMod.Version, _context.Platform.GetModsPlatform().ToModsPlatformString(), fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!remoteModDetails.Success)
		{
			return remoteModDetails;
		}
		CopyDirectoryResult copyDirectoryResult = await CopyModSourceDirectory(cachedMod, remoteModDetails).ConfigureAwait(continueOnCapturedContext: false);
		string targetpath = copyDirectoryResult.TargetPath;
		if (!copyDirectoryResult.Success)
		{
			await _fileIo.DeleteDirectory(targetpath, recursive: true);
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, "Copying of source mod files failed", "")
			};
		}
		if (copyDirectoryResult.WasTargetAlreadyPresent)
		{
			return new Result
			{
				Success = true
			};
		}
		DownloadResult downloadResult = await _modsDownloaderService.Download(remoteModDetails.Mod.ToModDownloadMetadata(), doNotSetTransferStateToSuccessful: true, useFinalDirAsTemp: true, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadResult.Success)
		{
			Log("Mod patching failed: " + downloadResult.Error?.Details, LogLevel.L2_Warning, fd);
		}
		if (downloadResult.Success)
		{
			ModsCacheError metadataUpdated = await _modsCache.CacheNewSubscribedMod(remoteMod, downloadResult, playsetId);
			if (!metadataUpdated.Success)
			{
				await _fileIo.DeleteDirectory(targetpath, recursive: true);
				return metadataUpdated;
			}
			await TryRemovePreviousVersion(cachedMod, fd);
			return new Result
			{
				Success = true
			};
		}
		await _fileIo.DeleteDirectory(targetpath, recursive: true);
		return downloadResult;
	}

	private async Task<Result> TryRemovePreviousVersion(IMod cachedMod, FlowData flowData = null)
	{
		if (cachedMod == null)
		{
			return new Result
			{
				Success = true
			};
		}
		bool notUsed = true;
		foreach (Playset allPlayset in (await _modsBackendApiService.ListAllPlaysets()).AllPlaysets)
		{
			if ((await _modsBackendApiService.ListModsInPlayset(allPlayset.PlaysetId)).Mods.Exists((PlaysetSubscribedMod m) => m.Id == cachedMod.Id && m.Version == cachedMod.Version))
			{
				notUsed = false;
				break;
			}
		}
		if (notUsed)
		{
			if ((await _fileIo.DeleteDirectory(cachedMod.LocalData.FolderAbsolutePath, recursive: true)).Success)
			{
				string key = cachedMod.Id.GetKey(cachedMod.Version);
				ModsCacheError modsCacheError = await _modsCache.DeleteMod(key);
				return new Result
				{
					Success = modsCacheError.Success
				};
			}
			return new Result
			{
				Success = false,
				Error = new Error(InternalError.FileIOError, "Deleting of old version mod files failed", "")
			};
		}
		return new Result
		{
			Success = true
		};
	}

	private async Task<CopyDirectoryResult> CopyModSourceDirectory(IMod cachedMod, GetDetailsResult getDetails)
	{
		string sourcepath = cachedMod.LocalData.FolderAbsolutePath;
		ModDownloadMetadata modDownloadMetadata = getDetails.Mod.ToModDownloadMetadata();
		string modFolderName = ModPath.GetModFolderName(modDownloadMetadata.Id, modDownloadMetadata.Version);
		string targetpath = Path.Combine(Directory.GetParent(sourcepath).FullName, modFolderName);
		if (_fileIo.PathExists(targetpath))
		{
			return new CopyDirectoryResult
			{
				TargetPath = targetpath,
				Success = true,
				WasTargetAlreadyPresent = true
			};
		}
		bool success = false;
		if (_modsDownloaderService is ModsDownloaderService)
		{
			success = await LockWhileAccessingFile(async () => await (_modsDownloaderService as ModsDownloaderService).GetExecutor().CopyRepoFiles(sourcepath, targetpath).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new CopyDirectoryResult
		{
			TargetPath = targetpath,
			Success = success,
			WasTargetAlreadyPresent = false
		};
	}

	internal async Task<T> LockWhileAccessingFile<T>(Func<Task<T>> writeFunc)
	{
		_ = 1;
		try
		{
			await _readWriteLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			return await writeFunc().ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_readWriteLock.ReleaseWriterLock();
		}
	}

	internal async Task<Result> SubscribeLocalMod(string modName, CachedPlaysetMetaData cachedPlayset, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.SubscribeLocalMod", LogLevel.L0_Info, flowData2);
		Mod cachedMod = await _modsCache.GetModOrNull(modName).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedMod == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The mod " + modName + " does not exist", "")
			};
		}
		if (!((cachedMod.LocalData.LocalType != LocalType.WorkInProgress) ? (await _modsCache.AddUnmanagedModToPlayset(modName, cachedMod.Size, cachedPlayset).ConfigureAwait(continueOnCapturedContext: false)) : (await _modsCache.AddWipModToPlayset(modName, cachedMod.Size, cachedPlayset).ConfigureAwait(continueOnCapturedContext: false))))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modName} could not be subscribed to the playset {cachedPlayset.GeneralDataTracked.TrackedObject.Id}", "")
			};
		}
		(_context.Events as IInternalEventHub).Publish(new ModSubscribed
		{
			EventId = Guid.NewGuid(),
			UnmanagedOrWipModName = modName
		});
		if (!cachedMod.InstalledDate.HasValue)
		{
			cachedMod.InstalledDate = DateTime.Now;
			await _modsCache.UpdateMod(cachedMod).ConfigureAwait(continueOnCapturedContext: false);
			await _modsCache.ResetDirtyMod(cachedMod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return Result.ResultSuccessful();
	}

	public virtual async Task<Result> Unsubscribe(int modId, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Unsubscribe", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData cachedPlayset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlayset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		KeyValuePair<string, PlaysetSubscribedModMetaData> modInPlayset = cachedPlayset.SubscribedModsTracked.GetAllWithoutTracking().FirstOrDefault((KeyValuePair<string, PlaysetSubscribedModMetaData> m) => m.Value.Id == modId);
		if (modInPlayset.Key == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modId} is not in the playset {playsetId}", "")
			};
		}
		Result markAsUnsubscribedResult = await _modsBackendApiService.MarkAsUnsubscribed(modId, playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		bool modRemovedFromPlayset = await _modsCache.RemoveModFromPlayset(modInPlayset.Key, cachedPlayset).ConfigureAwait(continueOnCapturedContext: false);
		if (!markAsUnsubscribedResult.Success && !modRemovedFromPlayset)
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modId} could not be unsubscribed from the playset {playsetId}", "")
			};
		}
		object obj = cachedPlayset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			Mod modToUnload = await _modsCache.GetModOrNull(modId.GetKey(modInPlayset.Value.Version));
			(_context.Events as IInternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				ModToUnload = modToUnload
			});
		}
		bool synced = markAsUnsubscribedResult.Success && modRemovedFromPlayset;
		if (synced)
		{
			cachedPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		}
		if (!(await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Any((CachedPlaysetMetaData p) => p.SubscribedModsTracked.GetAllWithoutTracking().Any((KeyValuePair<string, PlaysetSubscribedModMetaData> sm) => sm.Key == modInPlayset.Key)))
		{
			await _modsCache.CleanUpSubscribedMod(modId, modInPlayset.Value.Version).ConfigureAwait(continueOnCapturedContext: false);
		}
		(_context.Events as IInternalEventHub).Publish(new ModUnsubscribed
		{
			EventId = Guid.NewGuid(),
			ManagedModId = modId
		});
		return await UpdateAndPersistPlaysetMetadata(cachedPlayset, synced).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<Result> Unsubscribe(int modId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log($"Mods.Unsubscribe, mod {modId} from all playsets", LogLevel.L1_Debug, fd);
		List<KeyValuePair<string, CachedPlaysetMetaData>> list = (from x in (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllWithoutTracking()
			where x.Value.SubscribedModsTracked.Any((PlaysetSubscribedModMetaData m) => m.Id == modId)
			select x).ToList();
		if (list.Count == 0)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, $"The mod with id:{modId} is not present in any playsets", "")
			};
		}
		Result result = (await Task.WhenAll(list.Select((KeyValuePair<string, CachedPlaysetMetaData> p) => Unsubscribe(modId, p.Value.GeneralDataTracked.TrackedObject.Id, fd))).ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault((Result r) => !r.Success);
		return new Result
		{
			Success = (result == null),
			Error = result?.Error
		};
	}

	public virtual async Task<Result> Unsubscribe(string modName, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Unsubscribe", LogLevel.L1_Debug, flowData2);
		if (string.IsNullOrEmpty(modName))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "Specify a mod to unsubscribe from", "")
			};
		}
		CachedPlaysetMetaData cachedPlayset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlayset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		if (!(await _modsCache.RemoveModFromPlayset(modName, cachedPlayset).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modName} could not be unsubscribed from the playset {playsetId}", "")
			};
		}
		object obj = cachedPlayset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			Mod modToUnload = await _modsCache.GetModOrNull(modName);
			(_context.Events as IInternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				ModToUnload = modToUnload
			});
		}
		(_context.Events as IInternalEventHub).Publish(new ModUnsubscribed
		{
			EventId = Guid.NewGuid(),
			UnmanagedOrWipModName = modName
		});
		return await UpdateAndPersistPlaysetMetadata(cachedPlayset, synced: false, localChange: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<Result> Unsubscribe(string modName, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Unsubscribe, mod " + modName + " from all playsets", LogLevel.L1_Debug, fd);
		List<KeyValuePair<string, CachedPlaysetMetaData>> list = (from x in (await _modsCache.GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).GetAllWithoutTracking()
			where x.Value.UnmanagedModsTracked.Any((PlaysetUnmanagedModMetaData m) => m.Path == modName) || x.Value.WipModsTracked.Any((PlaysetWipModMetaData m) => m.Id == modName)
			select x).ToList();
		if (list.Count == 0)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The mod " + modName + " is not present in any playsets", "")
			};
		}
		Result result = (await Task.WhenAll(list.Select((KeyValuePair<string, CachedPlaysetMetaData> p) => Unsubscribe(modName, p.Value.GeneralDataTracked.TrackedObject.Id, fd))).ConfigureAwait(continueOnCapturedContext: false)).FirstOrDefault((Result r) => !r.Success);
		return new Result
		{
			Success = (result == null),
			Error = result?.Error
		};
	}

	public virtual async Task<Result> Enable(int modId, int playsetId, bool enableOnBackend = true, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Enable", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		if (!(await SetSubscribedModEnabledStateInPlayset(modId, playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, $"The mod {modId} could not be updated in the playset {playsetId}", "")
			};
		}
		Result enableResult = Result.ResultSuccessful();
		if (enableOnBackend)
		{
			enableResult = await _modsBackendApiService.MarkAsEnabled(modId, playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
			if (enableResult.Success)
			{
				playset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
			}
		}
		object obj = playset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			Mod newModToLoad = await _modsCache.GetModOrNull(modId.GetKey(playset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.Id == modId)?.Version));
			(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				NewModToLoad = newModToLoad
			});
		}
		return await UpdateAndPersistPlaysetMetadata(playset, enableResult.Success).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<Result> Enable(string modName, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Enable", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		Mod mod = await _modsCache.GetModOrNull(modName).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The mod " + modName + " could not be found locally", "")
			};
		}
		if (!((mod.LocalData.LocalType != LocalType.WorkInProgress) ? (await SetUnmanagedModEnabledStateInPlayset(modName, playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false)) : (await SetWipModEnabledStateInPlayset(modName, playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false))))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modName} could not be updated in the playset {playsetId}", "")
			};
		}
		object obj = playset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				NewModToLoad = mod
			});
		}
		return await UpdateAndPersistPlaysetMetadata(playset, synced: false, localChange: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<BulkResult> EnableBulk(List<string> modNames, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.EnableBulk", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", ""),
				Failures = null
			};
		}
		List<string> failingModNames = new List<string>();
		int i = 0;
		while (i < modNames.Count)
		{
			string modName = modNames[i];
			Mod mod = await _modsCache.GetModOrNull(modName).ConfigureAwait(continueOnCapturedContext: false);
			if (mod == null)
			{
				failingModNames.Add(modName);
			}
			else if (!((mod.LocalData.LocalType != LocalType.WorkInProgress) ? (await SetUnmanagedModEnabledStateInPlayset(modName, playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false)) : (await SetWipModEnabledStateInPlayset(modName, playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false))))
			{
				failingModNames.Add(modName);
			}
			else if (!(await UpdateAndPersistPlaysetMetadata(playset, synced: false, localChange: true).ConfigureAwait(continueOnCapturedContext: false)).Success)
			{
				failingModNames.Add(modName);
			}
			else
			{
				object obj = playset;
				if (obj == await _modsCache.GetActivePlayset())
				{
					(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
					{
						EventId = Guid.NewGuid(),
						ModToUnload = mod
					});
				}
			}
			int num = i + 1;
			i = num;
		}
		if (failingModNames.Count > 0)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, string.Format("Following Mods could not be updated in the playset {0} : {1}", playsetId, string.Join(", ", failingModNames)), ""),
				Failures = failingModNames
			};
		}
		return Result.ResultSuccessful().CastTo<BulkResult>();
	}

	public virtual async Task<BulkResult> EnableBulk(List<int> modIds, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.EnableBulk", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		Result onlineResult = await _modsBackendApiService.EnableBulk(modIds, playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (onlineResult.Success)
		{
			if (playset != null)
			{
				playset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
			}
		}
		else
		{
			Log("Mods.EnableBulk could not enable in the backend", LogLevel.L2_Warning, fd);
		}
		List<int> failingModIds = new List<int>();
		if (playset == null)
		{
			Log($"The playset {playsetId} was not found so the status of the mod cannot be updated locally.", LogLevel.L2_Warning, fd);
		}
		else
		{
			int i = 0;
			while (i < modIds.Count)
			{
				if (!(await SetSubscribedModEnabledStateInPlayset(modIds[i], playset, enabledState: true).ConfigureAwait(continueOnCapturedContext: false)))
				{
					failingModIds.Add(modIds[i]);
				}
				int num = i + 1;
				i = num;
			}
			if (failingModIds.Count > 0)
			{
				Log(string.Format("Following mods could not be locally set as enable in the playset {0} : {1}", playsetId, string.Join(", ", failingModIds)), LogLevel.L2_Warning, fd);
			}
		}
		return new BulkResult
		{
			Success = onlineResult.Success,
			Error = onlineResult.Error,
			Failures = failingModIds.ConvertAll((int num2) => num2.ToString())
		};
	}

	public virtual async Task<BulkResult> EnableBulk(int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.EnableBulk", LogLevel.L1_Debug, fd);
		List<string> failingMods = new List<string>();
		if (await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false) == null)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", ""),
				Failures = failingMods
			};
		}
		List<IPlaysetMod> allMods = (await ListModsInPlayset(playsetId, null, null, includeOnline: true)).Mods;
		List<int> list = (from m in allMods
			where m is PlaysetSubscribedMod
			select (m as PlaysetSubscribedMod).Id).ToList();
		BulkResult managedEnableResult;
		if (list.Count > 0)
		{
			managedEnableResult = await EnableBulk(list, playsetId, fd);
			if (managedEnableResult.Failures != null)
			{
				failingMods.AddRange(managedEnableResult.Failures);
			}
		}
		else
		{
			managedEnableResult = new BulkResult
			{
				Success = true
			};
		}
		List<string> list2 = (from m in allMods
			where m is PlaysetUnmanagedMod
			select (m as PlaysetUnmanagedMod).Id).ToList();
		BulkResult unmanagedEnableResult;
		if (list2.Count > 0)
		{
			unmanagedEnableResult = await EnableBulk(list2, playsetId, fd);
			if (unmanagedEnableResult.Failures != null)
			{
				failingMods.AddRange(unmanagedEnableResult.Failures);
			}
		}
		else
		{
			unmanagedEnableResult = new BulkResult
			{
				Success = true
			};
		}
		List<string> list3 = (from m in allMods
			where m is PlaysetWipMod
			select (m as PlaysetWipMod).Id).ToList();
		BulkResult bulkResult;
		if (list3.Count > 0)
		{
			bulkResult = await EnableBulk(list3, playsetId, fd);
			if (bulkResult.Failures != null)
			{
				failingMods.AddRange(bulkResult.Failures);
			}
		}
		else
		{
			bulkResult = new BulkResult
			{
				Success = true
			};
		}
		return new BulkResult
		{
			Success = (managedEnableResult.Success && unmanagedEnableResult.Success && bulkResult.Success),
			Error = (managedEnableResult.Error ?? unmanagedEnableResult.Error ?? bulkResult.Error),
			Failures = failingMods
		};
	}

	public virtual async Task<Result> Disable(int modId, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.Disable", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		if (!(await SetSubscribedModEnabledStateInPlayset(modId, playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, $"The mod {modId} could not be updated in the playset {playsetId}", "")
			};
		}
		Result disableResult = await _modsBackendApiService.MarkAsDisabled(modId, playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (disableResult.Success)
		{
			playset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		}
		object obj = playset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			Mod modToUnload = await _modsCache.GetModOrNull(modId.GetKey(playset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.Id == modId)?.Version));
			(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				ModToUnload = modToUnload
			});
		}
		return await UpdateAndPersistPlaysetMetadata(playset, disableResult.Success).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<Result> Disable(string modName, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.Disable", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", "")
			};
		}
		Mod mod = await _modsCache.GetModOrNull(modName).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The mod " + modName + " could not be found locally", "")
			};
		}
		if (!((mod.LocalData.LocalType != LocalType.WorkInProgress) ? (await SetUnmanagedModEnabledStateInPlayset(modName, playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false)) : (await SetWipModEnabledStateInPlayset(modName, playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false))))
		{
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The mod {modName} could not be updated in the playset {playsetId}", "")
			};
		}
		object obj = playset;
		if (obj == await _modsCache.GetActivePlayset())
		{
			(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
			{
				EventId = Guid.NewGuid(),
				ModToUnload = mod
			});
		}
		return await UpdateAndPersistPlaysetMetadata(playset, synced: false, localChange: true).ConfigureAwait(continueOnCapturedContext: false);
	}

	public virtual async Task<BulkResult> DisableBulk(List<string> modNames, int playsetId, FlowData flowData = null)
	{
		FlowData flowData2 = new FlowData(flowData);
		Log("Mods.DisableBulk", LogLevel.L1_Debug, flowData2);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (playset == null)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", ""),
				Failures = null
			};
		}
		List<string> failingModNames = new List<string>();
		int i = 0;
		while (i < modNames.Count)
		{
			string modName = modNames[i];
			Mod mod = await _modsCache.GetModOrNull(modName).ConfigureAwait(continueOnCapturedContext: false);
			if (mod == null)
			{
				failingModNames.Add(modName);
			}
			else if (!((mod.LocalData.LocalType != LocalType.WorkInProgress) ? (await SetUnmanagedModEnabledStateInPlayset(modName, playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false)) : (await SetWipModEnabledStateInPlayset(modName, playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false))))
			{
				failingModNames.Add(modName);
			}
			else if (!(await UpdateAndPersistPlaysetMetadata(playset, synced: false, localChange: true).ConfigureAwait(continueOnCapturedContext: false)).Success)
			{
				failingModNames.Add(modName);
			}
			else
			{
				object obj = playset;
				if (obj == await _modsCache.GetActivePlayset())
				{
					(_context.Events as InternalEventHub).Publish(new ModLoadStatusChanged
					{
						EventId = Guid.NewGuid(),
						ModToUnload = mod
					});
				}
			}
			int num = i + 1;
			i = num;
		}
		if (failingModNames.Count > 0)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, string.Format("Following Mods could not be updated in the playset {0} : {1}", playsetId, string.Join(", ", failingModNames)), ""),
				Failures = failingModNames
			};
		}
		return Result.ResultSuccessful().CastTo<BulkResult>();
	}

	public virtual async Task<BulkResult> DisableBulk(List<int> modIds, int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DisableBulk", LogLevel.L1_Debug, fd);
		CachedPlaysetMetaData playset = await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		Result onlineResult = await _modsBackendApiService.DisableBulk(modIds, playsetId, fd).ConfigureAwait(continueOnCapturedContext: false);
		if (onlineResult.Success)
		{
			if (playset != null)
			{
				playset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
			}
		}
		else
		{
			Log("Mods.DisableBulk could not disable in the backend", LogLevel.L2_Warning, fd);
		}
		List<int> failingModIds = new List<int>();
		if (playset == null)
		{
			Log($"The playset {playsetId} was not found so the status of the mod cannot be updated locally.", LogLevel.L2_Warning, fd);
		}
		else
		{
			int i = 0;
			while (i < modIds.Count)
			{
				if (!(await SetSubscribedModEnabledStateInPlayset(modIds[i], playset, enabledState: false).ConfigureAwait(continueOnCapturedContext: false)))
				{
					failingModIds.Add(modIds[i]);
				}
				int num = i + 1;
				i = num;
			}
			if (failingModIds.Count > 0)
			{
				Log(string.Format("Following mods could not be locally set as disabled in the playset {0} : {1}", playsetId, string.Join(", ", failingModIds)), LogLevel.L2_Warning, fd);
			}
		}
		return new BulkResult
		{
			Success = onlineResult.Success,
			Error = onlineResult.Error,
			Failures = failingModIds.ConvertAll((int num2) => num2.ToString())
		};
	}

	public virtual async Task<BulkResult> DisableBulk(int playsetId, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		Log("Mods.DisableBulk", LogLevel.L1_Debug, fd);
		List<string> failingMods = new List<string>();
		if (await _modsCache.GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false) == null)
		{
			return new BulkResult
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset id {playsetId} is invalid", ""),
				Failures = failingMods
			};
		}
		List<IPlaysetMod> allMods = (await ListModsInPlayset(playsetId, null, null, includeOnline: true)).Mods;
		List<int> list = (from m in allMods
			where m is PlaysetSubscribedMod
			select (m as PlaysetSubscribedMod).Id).ToList();
		BulkResult managedDisableResult;
		if (list.Count > 0)
		{
			managedDisableResult = await DisableBulk(list, playsetId, fd);
			if (managedDisableResult.Failures != null)
			{
				failingMods.AddRange(managedDisableResult.Failures);
			}
		}
		else
		{
			managedDisableResult = new BulkResult
			{
				Success = true
			};
		}
		List<string> list2 = (from m in allMods
			where m is PlaysetUnmanagedMod
			select (m as PlaysetUnmanagedMod).Id).ToList();
		BulkResult unmanagedDisableResult;
		if (list2.Count > 0)
		{
			unmanagedDisableResult = await DisableBulk(list2, playsetId, fd);
			if (unmanagedDisableResult.Failures != null)
			{
				failingMods.AddRange(unmanagedDisableResult.Failures);
			}
		}
		else
		{
			unmanagedDisableResult = new BulkResult
			{
				Success = true
			};
		}
		List<string> list3 = (from m in allMods
			where m is PlaysetWipMod
			select (m as PlaysetWipMod).Id).ToList();
		BulkResult bulkResult;
		if (list3.Count > 0)
		{
			bulkResult = await DisableBulk(list3, playsetId, fd);
			if (bulkResult.Failures != null)
			{
				failingMods.AddRange(bulkResult.Failures);
			}
		}
		else
		{
			bulkResult = new BulkResult
			{
				Success = true
			};
		}
		return new BulkResult
		{
			Success = (managedDisableResult.Success && unmanagedDisableResult.Success && bulkResult.Success),
			Error = (managedDisableResult.Error ?? unmanagedDisableResult.Error ?? bulkResult.Error),
			Failures = failingMods
		};
	}

	internal async Task<Result> DownloadNewPdxMod(GetDetailsResult modDetails, int modId, string version, int playsetId, FlowData flowData = null)
	{
		_context.HTTP.RemoveOngoingTransfer(ETransferType.Download, ETransferServiceType.Mods, modId.ToString());
		DownloadResult downloadResult = await _modsDownloaderService.Download(modDetails.Mod.ToModDownloadMetadata(), doNotSetTransferStateToSuccessful: true, useFinalDirAsTemp: false, flowData).ConfigureAwait(continueOnCapturedContext: false);
		if (!downloadResult.Success)
		{
			_context.Log($"The mod {modId} could not be downloaded to disk", LogLevel.L1_Debug, flowData);
			(_context.Events as InternalEventHub).Publish(new ModDownloadFailed
			{
				EventId = Guid.NewGuid(),
				ModId = modId
			});
			return downloadResult;
		}
		Mod mod = modDetails.Mod.ToMod();
		mod.Version = version;
		mod.InstalledDate = DateTime.Now;
		ModsCacheError modsCacheError = await _modsCache.CacheNewSubscribedMod(mod, downloadResult, playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (!modsCacheError.Success)
		{
			return new Result
			{
				Success = false,
				Error = modsCacheError.Error
			};
		}
		return Result.ResultSuccessful();
	}

	internal async Task<bool> SetSubscribedModEnabledStateInPlayset(int modId, CachedPlaysetMetaData playset, bool enabledState)
	{
		PlaysetSubscribedModMetaData playsetSubscribedModMetaData = playset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData sm) => sm.Id == modId);
		if (playsetSubscribedModMetaData == null)
		{
			return false;
		}
		playsetSubscribedModMetaData.IsEnabled = enabledState;
		if (!(await playset.SubscribedModsTracked.TryUpdate(playsetSubscribedModMetaData.GetKey(), playsetSubscribedModMetaData).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		playset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		return true;
	}

	internal async Task<bool> SetWipModEnabledStateInPlayset(string modName, CachedPlaysetMetaData playset, bool enabledState)
	{
		PlaysetWipModMetaData valueOrNull = playset.WipModsTracked.GetValueOrNull(modName);
		if (valueOrNull == null)
		{
			return false;
		}
		valueOrNull.IsEnabled = enabledState;
		if (!(await playset.WipModsTracked.TryUpdate(valueOrNull.GetKey(), valueOrNull).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		await playset.WipModsTracked.ResetDirty(modName).ConfigureAwait(continueOnCapturedContext: false);
		playset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		return true;
	}

	internal async Task<bool> SetUnmanagedModEnabledStateInPlayset(string modPath, CachedPlaysetMetaData playset, bool enabledState)
	{
		PlaysetUnmanagedModMetaData valueOrNull = playset.UnmanagedModsTracked.GetValueOrNull(modPath);
		if (valueOrNull == null)
		{
			return false;
		}
		valueOrNull.IsEnabled = enabledState;
		if (!(await playset.UnmanagedModsTracked.TryUpdate(valueOrNull.GetKey(), valueOrNull).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		await playset.UnmanagedModsTracked.ResetDirty(modPath).ConfigureAwait(continueOnCapturedContext: false);
		playset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		return true;
	}

	internal async Task<Result> UpdateAndPersistPlaysetMetadata(CachedPlaysetMetaData playset, bool synced = false, bool localChange = false)
	{
		bool isPlaysetDirty = await _modsCache.IsPlaysetDirty(playset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		bool updatePlayset = await _modsCache.UpdatePlayset(playset.GetKey(), playset).ConfigureAwait(continueOnCapturedContext: false);
		if (synced || (localChange && !isPlaysetDirty))
		{
			await _modsCache.ResetAllDirtyInPlayset(playset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new Result
		{
			Success = updatePlayset,
			Error = (updatePlayset ? null : new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, $"The playset {playset.GeneralDataTracked.TrackedObject.Id} could not be updated locally", ""))
		};
	}
}
