using System;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Internal.Enums;

namespace PDX.SDK.Internal.Util;

public sealed class AsyncReaderWriterLock : IDisposable
{
	private readonly SemaphoreSlim _readSemaphore = new SemaphoreSlim(1, 1);

	private readonly SemaphoreSlim _writeSemaphore = new SemaphoreSlim(1, 1);

	private int _readerCount;

	public async Task<AquireLockStatus> TryAcquireWriterLock(CancellationToken token = default(CancellationToken))
	{
		if (!(await _writeSemaphore.WaitAsync(0, token).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return AquireLockStatus.WriteFail;
		}
		if (await TrySafeAcquireReadSemaphore(token).ConfigureAwait(continueOnCapturedContext: false))
		{
			return AquireLockStatus.Success;
		}
		_writeSemaphore.Release();
		return AquireLockStatus.ReadFail;
	}

	public async Task AcquireWriterLock(CancellationToken token = default(CancellationToken))
	{
		await _writeSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		await SafeAcquireReadSemaphore(token).ConfigureAwait(continueOnCapturedContext: false);
	}

	public void ReleaseWriterLock()
	{
		_readSemaphore.Release();
		_writeSemaphore.Release();
	}

	public async Task<AquireLockStatus> TryAcquireReaderLock(CancellationToken token = default(CancellationToken))
	{
		if (!(await _writeSemaphore.WaitAsync(0, token).ConfigureAwait(continueOnCapturedContext: false)))
		{
			return AquireLockStatus.WriteFail;
		}
		if (Interlocked.Increment(ref _readerCount) == 1)
		{
			await SafeAcquireReadSemaphore(token).ConfigureAwait(continueOnCapturedContext: false);
		}
		_writeSemaphore.Release();
		return AquireLockStatus.Success;
	}

	public async Task AcquireReaderLock(CancellationToken token = default(CancellationToken))
	{
		await _writeSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		if (Interlocked.Increment(ref _readerCount) == 1)
		{
			await SafeAcquireReadSemaphore(token).ConfigureAwait(continueOnCapturedContext: false);
		}
		_writeSemaphore.Release();
	}

	public void ReleaseReaderLock()
	{
		if (Interlocked.Decrement(ref _readerCount) == 0)
		{
			_readSemaphore.Release();
		}
	}

	private async Task SafeAcquireReadSemaphore(CancellationToken token)
	{
		try
		{
			await _readSemaphore.WaitAsync(token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			_writeSemaphore.Release();
			throw;
		}
	}

	private async Task<bool> TrySafeAcquireReadSemaphore(CancellationToken token)
	{
		try
		{
			return await _readSemaphore.WaitAsync(0, token).ConfigureAwait(continueOnCapturedContext: false);
		}
		catch
		{
			_writeSemaphore.Release();
			throw;
		}
	}

	public void Dispose()
	{
		_writeSemaphore.Dispose();
		_readSemaphore.Dispose();
	}
}
