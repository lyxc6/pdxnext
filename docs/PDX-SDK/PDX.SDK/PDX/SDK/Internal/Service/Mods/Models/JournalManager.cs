using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Cache;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Mods.Models;

internal class JournalManager : CacheBase<Dictionary<string, bool>>
{
	private readonly IPathFactory _pathFactory;

	private readonly ILogger _logger;

	public string JournalName { get; private set; }

	public JournalManager(string journalName, IPathFactory pathFactory, IFileIO fileIo, ILogger logger = null)
		: base(fileIo, logger)
	{
		_pathFactory = pathFactory;
		_logger = logger;
		JournalName = journalName;
	}

	protected override string GetCacheFilePath()
	{
		return _pathFactory.GetJournalPath(JournalName);
	}

	public async Task UpdateJournalName(string journalName)
	{
		string cacheFilePath = GetCacheFilePath();
		JournalName = journalName;
		string cacheFilePath2 = GetCacheFilePath();
		await Move(cacheFilePath, cacheFilePath2).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Dictionary<string, bool>> GetDirty()
	{
		return await ObtainReadlockAndReturnData(base.CachedData.Where((KeyValuePair<string, bool> x) => x.Value).ToDictionary((KeyValuePair<string, bool> x) => x.Key, (KeyValuePair<string, bool> x) => x.Value)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<Dictionary<string, bool>> GetEntries()
	{
		return await ObtainReadlockAndReturnData(base.CachedData.ToDictionary((KeyValuePair<string, bool> x) => x.Key, (KeyValuePair<string, bool> x) => x.Value)).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task SetDirty(string key)
	{
		try
		{
			await Update<Dictionary<string, bool>>(delegate(Dictionary<string, bool> state)
			{
				state[key] = true;
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception)
		{
			_logger?.Log("Setting the dirty flag for the key " + key + " failed.", LogLevel.L3_Error);
		}
	}

	public async Task ResetDirty(string key)
	{
		try
		{
			await Update<Dictionary<string, bool>>(delegate(Dictionary<string, bool> state)
			{
				state[key] = false;
			}).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch (Exception)
		{
			_logger?.Log("Resetting the dirty flag for the key " + key + " failed.", LogLevel.L3_Error);
		}
	}

	public async Task Clear()
	{
		await Update<Dictionary<string, bool>>(delegate(Dictionary<string, bool> state)
		{
			state.Clear();
		}).ConfigureAwait(continueOnCapturedContext: false);
	}

	public async Task<bool> Remove(string key)
	{
		try
		{
			return (await Update<Dictionary<string, bool>>(delegate(Dictionary<string, bool> state)
			{
				state.Remove(key);
			}).ConfigureAwait(continueOnCapturedContext: false)).Status.Success;
		}
		catch (Exception ex)
		{
			_logger?.Log(ex.Message, LogLevel.L3_Error);
			return false;
		}
	}
}
