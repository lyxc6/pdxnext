using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Cache;

internal class StateCache : CacheBase<CachedData>, IStateCache
{
	private readonly IFileIO _fileIo;

	private readonly ILogger _logger;

	public ISessionCommand Session { get; }

	public IPlayerCommand Player { get; }

	public IMachineCommand Machine { get; }

	public StateCache(IFileIO fileIo, ILogger logger = null)
		: base(fileIo, FilePath.StateCache, logger)
	{
		_fileIo = fileIo;
		_logger = logger;
		Session = new SessionCommand(this);
		Player = new PlayerCommand(this);
		Machine = new MachineCommand(this);
	}

	public Task<ISessionData> GetSessionData()
	{
		return ObtainReadlockAndReturnData((ISessionData)base.CachedData);
	}

	public Task<IPlayerData> GetPlayerData()
	{
		return ObtainReadlockAndReturnData((IPlayerData)base.CachedData);
	}

	public Task<IMachineData> GetMachineData()
	{
		return ObtainReadlockAndReturnData((IMachineData)base.CachedData);
	}

	public override async Task Init()
	{
		await base.Init().ConfigureAwait(continueOnCapturedContext: false);
		await CheckForOldCacheFilesAndDelete().ConfigureAwait(continueOnCapturedContext: false);
	}

	private async Task CheckForOldCacheFilesAndDelete()
	{
		FilePath[] array = new FilePath[3]
		{
			FilePath.SessionData,
			FilePath.PlayerData,
			FilePath.LegalCache
		};
		FilePath[] array2 = array;
		foreach (FilePath path in array2)
		{
			FileIOStatus fileIOStatus = await _fileIo.Delete(path).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success && fileIOStatus.ErrorCode != DiskIOError.FileNotFound)
			{
				_logger?.Log($"Failed to delete old cache file: {fileIOStatus.Message}, {fileIOStatus.ErrorCode}", LogLevel.L3_Error);
			}
		}
	}
}
