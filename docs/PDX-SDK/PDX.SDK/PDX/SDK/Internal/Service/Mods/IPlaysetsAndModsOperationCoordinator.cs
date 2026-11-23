using System;
using System.Threading;
using System.Threading.Tasks;
using PDX.SDK.Contracts;
using PDX.SDK.Contracts.Attributes;

namespace PDX.SDK.Internal.Service.Mods;

[Preserve]
internal interface IPlaysetsAndModsOperationCoordinator
{
	bool IsSyncOngoing { get; }

	Task<Result> TryAcquireSync(CancellationToken cancellationToken = default(CancellationToken));

	void ReleaseSync(CancellationToken cancellationToken = default(CancellationToken));

	Task<Result> TryAcquireRead(CancellationToken cancellationToken = default(CancellationToken));

	void ReleaseRead(CancellationToken cancellationToken = default(CancellationToken));

	Task<T> ExecuteWithSyncLock<T>(Func<Task<T>> func, CancellationToken cancellationToken = default(CancellationToken)) where T : Result;

	Task<T> ExecuteIfNotSyncing<T>(Func<Task<T>> func, CancellationToken cancellationToken = default(CancellationToken)) where T : Result;
}
