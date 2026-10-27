namespace PdxLauncher.Core.Online;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// mod 缩略图补全：照 <see cref="DlcDescriptionService"/> 的「弱依赖联网」形状实现。
/// <para>
/// 设计要点：整批串行（下载器内部已限 2 并发，这里再串行只为保护索引写入）；
/// 连续 2 次失败就放弃本批（网络断掉时不要 42 个 mod 各等一次超时）；
/// 失败进入 6 小时退避，期间同一 mod 不再发起请求。
/// </para>
/// </summary>
public sealed class ModThumbnailService : IModThumbnailService
{
    private const int MaxConsecutiveFailures = 2;

    private readonly IAssetDownloader _assets;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public ModThumbnailService(
        IFileSystem fileSystem,
        IAssetDownloader assets,
        IClock clock,
        IAppLogger logger,
        string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(assets);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        _assets = assets;
        _clock = clock;
        _logger = logger;
        Cache = new ModThumbnailCache(fileSystem, logger, clock, cacheRoot);
    }

    /// <inheritdoc />
    public ModThumbnailCache Cache { get; }

    /// <inheritdoc />
    public async Task<ModThumbnailFillResult> FillAsync(
        IReadOnlyList<ModLibraryEntry> entries,
        bool allowOnline,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(entries);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var updates = new List<ModThumbnailUpdate>();
            var cacheHits = 0;

            foreach (var entry in entries)
            {
                if (string.IsNullOrWhiteSpace(entry.ThumbnailUrl))
                {
                    continue;
                }

                var cached = Cache.TryGetCachedPath(entry.ModId);
                if (cached is not null)
                {
                    updates.Add(new ModThumbnailUpdate(entry.ModId, cached));
                    cacheHits++;
                }
            }

            // 清理与联网无关：保留天数改小后立即生效，不必等到下一次联网。
            var pruned = Cache.Prune(retentionDays);

            if (!allowOnline)
            {
                return new ModThumbnailFillResult(updates, cacheHits, 0, 0, pruned);
            }

            var attempted = 0;
            var failed = 0;
            var consecutiveFailures = 0;

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var url = entry.ThumbnailUrl?.Trim();
                if (string.IsNullOrWhiteSpace(url) || Cache.TryGetCachedPath(entry.ModId) is not null)
                {
                    continue;
                }

                if (Cache.IsInBackoff(entry.ModId))
                {
                    continue;
                }

                if (consecutiveFailures >= MaxConsecutiveFailures)
                {
                    _logger.Warn("mod 缩略图联网补全：连续失败 " + consecutiveFailures.ToString(CultureInfo.InvariantCulture) + " 次，放弃本批");
                    break;
                }

                attempted++;
                var downloaded = await _assets
                    .DownloadAsync(url, Cache.TargetPathWithoutExtension(entry.ModId), cancellationToken)
                    .ConfigureAwait(false);

                if (string.IsNullOrWhiteSpace(downloaded))
                {
                    failed++;
                    consecutiveFailures++;
                    Cache.RecordFailure(entry.ModId, url);
                    continue;
                }

                consecutiveFailures = 0;
                Cache.RecordSuccess(entry.ModId, url, downloaded);
                updates.Add(new ModThumbnailUpdate(entry.ModId, downloaded));
            }

            if (attempted > 0)
            {
                _logger.Info(
                    "mod 缩略图联网补全：尝试 " + attempted.ToString(CultureInfo.InvariantCulture) +
                    " 个，成功 " + (attempted - failed).ToString(CultureInfo.InvariantCulture) +
                    " 个，失败 " + failed.ToString(CultureInfo.InvariantCulture) +
                    " 个，缓存命中 " + cacheHits.ToString(CultureInfo.InvariantCulture) +
                    " 个（" + Timestamp() + "）");
            }

            return new ModThumbnailFillResult(updates, cacheHits, attempted, failed, pruned);
        }
        finally
        {
            _gate.Release();
        }
    }

    private string Timestamp() => _clock.UtcNow.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
