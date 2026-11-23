using System;
using System.Threading.Tasks;
using PDX.SDK.Contracts.Enums;
using PDX.SDK.Contracts.Logging;
using PDX.SDK.Internal.Cache.Models;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Cache;

internal abstract class CacheBase<TCache> : IUpdateHandle<TCache> where TCache : class, new()
{
	private readonly IFileIO _fileIo;

	private readonly ILogger _logger;

	private readonly FilePath _filePathEnum;

	private readonly AsyncReaderWriterLock _readerWriterLock = new AsyncReaderWriterLock();

	protected TCache CachedData { get; private set; }

	public CacheBase(IFileIO fileIo, ILogger logger = null)
	{
		_fileIo = fileIo;
		_logger = logger;
		_filePathEnum = FilePath.None;
	}

	public CacheBase(IFileIO fileIo, FilePath filePath, ILogger logger = null)
	{
		_fileIo = fileIo;
		_logger = logger;
		_filePathEnum = filePath;
	}

	protected virtual string GetCacheFilePath()
	{
		return _fileIo.GetPath(_filePathEnum);
	}

	public virtual async Task Init()
	{
		_ = 1;
		try
		{
			await _readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			string cacheFilePath = GetCacheFilePath();
			OperationResult<TCache> operationResult = await _fileIo.ReadJSON<TCache>(cacheFilePath).ConfigureAwait(continueOnCapturedContext: false);
			if (!operationResult.Status.Success && operationResult.Status.ErrorCode != DiskIOError.FileNotFound)
			{
				_logger?.Log($"Unable to read cached data: {operationResult.Status.Message}, {operationResult.Status.ErrorCode}", LogLevel.L4_Fatal);
			}
			CachedData = operationResult.Result ?? new TCache();
		}
		finally
		{
			_readerWriterLock.ReleaseWriterLock();
		}
	}

	public async Task<UpdateResult<T>> Update<T>(Action<TCache> stateMutationHook) where T : class
	{
		_ = 1;
		try
		{
			await _readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			stateMutationHook(CachedData);
			FileIOStatus fileIOStatus = await _fileIo.WriteJSON(GetCacheFilePath(), CachedData).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				_logger?.Log($"Failed to persist {GetCacheFilePath()} cache data to disk: {fileIOStatus.Message}, {fileIOStatus.ErrorCode}", LogLevel.L3_Error);
			}
			return new UpdateResult<T>
			{
				Status = fileIOStatus,
				NewState = (CachedData as T)
			};
		}
		finally
		{
			_readerWriterLock.ReleaseWriterLock();
		}
	}

	public async Task Move(string sourcePath, string targetPath)
	{
		_ = 1;
		try
		{
			await _readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			FileIOStatus fileIOStatus = await _fileIo.Move(sourcePath, targetPath).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				_logger?.Log($"Failed to persist {GetCacheFilePath()} cache data to disk: {fileIOStatus.Message}, {fileIOStatus.ErrorCode}", LogLevel.L3_Error);
			}
		}
		finally
		{
			_readerWriterLock.ReleaseWriterLock();
		}
	}

	public async Task CleanUp()
	{
		_ = 1;
		try
		{
			await _readerWriterLock.AcquireWriterLock().ConfigureAwait(continueOnCapturedContext: false);
			FileIOStatus fileIOStatus = await _fileIo.Delete(GetCacheFilePath()).ConfigureAwait(continueOnCapturedContext: false);
			if (!fileIOStatus.Success)
			{
				_logger?.Log($"Failed to delete {GetCacheFilePath()} cache data from disk: {fileIOStatus.Message}, {fileIOStatus.ErrorCode}", LogLevel.L3_Error);
			}
		}
		finally
		{
			_readerWriterLock.ReleaseWriterLock();
		}
	}

	protected async Task<T> ObtainReadlockAndReturnData<T>(T data)
	{
		try
		{
			await _readerWriterLock.AcquireReaderLock().ConfigureAwait(continueOnCapturedContext: false);
			return data;
		}
		finally
		{
			_readerWriterLock.ReleaseReaderLock();
		}
	}
}
