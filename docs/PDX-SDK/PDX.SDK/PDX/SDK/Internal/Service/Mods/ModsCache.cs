using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Internal;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Contracts.Service.Mods.Enums;
using PDX.SDK.Contracts.Service.Mods.Models;
using PDX.SDK.Contracts.Service.Mods.Result;
using PDX.SDK.Internal.Errors;
using PDX.SDK.Internal.Mapper;
using PDX.SDK.Internal.Results;
using PDX.SDK.Internal.Service.Mods.Helpers;
using PDX.SDK.Internal.Service.Mods.Models;
using PDX.SDK.Internal.Util;
using TinyJson;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class ModsCache : IModsCache
{
	private readonly string _namespace;

	private readonly IPathFactory _pathFactory;

	private readonly IFileIO _fileIo;

	private readonly ILogger _logger;

	private readonly AsyncReaderWriterLock _readWriteLock = new AsyncReaderWriterLock();

	private ChangeTrackedConcurrentDictionary<CachedPlaysetMetaData> _playsets;

	private ChangeTrackedConcurrentDictionary<IMod> _mods;

	private PlaysetSettings _playsetSettings;

	[Preserve]
	internal ModsCache(string @namespace, IPathFactory pathFactory, IFileIO fileIo, ILogger logger = null)
	{
		_namespace = @namespace;
		_pathFactory = pathFactory;
		_fileIo = fileIo;
		_logger = logger;
		_playsets = new ChangeTrackedConcurrentDictionary<CachedPlaysetMetaData>(new JournalManager("playsets", pathFactory, fileIo, logger));
		_mods = new ChangeTrackedConcurrentDictionary<IMod>(new JournalManager("mods", pathFactory, fileIo, logger));
		_playsetSettings = new PlaysetSettings();
	}

	public async Task Init()
	{
		await _fileIo.CreateDirectory(_pathFactory.GetModsPath()).ConfigureAwait(continueOnCapturedContext: false);
		await _fileIo.CreateDirectory(_pathFactory.GetModsPath("", LocalType.WorkInProgress)).ConfigureAwait(continueOnCapturedContext: false);
		await _fileIo.CreateDirectory(_pathFactory.GetModsPath("", LocalType.Unmanaged)).ConfigureAwait(continueOnCapturedContext: false);
		await MoveMetadataFilesToModsFolder().ConfigureAwait(continueOnCapturedContext: false);
		await LoadModsCache().ConfigureAwait(continueOnCapturedContext: false);
		await LoadPlaysetsCache().ConfigureAwait(continueOnCapturedContext: false);
		await LoadActivePlayset().ConfigureAwait(continueOnCapturedContext: false);
		await UpdateSubscribedModsFolderToUniformStructure().ConfigureAwait(continueOnCapturedContext: false);
	}

	public bool HasAnyDirty()
	{
		if (!_playsets.HasDirty())
		{
			return _mods.HasDirty();
		}
		return true;
	}

	public async Task<ChangeTrackedConcurrentDictionary<CachedPlaysetMetaData>> GetAllPlaysets()
	{
		return await LockWhileReadingCache(() => _playsets).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<CachedPlaysetMetaData> GetPlaysetOrNull(int key)
	{
		return await LockWhileReadingCache(delegate
		{
			CachedPlaysetMetaData valueOrNull = _playsets.GetValueOrNull(key.ToString());
			if (valueOrNull == null)
			{
				valueOrNull = _playsets.GetValueOrNull(key.GetLocalPlaysetKey());
			}
			return valueOrNull;
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ChangeTrackedConcurrentDictionary<IMod>> GetAllMods()
	{
		return await LockWhileReadingCache(() => _mods).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<Mod>> GetAllModsWithPlaysetData()
	{
		List<IMod> list = (await GetAllMods().ConfigureAwait(continueOnCapturedContext: false)).ToList();
		List<Mod> allModsWithPlaysetData = new List<Mod>();
		foreach (IMod item in list)
		{
			List<Mod> list2 = allModsWithPlaysetData;
			list2.Add(await GetModOrNull(item.GetKey()).ConfigureAwait(continueOnCapturedContext: false));
		}
		return allModsWithPlaysetData;
	}

	public async Task<List<IMod>> GetModsInPlayset(int playsetId)
	{
		List<IMod> modsInPlayset = new List<IMod>();
		CachedPlaysetMetaData cachedPlaysetMetaData = await GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlaysetMetaData == null)
		{
			return null;
		}
		modsInPlayset.AddRange(cachedPlaysetMetaData.SubscribedModsTracked.Select((PlaysetSubscribedModMetaData sm) => _mods.FirstOrDefault((IMod mod) => mod.GetKey() == sm.GetKey())).ToList());
		modsInPlayset.AddRange(cachedPlaysetMetaData.WipModsTracked.Select((PlaysetWipModMetaData sm) => _mods.FirstOrDefault((IMod mod) => mod.GetKey() == sm.GetKey())).ToList());
		modsInPlayset.AddRange(cachedPlaysetMetaData.UnmanagedModsTracked.Select((PlaysetUnmanagedModMetaData sm) => _mods.FirstOrDefault((IMod mod) => mod.GetKey() == sm.GetKey())).ToList());
		return modsInPlayset.Where((IMod m) => m != null).ToList();
	}

	public async Task<List<CachedPlaysetMetaData>> GetSubscribedModPlaysets(int modId, string version = null)
	{
		return await LockWhileReadingCache(() => _playsets.Where((CachedPlaysetMetaData p) => p.SubscribedModsTracked.Any((PlaysetSubscribedModMetaData m) => m.GetKey() == modId.GetKey(version))).ToList()).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<CachedPlaysetMetaData>> GetWipModPlaysets(string name)
	{
		return await LockWhileReadingCache(() => _playsets.Where((CachedPlaysetMetaData p) => p.WipModsTracked.Any((PlaysetWipModMetaData m) => m.GetKey() == name)).ToList()).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<List<CachedPlaysetMetaData>> GetUnmanagedModPlaysets(string path)
	{
		return await LockWhileReadingCache(() => _playsets.Where((CachedPlaysetMetaData p) => p.UnmanagedModsTracked.Any((PlaysetUnmanagedModMetaData m) => m.GetKey() == path)).ToList()).ConfigureAwait(continueOnCapturedContext: false);
	}

	public bool IsModInPlayset(CachedPlaysetMetaData playset, string modKey)
	{
		if (!playset.SubscribedModsTracked.Any((PlaysetSubscribedModMetaData m) => m.GetKey() == modKey) && !playset.WipModsTracked.Any((PlaysetWipModMetaData m) => m.GetKey() == modKey))
		{
			return playset.UnmanagedModsTracked.Any((PlaysetUnmanagedModMetaData m) => m.GetKey() == modKey);
		}
		return true;
	}

	public async Task<Mod> GetModOrNull(string key)
	{
		Mod mod = (await LockWhileReadingCache(() => _mods.GetValueOrNull(key)).ConfigureAwait(continueOnCapturedContext: false)) as Mod;
		if (mod == null)
		{
			return mod;
		}
		LocalData localData = mod.LocalData;
		if (localData != null && localData.LocalType == LocalType.Subscribed)
		{
			List<CachedPlaysetMetaData> source = await GetSubscribedModPlaysets(mod.Id, mod.Version).ConfigureAwait(continueOnCapturedContext: false);
			mod.Playsets = source.Select((CachedPlaysetMetaData p) => p.ToPlaysetInSubscribedMod(mod.GetKey())).ToList();
		}
		else
		{
			LocalData localData2 = mod.LocalData;
			if (localData2 != null && localData2.LocalType == LocalType.WorkInProgress)
			{
				List<CachedPlaysetMetaData> source2 = await GetWipModPlaysets(mod.Name).ConfigureAwait(continueOnCapturedContext: false);
				mod.Playsets = source2.Select((CachedPlaysetMetaData p) => p.ToPlaysetInWipMod(mod.GetKey())).ToList();
			}
			else
			{
				LocalData localData3 = mod.LocalData;
				if (localData3 != null && localData3.LocalType == LocalType.Unmanaged)
				{
					List<CachedPlaysetMetaData> source3 = await GetUnmanagedModPlaysets(mod.Name).ConfigureAwait(continueOnCapturedContext: false);
					mod.Playsets = source3.Select((CachedPlaysetMetaData p) => p.ToPlaysetInUnmanagedMod(mod.GetKey())).ToList();
				}
			}
		}
		return mod;
	}

	public async Task ResetAllDirtyInPlayset(string key)
	{
		await LockWhileAccessingFile(async delegate
		{
			await _playsets.ResetDirty(key).ConfigureAwait(continueOnCapturedContext: false);
			CachedPlaysetMetaData dirtyPlayset = _playsets.GetValueOrNull(key);
			await dirtyPlayset.GeneralDataTracked.ResetIsDirty().ConfigureAwait(continueOnCapturedContext: false);
			await dirtyPlayset.SubscribedModsTracked.ResetAllDirty().ConfigureAwait(continueOnCapturedContext: false);
			await dirtyPlayset.WipModsTracked.ResetAllDirty().ConfigureAwait(continueOnCapturedContext: false);
			await dirtyPlayset.UnmanagedModsTracked.ResetAllDirty().ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> IsPlaysetDirty(string key)
	{
		return await LockWhileReadingCache(() => _playsets.GetAllDirtyWithoutTracking().Any((KeyValuePair<string, CachedPlaysetMetaData> p) => p.Key == key)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task ResetDirtyMod(string key)
	{
		await LockWhileAccessingFile(async delegate
		{
			await _mods.ResetDirty(key).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> AddPlayset(string key, CachedPlaysetMetaData playsetMetaData, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		if (!(await _playsets.TryAdd(key, playsetMetaData).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		PlaysetMetaData playsetDataToPersist = new PlaysetMetaData
		{
			GeneralData = playsetMetaData.GeneralDataTracked?.TrackedObject,
			SubscribedMods = playsetMetaData.SubscribedModsTracked?.GetAllWithoutTracking(),
			WipMods = playsetMetaData.WipModsTracked?.GetAllWithoutTracking(),
			UnmanagedMods = playsetMetaData.UnmanagedModsTracked?.GetAllWithoutTracking()
		};
		await LockWhileAccessingFile(async delegate
		{
			await _fileIo.WriteJSON(_pathFactory.GetModPlaysetsMetadataPath(key), playsetDataToPersist).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public async Task<bool> AddPlaysetWithAutoGeneratedPlaysetId(CachedPlaysetMetaData playsetMetaData, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		await _readWriteLock.AcquireWriterLock(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			int id = CreateNewLocalPlaysetIdWithoutTakingLock(cancellationToken);
			string newPlaysetKey = id.GetLocalPlaysetKey();
			await playsetMetaData.UpdatePlaysetId(id).ConfigureAwait(continueOnCapturedContext: false);
			if (!(await _playsets.TryAdd(newPlaysetKey, playsetMetaData).ConfigureAwait(continueOnCapturedContext: false)))
			{
				_logger?.Log("Could not add the playset with auto-generated id to cache");
				return false;
			}
			PlaysetMetaData data = new PlaysetMetaData
			{
				GeneralData = playsetMetaData.GeneralDataTracked?.TrackedObject,
				SubscribedMods = playsetMetaData.SubscribedModsTracked?.GetAllWithoutTracking(),
				WipMods = playsetMetaData.WipModsTracked?.GetAllWithoutTracking(),
				UnmanagedMods = playsetMetaData.UnmanagedModsTracked?.GetAllWithoutTracking()
			};
			await _fileIo.WriteJSON(_pathFactory.GetModPlaysetsMetadataPath(newPlaysetKey), data).ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_readWriteLock.ReleaseWriterLock();
		}
		return true;
	}

	public async Task<bool> UpdatePlayset(string key, CachedPlaysetMetaData playsetMetaData, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		if (!(await _playsets.TryUpdate(key, playsetMetaData).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		PlaysetMetaData playsetDataToPersist = new PlaysetMetaData
		{
			GeneralData = playsetMetaData.GeneralDataTracked?.TrackedObject,
			SubscribedMods = playsetMetaData.SubscribedModsTracked?.GetAllWithoutTracking(),
			WipMods = playsetMetaData.WipModsTracked?.GetAllWithoutTracking(),
			UnmanagedMods = playsetMetaData.UnmanagedModsTracked?.GetAllWithoutTracking()
		};
		await LockWhileAccessingFile(async delegate
		{
			await _fileIo.WriteJSON(_pathFactory.GetModPlaysetsMetadataPath(key), playsetDataToPersist).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public async Task<bool> UpdatePlaysetId(string key, int newPlaysetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		CachedPlaysetMetaData playsetToUpdate = _playsets.GetValueOrNull(key);
		await playsetToUpdate.UpdatePlaysetId(newPlaysetId).ConfigureAwait(continueOnCapturedContext: false);
		if (!(await AddPlayset(newPlaysetId.ToString(), playsetToUpdate, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		if (!(await _playsets.TryRemove(key).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		await LockWhileAccessingFile(async delegate
		{
			await _fileIo.Delete(_pathFactory.GetModPlaysetsMetadataPath(key)).ConfigureAwait(continueOnCapturedContext: false);
		}).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public async Task<ModsCacheError> RemovePlayset(string key, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<ModsCacheError>();
		}
		CachedPlaysetMetaData valueOrNull = _playsets.GetValueOrNull(key);
		if (valueOrNull == null)
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, "The playset " + key + " could not be found", string.Empty)
			};
		}
		await valueOrNull.CleanUpJournal().ConfigureAwait(continueOnCapturedContext: false);
		if (!(await _playsets.TryRemove(key).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The playset could not be deleted from cache", "")
			};
		}
		FileIOStatus fileIOStatus = await LockWhileAccessingFile(async () => await _fileIo.Delete(_pathFactory.GetModPlaysetsMetadataPath(key)).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return new ModsCacheError(fileIOStatus.ToResult());
		}
		return new ModsCacheError
		{
			Success = true
		};
	}

	public async Task<Result> LoadPlaysetsCache(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false);
		}
		return await LockWhileAccessingFile(async delegate
		{
			_playsets.Clear();
			OperationResult<List<string>> operationResult = await _fileIo.ListFiles(_pathFactory.GetModPlaysetsMetadataPath()).ConfigureAwait(continueOnCapturedContext: false);
			if (!operationResult.Status.Success)
			{
				return operationResult.Status.ToResult();
			}
			ConcurrentDictionary<string, CachedPlaysetMetaData> playsets = new ConcurrentDictionary<string, CachedPlaysetMetaData>();
			bool isSuccessfullyLoaded = (await Task.WhenAll(operationResult.Result.Select(async delegate(string f)
			{
				string key = Path.GetFileName(f);
				string result;
				try
				{
					OperationResult<string> operationResult2 = await _fileIo.ReadText(_pathFactory.GetModPlaysetsMetadataPath(key)).ConfigureAwait(continueOnCapturedContext: false);
					if (!operationResult2.Status.Success)
					{
						return false;
					}
					result = operationResult2.Result;
				}
				catch (Exception ex)
				{
					_logger?.Log(ex.Message, LogLevel.L3_Error);
					return false;
				}
				PlaysetMetaData playsetMetaData = result.FromJson<PlaysetMetaData>();
				ConcurrentDictionary<string, CachedPlaysetMetaData> concurrentDictionary = playsets;
				string key2 = key;
				return concurrentDictionary.TryAdd(key2, await CachedPlaysetMetaData.Create(playsetMetaData.GeneralData, playsetMetaData.SubscribedMods, playsetMetaData.WipMods, playsetMetaData.UnmanagedMods, _pathFactory, _fileIo, _logger).ConfigureAwait(continueOnCapturedContext: false));
			})).ConfigureAwait(continueOnCapturedContext: false)).All((bool r) => r);
			if (isSuccessfullyLoaded)
			{
				_playsets = new ChangeTrackedConcurrentDictionary<CachedPlaysetMetaData>(playsets.ToDictionary((KeyValuePair<string, CachedPlaysetMetaData> x) => x.Key, (KeyValuePair<string, CachedPlaysetMetaData> x) => x.Value), new JournalManager("playsets", _pathFactory, _fileIo, _logger));
				await _playsets.Initialize().ConfigureAwait(continueOnCapturedContext: false);
			}
			return new Result
			{
				Success = isSuccessfullyLoaded,
				Error = (isSuccessfullyLoaded ? null : new Error(InternalError.InternalError, "Not all Playset files could be loaded.", string.Empty))
			};
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<IMod> GetSubscribedMod(int id, string version)
	{
		string key = id.GetModKey(version);
		IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(key)).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData?.LocalType != LocalType.Subscribed)
		{
			return null;
		}
		return mod;
	}

	public async Task<IMod> GetWipMod(string guid)
	{
		IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(guid)).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData?.LocalType != LocalType.WorkInProgress)
		{
			return null;
		}
		return mod;
	}

	public async Task<IMod> GetUnmanagedMod(string path)
	{
		IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(path)).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null || mod.LocalData?.LocalType != LocalType.Unmanaged)
		{
			return null;
		}
		return mod;
	}

	public async Task<ModsCacheError> AddMod(IMod mod, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await AddMod(mod, -1, cancellationToken);
	}

	public async Task<ModsCacheError> AddMod(IMod mod, int playsetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return new ModsCacheError
			{
				Success = false
			};
		}
		if (!(await _mods.TryAdd(mod.GetKey(), mod).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The mod already exists locally", "")
			};
		}
		if (int.TryParse(mod.Version, out var result))
		{
			string modKeyToRemove = "";
			string modKeyToAdd = mod.GetKey();
			List<string> list = new List<string>();
			foreach (CachedPlaysetMetaData playset in _playsets)
			{
				List<PlaysetSubscribedModMetaData> list2 = playset.SubscribedModsTracked.ToList();
				if (playsetId != -1 && playset.GetKey() != playsetId.ToString())
				{
					continue;
				}
				foreach (PlaysetSubscribedModMetaData item in list2)
				{
					if (int.TryParse(item.Version, out var result2) && item.Id == mod.Id && result2 < result)
					{
						modKeyToRemove = item.GetKey();
						list.Add(playset.GetKey());
					}
				}
			}
			if (!string.IsNullOrEmpty(modKeyToAdd) && !string.IsNullOrEmpty(modKeyToRemove))
			{
				foreach (string item2 in list)
				{
					CachedPlaysetMetaData playsetMetadata = _playsets.GetValueOrNull(item2);
					if (playsetMetadata != null)
					{
						PlaysetSubscribedModMetaData modMetaDataToRemove = playsetMetadata.SubscribedModsTracked.GetValueOrNull(modKeyToRemove);
						if (modMetaDataToRemove != null)
						{
							await playsetMetadata.SubscribedModsTracked.TryRemove(modKeyToRemove).ConfigureAwait(continueOnCapturedContext: false);
							await playsetMetadata.SubscribedModsTracked.TryAdd(modKeyToAdd, new PlaysetSubscribedModMetaData
							{
								Id = mod.Id,
								Version = mod.Version,
								LoadOrder = modMetaDataToRemove.LoadOrder
							}).ConfigureAwait(continueOnCapturedContext: false);
						}
					}
				}
			}
		}
		FileIOStatus fileIOStatus = await LockWhileAccessingFile(async () => await _fileIo.WriteJSON(_pathFactory.GetModsMetadataPath(mod.LocalData.FolderAbsolutePath, mod.LocalData.LocalType), mod).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return fileIOStatus.ToResult<ModsCacheError>();
		}
		if (!mod.IsSubscribedMod())
		{
			await ResetDirtyMod(mod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new ModsCacheError
		{
			Success = true
		};
	}

	public async Task<ModsCacheError> UpdateMod(IMod mod, CancellationToken cancellationToken = default(CancellationToken))
	{
		return await UpdateMod(mod, -1, cancellationToken);
	}

	public async Task<ModsCacheError> UpdateMod(IMod mod, int playsetId, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return new ModsCacheError
			{
				Success = false
			};
		}
		if (!(await _mods.TryUpdate(mod.GetKey(), mod).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new ModsCacheError
			{
				Success = false
			};
		}
		FileIOStatus fileIOStatus = await LockWhileAccessingFile(async () => await _fileIo.WriteJSON(_pathFactory.GetModsMetadataPath(mod.LocalData.FolderAbsolutePath, mod.LocalData.LocalType), mod).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return fileIOStatus.ToResult<ModsCacheError>();
		}
		if (!mod.IsSubscribedMod())
		{
			await ResetDirtyMod(mod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return new ModsCacheError
		{
			Success = true
		};
	}

	public async Task<ModsCacheError> DeleteMod(string key, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return new ModsCacheError
			{
				Success = false
			};
		}
		IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(key)).ConfigureAwait(continueOnCapturedContext: false);
		if (mod != null && _fileIo.PathExists(_pathFactory.GetModsMetadataPath(mod.LocalData.FolderAbsolutePath, mod.LocalData.LocalType)))
		{
			await _fileIo.Delete(_pathFactory.GetModsMetadataPath(mod.LocalData.FolderAbsolutePath, mod.LocalData.LocalType)).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (!(await _mods.TryRemove(key).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The mod could not be deleted from cache", "")
			};
		}
		return new ModsCacheError
		{
			Success = true
		};
	}

	public async Task<Result> LoadModsCache(CancellationToken cancellationToken = default(CancellationToken))
	{
		await LockWhileAccessingFile(delegate
		{
			_mods.Clear();
		}).ConfigureAwait(continueOnCapturedContext: false);
		LoadModsResult loadModsResult = await GetSubscribedAndWipModsFromDisk(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!loadModsResult.Success)
		{
			Dictionary<string, IMod> mods = loadModsResult.Mods;
			if (mods != null && mods.Count == 0)
			{
				return loadModsResult;
			}
		}
		Dictionary<string, IMod> loadedMods = new Dictionary<string, IMod>(loadModsResult.Mods);
		LoadModsResult loadModsResult2 = await GetUnmanagedModsFromDisk(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!loadModsResult2.Success)
		{
			Dictionary<string, IMod> mods2 = loadModsResult2.Mods;
			if (mods2 != null && mods2.Count == 0)
			{
				return loadModsResult2;
			}
		}
		foreach (KeyValuePair<string, IMod> mod in loadModsResult2.Mods)
		{
			if (!loadedMods.ContainsKey(mod.Key))
			{
				loadedMods.Add(mod.Key, mod.Value);
			}
		}
		_mods = new ChangeTrackedConcurrentDictionary<IMod>(loadedMods, new JournalManager("mods", _pathFactory, _fileIo, _logger));
		await _mods.Initialize().ConfigureAwait(continueOnCapturedContext: false);
		Dictionary<string, IMod> allDirtyWithoutTracking = _mods.GetAllDirtyWithoutTracking();
		foreach (IMod value in allDirtyWithoutTracking.Values)
		{
			LocalData localData = value.LocalData;
			if (localData == null || localData.LocalType != LocalType.Subscribed)
			{
				await ResetDirtyMod(value.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		return new Result
		{
			Success = true
		};
	}

	public async Task<LoadModsResult> GetSubscribedAndWipModsFromDisk(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<LoadModsResult>();
		}
		return await LockWhileAccessingFile(async delegate
		{
			List<string> listModsFolders = new List<string>();
			OperationResult<List<string>> subscribedModsResult = await _fileIo.ListDirectories(_pathFactory.GetModsPath());
			if (subscribedModsResult.Status.Success && subscribedModsResult.Result.Count > 0)
			{
				listModsFolders.AddRange(subscribedModsResult.Result);
			}
			OperationResult<List<string>> operationResult = await _fileIo.ListDirectories(_pathFactory.GetModsPath("", LocalType.WorkInProgress));
			if (operationResult.Status.Success && operationResult.Result.Count > 0)
			{
				listModsFolders.AddRange(operationResult.Result);
			}
			if (listModsFolders.Count == 0)
			{
				bool flag = subscribedModsResult.Status.Success && operationResult.Status.Success;
				return new LoadModsResult
				{
					Success = flag,
					Error = (flag ? null : new Error(InternalError.InternalError, "Not all Mods metadata files could be loaded.", string.Empty)),
					Mods = new Dictionary<string, IMod>()
				};
			}
			ConcurrentDictionary<string, IMod> mods = new ConcurrentDictionary<string, IMod>();
			bool[] source = await Task.WhenAll(listModsFolders.Select(async delegate(string f)
			{
				string path = Path.Combine(f, ".metadata", "metadata.json");
				string result;
				try
				{
					OperationResult<string> operationResult2 = await _fileIo.ReadText(path).ConfigureAwait(continueOnCapturedContext: false);
					if (!operationResult2.Status.Success)
					{
						return false;
					}
					result = operationResult2.Result;
				}
				catch (Exception ex)
				{
					_logger?.Log(ex.Message, LogLevel.L3_Error);
					return false;
				}
				Mod mod = result.FromJson<Mod>();
				string text = mod?.GetKey();
				if (!string.IsNullOrEmpty(text))
				{
					if (mod.LocalData != null && mod.LocalData.FolderAbsolutePath != f)
					{
						mod.LocalData.FolderAbsolutePath = f;
					}
					return mods.TryAdd(text, mod);
				}
				return false;
			})).ConfigureAwait(continueOnCapturedContext: false);
			return new LoadModsResult
			{
				Success = source.All((bool r) => r),
				Error = (source.All((bool r) => r) ? null : new Error(InternalError.InternalError, "Not all Mods metadata files could be loaded.", string.Empty)),
				Mods = mods.ToDictionary((KeyValuePair<string, IMod> x) => x.Key, (KeyValuePair<string, IMod> x) => x.Value)
			};
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<LoadModsResult> GetUnmanagedModsFromDisk(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<LoadModsResult>();
		}
		return await LockWhileAccessingFile(async delegate
		{
			OperationResult<List<string>> operationResult = await GetUnmanageModsFilePaths().ConfigureAwait(continueOnCapturedContext: false);
			if (!operationResult.Status.Success)
			{
				return operationResult.Status.ToResult<LoadModsResult>();
			}
			KeyValuePair<string, IMod>[] source = await Task.WhenAll(operationResult.Result.Select(async delegate(string modFilePath)
			{
				string fileName = Path.GetFileName(modFilePath);
				Mod modData = null;
				string path = Path.Combine(modFilePath, ".metadata", "metadata.json");
				if (_fileIo.PathExists(path))
				{
					try
					{
						OperationResult<string> operationResult2 = await _fileIo.ReadText(path).ConfigureAwait(continueOnCapturedContext: false);
						if (operationResult2.Status.Success)
						{
							modData = operationResult2.Result.FromJson<Mod>();
							if (modData.LocalData == null)
							{
								modData.LocalData = new LocalData
								{
									FolderAbsolutePath = modFilePath,
									ContentFileOrFolder = fileName
								};
							}
							modData.LocalData.LocalType = LocalType.Unmanaged;
						}
					}
					catch (Exception ex)
					{
						_logger?.Log(ex.Message, LogLevel.L2_Warning);
					}
				}
				if (modData == null)
				{
					modData = new Mod
					{
						Name = fileName,
						DisplayName = fileName,
						LocalData = new LocalData
						{
							FolderAbsolutePath = modFilePath,
							ContentFileOrFolder = fileName,
							LocalType = LocalType.Unmanaged
						}
					};
				}
				return new KeyValuePair<string, IMod>(modData.GetKey(), modData);
			})).ConfigureAwait(continueOnCapturedContext: false);
			return new LoadModsResult
			{
				Success = true,
				Mods = source.ToDictionary((KeyValuePair<string, IMod> x) => x.Key, (KeyValuePair<string, IMod> x) => x.Value)
			};
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task UpdateSubscribedModsFolderToUniformStructure()
	{
		List<IMod> updatedMods = new List<IMod>();
		await LockWhileAccessingFile(async delegate
		{
			foreach (IMod mod in _mods)
			{
				LocalData localData = mod.LocalData;
				if (localData != null && localData.LocalType == LocalType.Subscribed)
				{
					LocalData modLocalData = mod.LocalData;
					string oldContentFolderPath = Path.Combine(modLocalData.FolderAbsolutePath, "modassets");
					if (_fileIo.PathExists(oldContentFolderPath))
					{
						string metadataFolderPath = Path.Combine(modLocalData.FolderAbsolutePath, ".metadata");
						if (_fileIo.PathExists(metadataFolderPath) || (await _fileIo.CreateDirectory(metadataFolderPath).ConfigureAwait(continueOnCapturedContext: false)).Success)
						{
							bool overallMoveSuccessful = true;
							foreach (string item in (await _fileIo.ListFiles(modLocalData.FolderAbsolutePath).ConfigureAwait(continueOnCapturedContext: false)).Result)
							{
								string fileName = Path.GetFileName(item);
								if (!(fileName == modLocalData.ContentFileOrFolder))
								{
									string text = Path.Combine(metadataFolderPath, fileName);
									bool flag = ((!_fileIo.PathExists(text)) ? (await _fileIo.Move(item, text).ConfigureAwait(continueOnCapturedContext: false)).Success : (await _fileIo.Delete(item).ConfigureAwait(continueOnCapturedContext: false)).Success);
									overallMoveSuccessful = overallMoveSuccessful && flag;
									if (!flag)
									{
										break;
									}
									if (fileName == modLocalData.ThumbnailFilename)
									{
										modLocalData.ThumbnailFilename = Path.Combine(".metadata", fileName);
									}
									else
									{
										int num = modLocalData.ScreenshotsFilenames.FindIndex((string s) => s == fileName);
										if (num != -1)
										{
											modLocalData.ScreenshotsFilenames[num] = Path.Combine(".metadata", fileName);
										}
									}
								}
							}
							if (overallMoveSuccessful)
							{
								List<Task<FileIOStatus>> moveTasks = new List<Task<FileIOStatus>>();
								List<string> contentAssets = new List<string>();
								OperationResult<List<string>> operationResult = await _fileIo.ListFiles(oldContentFolderPath).ConfigureAwait(continueOnCapturedContext: false);
								if (operationResult.Status.Success)
								{
									contentAssets.AddRange(operationResult.Result);
								}
								OperationResult<List<string>> operationResult2 = await _fileIo.ListDirectories(oldContentFolderPath).ConfigureAwait(continueOnCapturedContext: false);
								if (operationResult2.Status.Success)
								{
									contentAssets.AddRange(operationResult2.Result);
								}
								foreach (string item2 in contentAssets)
								{
									string text2 = Path.Combine(modLocalData.FolderAbsolutePath, Path.GetFileName(item2));
									if (!_fileIo.PathExists(text2))
									{
										moveTasks.Add(_fileIo.Move(item2, text2));
									}
								}
								if ((await Task.WhenAll(moveTasks).ConfigureAwait(continueOnCapturedContext: false)).All((FileIOStatus r) => r.Success))
								{
									modLocalData.ContentFileOrFolder = ((contentAssets.Count == 1) ? Path.GetFileName(contentAssets[0]) : string.Empty);
									await _fileIo.DeleteDirectory(oldContentFolderPath, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
								}
								updatedMods.Add(mod);
							}
						}
					}
				}
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		foreach (IMod item3 in updatedMods)
		{
			await UpdateMod(item3).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	internal async Task MoveMetadataFilesToModsFolder()
	{
		string oldMetadataFolderPath = _pathFactory.GetModsRootPath("mods_metadata");
		if (_fileIo.PathExists(oldMetadataFolderPath) && await LockWhileAccessingFile(async delegate
		{
			OperationResult<List<string>> operationResult = await _fileIo.ListFiles(oldMetadataFolderPath).ConfigureAwait(continueOnCapturedContext: false);
			return operationResult.Status.Success && (await Task.WhenAll(operationResult.Result.Select(async delegate(string f)
			{
				string key = Path.GetFileName(f);
				try
				{
					Guid result;
					string targetPath = _pathFactory.GetModsMetadataPath(key, Guid.TryParse(key, out result) ? LocalType.WorkInProgress : LocalType.Subscribed);
					if (!_fileIo.PathExists(_fileIo.DiskIO.GetDirectoryName(targetPath)) && !(await _fileIo.CreateDirectory(_fileIo.DiskIO.GetDirectoryName(targetPath))).Success)
					{
						_logger.Log("Could not create the metadata folder for mod " + key, LogLevel.L2_Warning);
						return false;
					}
					FileIOStatus obj = await _fileIo.Move(f, targetPath);
					if (!obj.Success)
					{
						_logger.Log("The metadatafile " + key + " could not be moved to its respective mod folder", LogLevel.L2_Warning);
					}
					return obj.Success;
				}
				catch (Exception ex)
				{
					_logger?.Log(ex.Message, LogLevel.L3_Error);
					return false;
				}
			})).ConfigureAwait(continueOnCapturedContext: false)).All((bool r) => r);
		}))
		{
			await _fileIo.DeleteDirectory(oldMetadataFolderPath).ConfigureAwait(continueOnCapturedContext: false);
		}
	}

	public async Task<LoadUnmanagedModsResult> RefreshUnmanagedModsCache(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<LoadUnmanagedModsResult>();
		}
		LoadModsResult loadModsResult = await GetUnmanagedModsFromDisk(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!loadModsResult.Success && loadModsResult.Error.SubCategory.Equals(InternalError.FileIOError))
		{
			return loadModsResult.CastTo<LoadUnmanagedModsResult>();
		}
		LoadUnmanagedModsResult result = await RefreshUnmanagedModsCacheInner(loadModsResult.Mods.Values.ToList()).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result;
		}
		Dictionary<IMod, IEnumerable<CachedPlaysetMetaData>> dictionary = result.ModsRemoved.ToDictionary((IMod m) => m, (IMod m) => from x in _playsets.GetAllWithoutTracking()
			where x.Value.UnmanagedModsTracked.Any((PlaysetUnmanagedModMetaData playsetUnmanagedMod) => m.GetKey() == playsetUnmanagedMod.GetKey())
			select x.Value);
		foreach (KeyValuePair<IMod, IEnumerable<CachedPlaysetMetaData>> row in dictionary)
		{
			foreach (CachedPlaysetMetaData cachedPlayset in row.Value)
			{
				bool isPlaysetDirty = await IsPlaysetDirty(cachedPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
				if (!(await RemoveModFromPlayset(row.Key.GetKey(), cachedPlayset, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
				{
					result.Success = false;
				}
				if (!(await UpdatePlayset(cachedPlayset.GetKey(), cachedPlayset, cancellationToken).ConfigureAwait(continueOnCapturedContext: false)))
				{
					_logger?.Log("Failed to persist playset to disk", LogLevel.L2_Warning);
					result.Success = false;
				}
				if (!isPlaysetDirty)
				{
					await ResetAllDirtyInPlayset(cachedPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		return result;
	}

	private async Task<LoadUnmanagedModsResult> RefreshUnmanagedModsCacheInner(List<IMod> modsFromDisk, FlowData flowData = null)
	{
		FlowData fd = new FlowData(flowData);
		_logger?.Log("ModsCache.RefreshUnmanagedModsCacheInner", LogLevel.L1_Debug, fd);
		List<IMod> source = (from x in _mods.GetAllWithoutTracking()
			select x.Value).Where(delegate(IMod x)
		{
			LocalData localData = x.LocalData;
			return localData != null && localData.LocalType == LocalType.Unmanaged;
		}).ToList();
		List<IMod> modsAdded = modsFromDisk.Where((IMod m) => _mods.GetValueOrNull(m.GetKey()) == null).ToList();
		List<IMod> modsRemoved = source.Where((IMod m) => !modsFromDisk.Any((IMod mod) => mod.GetKey() == m.GetKey())).ToList();
		bool allWasAdded = true;
		foreach (IMod modAdded in modsAdded)
		{
			if (!(await _mods.TryAdd(modAdded.Name, modAdded).ConfigureAwait(continueOnCapturedContext: false)))
			{
				_logger?.Log("Failed to add mod " + modAdded.Name + " to cache", LogLevel.L2_Warning, fd);
				allWasAdded = false;
			}
			await ResetDirtyMod(modAdded.Name).ConfigureAwait(continueOnCapturedContext: false);
		}
		bool allWasRemoved = true;
		foreach (IMod modAdded in modsRemoved)
		{
			if (!(await _mods.TryRemove(modAdded.Name).ConfigureAwait(continueOnCapturedContext: false)))
			{
				_logger?.Log("Failed to remove mod " + modAdded.Name + " from cache", LogLevel.L2_Warning, fd);
				allWasRemoved = false;
			}
		}
		return new LoadUnmanagedModsResult
		{
			Success = (allWasAdded && allWasRemoved),
			ModsAdded = modsAdded.Cast<IMod>().ToList(),
			ModsRemoved = modsRemoved
		};
	}

	private async Task<OperationResult<List<string>>> GetUnmanageModsFilePaths()
	{
		string unmanagedModsFolderPath = _pathFactory.GetModsPath("", LocalType.Unmanaged);
		List<string> unmanagedModFilesAndFolders = new List<string>();
		OperationResult<List<string>> unmanagedModsDirectoryResult = await _fileIo.ListDirectories(unmanagedModsFolderPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!unmanagedModsDirectoryResult.Status.Success)
		{
			return new OperationResult<List<string>>
			{
				Status = unmanagedModsDirectoryResult.Status
			};
		}
		unmanagedModFilesAndFolders.AddRange(unmanagedModsDirectoryResult.Result);
		OperationResult<List<string>> operationResult = await _fileIo.ListFiles(unmanagedModsFolderPath).ConfigureAwait(continueOnCapturedContext: false);
		if (!operationResult.Status.Success)
		{
			return new OperationResult<List<string>>
			{
				Status = unmanagedModsDirectoryResult.Status
			};
		}
		unmanagedModFilesAndFolders.AddRange(operationResult.Result);
		return new OperationResult<List<string>>
		{
			Status = new FileIOStatus
			{
				Success = true
			},
			Result = unmanagedModFilesAndFolders
		};
	}

	public async Task<int> CreateNewLocalPlaysetId(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return -1;
		}
		await _readWriteLock.AcquireWriterLock(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		try
		{
			return CreateNewLocalPlaysetIdWithoutTakingLock(cancellationToken);
		}
		finally
		{
			_readWriteLock.ReleaseWriterLock();
		}
	}

	private int CreateNewLocalPlaysetIdWithoutTakingLock(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return -1;
		}
		string text = (from p in _playsets.GetAllWithoutTracking()
			where p.Key.Contains("local_")
			select p.Key.Split('_').Last() into k
			orderby k
			select k).LastOrDefault();
		int result;
		bool flag = int.TryParse(text, out result);
		if (!string.IsNullOrEmpty(text) && flag)
		{
			return result + 1;
		}
		return 1;
	}

	public async Task<bool> CreateLocalPlayset(string playsetName, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.CreateLocalPlayset", LogLevel.L0_Info, flowData2);
		if (string.IsNullOrEmpty(playsetName))
		{
			return false;
		}
		return await AddPlaysetWithAutoGeneratedPlaysetId(await CachedPlaysetMetaData.Create(new PlaysetGeneralMetaData
		{
			Id = -1,
			Name = playsetName,
			Updated = DateTime.Now,
			PlaysetModsLatestUpdated = DateTime.Now,
			Game = _namespace
		}, _pathFactory, _fileIo, _logger).ConfigureAwait(continueOnCapturedContext: false), cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> RenameLocalPlayset(int playsetId, string playsetName, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.RenameLocalPlayset", LogLevel.L0_Info, flowData2);
		if (string.IsNullOrEmpty(playsetName))
		{
			return false;
		}
		CachedPlaysetMetaData cachedPlaysetMetaData = await GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlaysetMetaData == null)
		{
			return false;
		}
		cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Name = playsetName;
		cachedPlaysetMetaData.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		return await UpdatePlayset(cachedPlaysetMetaData.GetKey(), cachedPlaysetMetaData).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModsCacheError> DeleteLocalPlayset(int playsetId, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<ModsCacheError>();
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.DeleteLocalPlayset", LogLevel.L0_Info, flowData2);
		CachedPlaysetMetaData cachedPlaysetMetaData = await GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlaysetMetaData == null)
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(NotFound.NoModPlayset, $"The playset {playsetId} could not be found", string.Empty)
			};
		}
		return await RemovePlayset(cachedPlaysetMetaData.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<ModsCacheError> CacheNewSubscribedMod(IMod mod, DownloadResult downloadResult, int playsetId)
	{
		mod.LocalData = new LocalData
		{
			LocalType = LocalType.Subscribed,
			FolderAbsolutePath = _pathFactory.GetModsPath(ModPath.GetModFolderName(mod.Id, mod.Version)),
			ContentFileOrFolder = downloadResult.ContentFileOrFolder,
			ThumbnailFilename = downloadResult.ThumbnailFilename,
			ScreenshotsFilenames = downloadResult.ScreenshotsFilenames
		};
		mod.LatestUpdate = DateTime.Now;
		ModsCacheError added = await AddMod(mod, playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (added.Success)
		{
			await ResetDirtyMod(mod.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		}
		return added;
	}

	public async Task<RegisterResult> RegisterWIP(string displayName, string shortDescription = null, string longDescription = null, ulong size = 0uL, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<RegisterResult>();
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.RegisterWIP", LogLevel.L1_Debug, flowData2);
		string modName = Guid.NewGuid().ToString();
		string path = _pathFactory.GetModsPath(modName, LocalType.WorkInProgress);
		FileIOStatus fileIOStatus = await _fileIo.CreateDirectory(path).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return new RegisterResult
			{
				Success = false,
				Error = new Error(fileIOStatus.ErrorCode.ToInternalError(), "Failed to create the mod's folder", fileIOStatus.Message)
			};
		}
		await _fileIo.CreateDirectory(Path.Combine(path, ".metadata")).ConfigureAwait(continueOnCapturedContext: false);
		ModsCacheError result = await AddMod(new Mod
		{
			Name = modName,
			DisplayName = displayName,
			ShortDescription = shortDescription,
			LongDescription = longDescription,
			Size = size,
			LocalData = new LocalData
			{
				ContentFileOrFolder = null,
				ThumbnailFilename = null,
				ScreenshotsFilenames = new List<string>(),
				FolderAbsolutePath = path,
				LocalType = LocalType.WorkInProgress
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			await _fileIo.DeleteDirectory(path, recursive: true).ConfigureAwait(continueOnCapturedContext: false);
			return new RegisterResult
			{
				Success = false,
				Error = ((result.FileIoStatus == null) ? result.Error : new Error(result.FileIoStatus.ErrorCode.ToInternalError(), "Register WIP mod failed write to disk", result.FileIoStatus.Message))
			};
		}
		return new RegisterResult
		{
			Success = true,
			Guid = modName,
			Path = path
		};
	}

	public async Task<RegisterResult> RegisterExistingWIP(UpdateWipData wipData, string path, int? modId = null, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<RegisterResult>();
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.RegisterExistingWIP", LogLevel.L1_Debug, flowData2);
		if (string.IsNullOrEmpty(wipData.guid))
		{
			return new RegisterResult
			{
				Success = false,
				Error = new Error(InvalidInput.MissingParameter, "Please specify the mod's guid (unique name)", string.Empty)
			};
		}
		IMod valueOrNull = _mods.GetValueOrNull(wipData.guid);
		if (valueOrNull != null)
		{
			if (valueOrNull.LocalData?.FolderAbsolutePath == path)
			{
				return new RegisterResult
				{
					Success = true,
					Guid = wipData.guid,
					Path = path
				};
			}
			return new RegisterResult
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, "The WIP mod " + wipData.guid + " already exists", string.Empty)
			};
		}
		string path2 = Path.Combine(path, ".metadata");
		if (!_fileIo.PathExists(path2))
		{
			await _fileIo.CreateDirectory(path2).ConfigureAwait(continueOnCapturedContext: false);
		}
		Mod mod = new Mod
		{
			Name = wipData.guid,
			Id = modId.GetValueOrDefault(),
			DisplayName = wipData.displayName,
			ShortDescription = wipData.shortDescription,
			LongDescription = wipData.longDescription,
			Size = wipData.size,
			LocalData = new LocalData
			{
				FolderAbsolutePath = path,
				LocalType = LocalType.WorkInProgress
			},
			Tags = wipData.tags
		};
		SetAssetsPaths(mod.LocalData, wipData.contentFileOrFolderName, wipData.thumbnailFilename, wipData.screenshotsFilenames);
		ModsCacheError modsCacheError = await AddMod(mod).ConfigureAwait(continueOnCapturedContext: false);
		if (!modsCacheError.Success)
		{
			return new RegisterResult
			{
				Success = false,
				Error = ((modsCacheError.FileIoStatus == null) ? modsCacheError.Error : new Error(modsCacheError.FileIoStatus.ErrorCode.ToInternalError(), "Register existing WIP mod failed write to disk", modsCacheError.FileIoStatus.Message))
			};
		}
		return new RegisterResult
		{
			Success = true,
			Guid = mod.Name,
			Path = path
		};
	}

	public async Task<Result> UpdateWIP(UpdateWipData updateWipData, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false);
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.UpdateWorkInProgress", LogLevel.L1_Debug, flowData2);
		IMod mod = await GetWipMod(updateWipData.guid).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null)
		{
			string text = "WIP Guid " + updateWipData.guid + " cannot be found in cache. Not registered?";
			_logger?.Log(text, LogLevel.L2_Warning);
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.LocalError, text, "")
			};
		}
		if (!string.IsNullOrEmpty(updateWipData.contentFileOrFolderName) && updateWipData.contentFileOrFolderName.Any(((IEnumerable<char>)Path.GetInvalidFileNameChars()).Contains<char>))
		{
			return new Result
			{
				Success = false,
				Error = new Error(InvalidInput.InvalidFormat, "Invalid format of 'contentFileOrFolderName'. Please specify the actual name of the mod's content folder", string.Empty)
			};
		}
		mod.DisplayName = updateWipData.displayName ?? mod.DisplayName;
		mod.ShortDescription = updateWipData.shortDescription ?? mod.ShortDescription;
		mod.LongDescription = updateWipData.longDescription ?? mod.LongDescription;
		mod.Size = ((updateWipData.size != 0L) ? updateWipData.size : mod.Size);
		mod.Tags = ((updateWipData.tags != null && updateWipData.tags.Count > 0) ? updateWipData.tags : mod.Tags);
		if (mod.LocalData == null)
		{
			mod.LocalData = new LocalData
			{
				LocalType = LocalType.WorkInProgress,
				FolderAbsolutePath = _pathFactory.GetModsPath(updateWipData.guid, LocalType.WorkInProgress)
			};
		}
		SetAssetsPaths(mod.LocalData, updateWipData.contentFileOrFolderName, updateWipData.thumbnailFilename, updateWipData.screenshotsFilenames);
		ModsCacheError modsCacheError = await UpdateMod(mod).ConfigureAwait(continueOnCapturedContext: false);
		if (!modsCacheError.Success)
		{
			return new Result
			{
				Success = false,
				Error = ((modsCacheError.FileIoStatus == null) ? modsCacheError.Error : new Error(modsCacheError.FileIoStatus.ErrorCode.ToInternalError(), modsCacheError.FileIoStatus.Message, ""))
			};
		}
		return new Result
		{
			Success = true
		};
	}

	private void SetAssetsPaths(LocalData localData, string contentFileOrFolderName = null, string thumbnailFilename = null, List<string> screenshotsFilenames = null)
	{
		if (!string.IsNullOrEmpty(contentFileOrFolderName))
		{
			if (contentFileOrFolderName == Path.GetFileName(localData.FolderAbsolutePath))
			{
				localData.ContentFileOrFolder = string.Empty;
			}
			else
			{
				localData.ContentFileOrFolder = contentFileOrFolderName;
			}
		}
		if (!string.IsNullOrEmpty(thumbnailFilename))
		{
			if (_fileIo.PathExists(Path.Combine(localData.FolderAbsolutePath, ".metadata", thumbnailFilename)))
			{
				localData.ThumbnailFilename = Path.Combine(".metadata", thumbnailFilename);
			}
			else
			{
				localData.ThumbnailFilename = thumbnailFilename;
			}
		}
		if (screenshotsFilenames == null)
		{
			return;
		}
		List<string> list = new List<string>();
		foreach (string screenshotsFilename in screenshotsFilenames)
		{
			if (_fileIo.PathExists(Path.Combine(localData.FolderAbsolutePath, ".metadata", screenshotsFilename)))
			{
				list.Add(Path.Combine(".metadata", screenshotsFilename));
			}
			else
			{
				list.Add(screenshotsFilename);
			}
		}
		localData.ScreenshotsFilenames = list;
	}

	public async Task<Result> UnregisterWIP(string guid, FlowData flowData = null, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false);
		}
		FlowData flowData2 = new FlowData(flowData);
		_logger?.Log("Mods.UnregisterWIP", LogLevel.L1_Debug, flowData2);
		IMod wipMod = await GetWipMod(guid).ConfigureAwait(continueOnCapturedContext: false);
		if (wipMod == null)
		{
			return new Result
			{
				Success = false,
				Error = new Error(NotFound.NoMod, "The mod " + guid + " could not be found", string.Empty)
			};
		}
		IEnumerable<CachedPlaysetMetaData> enumerable = (await GetAllPlaysets().ConfigureAwait(continueOnCapturedContext: false)).Where((CachedPlaysetMetaData p) => p.WipModsTracked.GetAllWithoutTracking().Keys.Contains(guid));
		foreach (CachedPlaysetMetaData item in enumerable)
		{
			await RemoveModFromPlayset(guid, item).ConfigureAwait(continueOnCapturedContext: false);
		}
		ModsCacheError modsCacheError = await DeleteMod(guid).ConfigureAwait(continueOnCapturedContext: false);
		if (!modsCacheError.Success)
		{
			return new Result
			{
				Success = false,
				Error = ((modsCacheError.FileIoStatus == null) ? modsCacheError.Error : new Error(modsCacheError.FileIoStatus?.ErrorCode.ToInternalError(), "File operation error", modsCacheError.FileIoStatus.Message))
			};
		}
		FileIOStatus fileIOStatus = await LockWhileAccessingFile(async () => await _fileIo.DeleteDirectory(wipMod.LocalData.FolderAbsolutePath, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		if (!fileIOStatus.Success)
		{
			return fileIOStatus.ToResult();
		}
		return new Result
		{
			Success = true
		};
	}

	public async Task<bool> AddSubscribedModToPlayset(int modId, string version, ulong size, CachedPlaysetMetaData cachedPlayset, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		if (!(await cachedPlayset.SubscribedModsTracked.TryAdd(modId.GetKey(version), new PlaysetSubscribedModMetaData
		{
			Id = modId,
			IsEnabled = false,
			Version = version,
			LoadOrder = cachedPlayset.GeneralDataTracked.TrackedObject.ModsCount
		}).ConfigureAwait(continueOnCapturedContext: false)))
		{
			_logger?.Log("Failed to add the subscribed mod to the cached playset");
			return false;
		}
		cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromAddedMod(size);
		return true;
	}

	public async Task<bool> AddWipModToPlayset(string modName, ulong size, CachedPlaysetMetaData cachedPlayset, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		if (!(await cachedPlayset.WipModsTracked.TryAdd(modName, new PlaysetWipModMetaData
		{
			Id = modName,
			IsEnabled = false,
			LoadOrder = cachedPlayset.GeneralDataTracked.TrackedObject.ModsCount + 1
		}).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		await cachedPlayset.WipModsTracked.ResetDirty(modName).ConfigureAwait(continueOnCapturedContext: false);
		cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromAddedMod(size);
		return true;
	}

	public async Task<bool> AddUnmanagedModToPlayset(string modPath, ulong size, CachedPlaysetMetaData cachedPlayset, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		if (!(await cachedPlayset.UnmanagedModsTracked.TryAdd(modPath, new PlaysetUnmanagedModMetaData
		{
			Path = modPath,
			IsEnabled = true,
			LoadOrder = cachedPlayset.GeneralDataTracked.TrackedObject.ModsCount + 1
		}).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return false;
		}
		await cachedPlayset.UnmanagedModsTracked.ResetDirty(modPath).ConfigureAwait(continueOnCapturedContext: false);
		cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromAddedMod(size);
		return true;
	}

	public async Task<bool> RemoveModFromPlayset(string modKey, CachedPlaysetMetaData cachedPlayset, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		bool flag = await cachedPlayset.SubscribedModsTracked.TryRemove(modKey).ConfigureAwait(continueOnCapturedContext: false);
		if (!flag)
		{
			flag = await cachedPlayset.WipModsTracked.TryRemove(modKey).ConfigureAwait(continueOnCapturedContext: false);
		}
		bool flag2 = flag;
		if (!flag2)
		{
			flag2 = await cachedPlayset.UnmanagedModsTracked.TryRemove(modKey).ConfigureAwait(continueOnCapturedContext: false);
		}
		if (flag2)
		{
			IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(modKey)).ConfigureAwait(continueOnCapturedContext: false);
			cachedPlayset.GeneralDataTracked.TrackedObject.UpdateMetaFromRemovedMod(mod?.Size ?? 0);
			return true;
		}
		_logger.Log($"The mod {modKey} could not be unsubscribed from the playset {cachedPlayset.GeneralDataTracked.TrackedObject.Id} locally", LogLevel.L2_Warning);
		return false;
	}

	public async Task<ModsCacheError> CleanUpSubscribedMod(int modId, string version, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false).CastTo<ModsCacheError>();
		}
		IMod mod = await GetSubscribedMod(modId, version).ConfigureAwait(continueOnCapturedContext: false);
		if (mod == null)
		{
			return new ModsCacheError
			{
				Success = false,
				Error = new Error(NotFound.NoMod, $"The mod {modId} with version {version} could not be found", string.Empty)
			};
		}
		FileIOStatus fileIoStatus = await LockWhileAccessingFile(async () => await _fileIo.DeleteDirectory(mod.LocalData.FolderAbsolutePath, recursive: true).ConfigureAwait(continueOnCapturedContext: false)).ConfigureAwait(continueOnCapturedContext: false);
		ModsCacheError modsCacheError = await DeleteMod(modId.GetKey(version)).ConfigureAwait(continueOnCapturedContext: false);
		if (!modsCacheError.Success)
		{
			return modsCacheError;
		}
		if (!fileIoStatus.Success)
		{
			return new ModsCacheError(fileIoStatus.ToResult());
		}
		return new ModsCacheError
		{
			Success = true
		};
	}

	public async Task UpdateModsLoadOrderInPlayset(CachedPlaysetMetaData cachedPlayset, List<ModLoadOrder> modLoadOrder, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return;
		}
		foreach (ModLoadOrder loadOrder in modLoadOrder)
		{
			IMod mod = await LockWhileReadingCache(() => _mods.GetValueOrNull(loadOrder.Mod.GetKey())).ConfigureAwait(continueOnCapturedContext: false);
			if (mod == null)
			{
				continue;
			}
			if (mod.LocalData.LocalType == LocalType.Subscribed)
			{
				PlaysetSubscribedModMetaData playsetSubscribedModMetaData = cachedPlayset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.GetKey() == loadOrder.Mod.GetKey());
				if (playsetSubscribedModMetaData != null)
				{
					playsetSubscribedModMetaData.LoadOrder = loadOrder.LoadOrder;
					await cachedPlayset.SubscribedModsTracked.TryUpdate(playsetSubscribedModMetaData.GetKey(), playsetSubscribedModMetaData).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else if (mod.LocalData.LocalType == LocalType.WorkInProgress)
			{
				PlaysetWipModMetaData playsetWipModMetaData = cachedPlayset.WipModsTracked.FirstOrDefault((PlaysetWipModMetaData m) => m.GetKey() == loadOrder.Mod.GetKey());
				if (playsetWipModMetaData != null)
				{
					playsetWipModMetaData.LoadOrder = loadOrder.LoadOrder;
					await cachedPlayset.WipModsTracked.TryUpdate(playsetWipModMetaData.GetKey(), playsetWipModMetaData).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
			else
			{
				PlaysetUnmanagedModMetaData playsetUnmanagedModMetaData = cachedPlayset.UnmanagedModsTracked.FirstOrDefault((PlaysetUnmanagedModMetaData m) => m.GetKey() == loadOrder.Mod.GetKey());
				if (playsetUnmanagedModMetaData != null)
				{
					playsetUnmanagedModMetaData.LoadOrder = loadOrder.LoadOrder;
					await cachedPlayset.UnmanagedModsTracked.TryUpdate(playsetUnmanagedModMetaData.GetKey(), playsetUnmanagedModMetaData).ConfigureAwait(continueOnCapturedContext: false);
				}
			}
		}
		cachedPlayset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
	}

	public async Task<bool> ResetLoadOrderInPlayset(int playsetId, List<ModLoadOrder> subscribedModLoadOrder, CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		CachedPlaysetMetaData cachedPlayset = await GetPlaysetOrNull(playsetId).ConfigureAwait(continueOnCapturedContext: false);
		if (cachedPlayset == null)
		{
			return false;
		}
		foreach (ModLoadOrder modLoadOrder in subscribedModLoadOrder)
		{
			PlaysetSubscribedModMetaData playsetSubscribedModMetaData = cachedPlayset.SubscribedModsTracked.FirstOrDefault((PlaysetSubscribedModMetaData m) => m.Id == modLoadOrder.Mod.Id);
			if (playsetSubscribedModMetaData != null)
			{
				playsetSubscribedModMetaData.LoadOrder = modLoadOrder.LoadOrder;
				await cachedPlayset.SubscribedModsTracked.TryUpdate(playsetSubscribedModMetaData.GetKey(), playsetSubscribedModMetaData).ConfigureAwait(continueOnCapturedContext: false);
			}
		}
		int loadOrder = cachedPlayset.SubscribedModsTracked.Count();
		foreach (PlaysetWipModMetaData item in cachedPlayset.WipModsTracked)
		{
			item.LoadOrder = loadOrder++;
			await cachedPlayset.WipModsTracked.TryUpdate(item.GetKey(), item).ConfigureAwait(continueOnCapturedContext: false);
		}
		foreach (PlaysetUnmanagedModMetaData item2 in cachedPlayset.UnmanagedModsTracked)
		{
			item2.LoadOrder = loadOrder++;
			await cachedPlayset.UnmanagedModsTracked.TryUpdate(item2.Path, item2).ConfigureAwait(continueOnCapturedContext: false);
		}
		cachedPlayset.GeneralDataTracked.TrackedObject.Updated = DateTime.Now;
		cachedPlayset.GeneralDataTracked.TrackedObject.UpdatedSyncedWithBackend = DateTime.Now;
		await UpdatePlayset(cachedPlayset.GetKey(), cachedPlayset).ConfigureAwait(continueOnCapturedContext: false);
		await ResetAllDirtyInPlayset(cachedPlayset.GetKey()).ConfigureAwait(continueOnCapturedContext: false);
		return true;
	}

	public async Task<bool> SetActivePlayset(int? playsetId)
	{
		if (!playsetId.HasValue)
		{
			_playsetSettings.ActivePlaysetId = null;
			await LockWhileAccessingFile(async delegate
			{
				await _fileIo.Delete(_pathFactory.GetModsRootPath("PlaysetSettings")).ConfigureAwait(continueOnCapturedContext: false);
			}).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		if (playsetId.Value == _playsetSettings.ActivePlaysetId)
		{
			return true;
		}
		if (await GetPlaysetOrNull(playsetId.Value).ConfigureAwait(continueOnCapturedContext: false) != null)
		{
			_playsetSettings.ActivePlaysetId = playsetId;
			await LockWhileAccessingFile(async delegate
			{
				await _fileIo.WriteJSON(_pathFactory.GetModsRootPath("PlaysetSettings"), _playsetSettings).ConfigureAwait(continueOnCapturedContext: false);
			}).ConfigureAwait(continueOnCapturedContext: false);
			return true;
		}
		return false;
	}

	public async Task<CachedPlaysetMetaData> GetActivePlayset()
	{
		if (!_playsetSettings.ActivePlaysetId.HasValue)
		{
			return null;
		}
		return await GetPlaysetOrNull(_playsetSettings.ActivePlaysetId.Value).ConfigureAwait(continueOnCapturedContext: false);
	}

	internal async Task<bool> LoadActivePlayset(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return false;
		}
		string body = "";
		await LockWhileAccessingFile(async delegate
		{
			try
			{
				OperationResult<string> operationResult = await _fileIo.ReadText(_pathFactory.GetModsRootPath("PlaysetSettings")).ConfigureAwait(continueOnCapturedContext: false);
				if (operationResult.Status.Success)
				{
					body = operationResult.Result;
				}
			}
			catch (Exception ex)
			{
				_logger?.Log(ex.Message, LogLevel.L3_Error);
			}
		}).ConfigureAwait(continueOnCapturedContext: false);
		if (string.IsNullOrEmpty(body))
		{
			return false;
		}
		PlaysetSettings playsetSettings = body.FromJson<PlaysetSettings>();
		return await SetActivePlayset(playsetSettings.ActivePlaysetId).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Result> CleanUpSyncedModState(bool hardClean = false)
	{
		AggregateResult result = new AggregateResult();
		IEnumerable<IMod> enumerable = _mods.Where((IMod m) => m.IsSubscribedMod());
		foreach (IMod item in enumerable)
		{
			AggregateResult aggregateResult = result;
			aggregateResult.Add(await CleanUpSubscribedMod(item.Id, item.Version).ConfigureAwait(continueOnCapturedContext: false));
		}
		if (hardClean)
		{
			foreach (CachedPlaysetMetaData playset in _playsets)
			{
				AggregateResult aggregateResult = result;
				aggregateResult.Add(await DeleteLocalPlayset(playset.GeneralDataTracked.TrackedObject.Id).ConfigureAwait(continueOnCapturedContext: false));
			}
		}
		return result;
	}

	public async Task<T> LockWhileReadingCache<T>(Func<T> readFunc)
	{
		try
		{
			await _readWriteLock.AcquireReaderLock().ConfigureAwait(continueOnCapturedContext: false);
			return readFunc();
		}
		finally
		{
			_readWriteLock.ReleaseReaderLock();
		}
	}

	internal async Task LockWhileAccessingFile(Func<Task> writeFunc)
	{
		_ = 1;
		try
		{
			await _readWriteLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			await writeFunc().ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			_readWriteLock.ReleaseWriterLock();
		}
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

	internal async Task LockWhileAccessingFile(Action writeAction)
	{
		try
		{
			await _readWriteLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			writeAction();
		}
		finally
		{
			_readWriteLock.ReleaseWriterLock();
		}
	}
}
