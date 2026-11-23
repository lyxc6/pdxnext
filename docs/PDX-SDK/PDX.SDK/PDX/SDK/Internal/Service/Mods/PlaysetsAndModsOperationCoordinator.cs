using System;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;
using PDX.SDK.Contracts.Enums.Errors;
using PDX.SDK.Contracts.Events.Mods;
using PDX.SDK.Events;
using PDX.SDK.Internal.Enums;
using PDX.SDK.Internal.Events.Mods;
using PDX.SDK.Internal.Results;
using PDX.SDK.Internal.Util;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal class PlaysetsAndModsOperationCoordinator : IPlaysetsAndModsOperationCoordinator
{
	private readonly IInternalEventHub _eventHub;

	private readonly AsyncReaderWriterLock _readerWriterLock = new AsyncReaderWriterLock();

	private bool _isSyncOngoing;

	public bool IsSyncOngoing
	{
		get
		{
			return _isSyncOngoing;
		}
		private set
		{
			_isSyncOngoing = value;
			_eventHub.Publish((IModSyncStatusChanged)new ModSyncStatusChanged
			{
				IsSyncOngoing = value
			});
		}
	}

	[Preserve]
	public PlaysetsAndModsOperationCoordinator(IInternalEventHub eventHub)
	{
		_eventHub = eventHub;
	}

	public async Task<Result> TryAcquireSync(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false);
		}
		switch (await _readerWriterLock.TryAcquireWriterLock(cancellationToken).ConfigureAwait(continueOnCapturedContext: false))
		{
		case AquireLockStatus.Success:
			IsSyncOngoing = true;
			return new Result
			{
				Success = true
			};
		case AquireLockStatus.WriteFail:
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.SyncOngoing, "A playset and mods sync is already ongoing", "")
			};
		default:
			return new Result
			{
				Success = false,
				Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.OperationsOngoing, "A mod operation is already ongoing", "")
			};
		}
	}

	public void ReleaseSync(CancellationToken cancellationToken = default(CancellationToken))
	{
		_isSyncOngoing = false;
		_readerWriterLock.ReleaseWriterLock();
		_eventHub.Publish((IModSyncStatusChanged)new ModSyncStatusChanged
		{
			IsSyncOngoing = false
		});
	}

	public async Task<Result> TryAcquireRead(CancellationToken cancellationToken = default(CancellationToken))
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return ResultFactory.CreateCancellationResult(success: false);
		}
		if (await _readerWriterLock.TryAcquireReaderLock(cancellationToken).ConfigureAwait(continueOnCapturedContext: false) == AquireLockStatus.Success)
		{
			return new Result
			{
				Success = true
			};
		}
		return new Result
		{
			Success = false,
			Error = new Error(PDX.SDK.Contracts.Enums.Errors.Mods.SyncOngoing, "A playset and mods sync is already ongoing", "")
		};
	}

	public void ReleaseRead(CancellationToken cancellationToken = default(CancellationToken))
	{
		_readerWriterLock.ReleaseReaderLock();
	}

	public async Task<T> ExecuteWithSyncLock<T>(Func<Task<T>> func, CancellationToken cancellationToken = default(CancellationToken)) where T : Result
	{
		Result result = await TryAcquireSync(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<T>();
		}
		try
		{
			return await func().ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			ReleaseSync(cancellationToken);
		}
	}

	public async Task<T> ExecuteIfNotSyncing<T>(Func<Task<T>> func, CancellationToken cancellationToken = default(CancellationToken)) where T : Result
	{
		Result result = await TryAcquireRead(cancellationToken).ConfigureAwait(continueOnCapturedContext: false);
		if (!result.Success)
		{
			return result.CastTo<T>();
		}
		try
		{
			return await func().ConfigureAwait(continueOnCapturedContext: false);
		}
		finally
		{
			ReleaseRead(cancellationToken);
		}
	}
}
