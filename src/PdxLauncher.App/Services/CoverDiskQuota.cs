namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊封面/截图目录的磁盘配额：总量超过 100 MB 时，从最久没写过的文件开始删，删到 90 MB 为止。
/// <para>
/// 实测这个目录会涨到 200 MB 以上：每个 mod 一张封面，详情页还会再存一组截图，而列表里翻过的
/// mod 不会自己消失。目录只增不减，重装/换盘时用户会看到一个启动器占了小半个 G。
/// </para>
/// <para>
/// 刚下载完的文件（10 分钟内写过）不删：它们可能正被当前页面引用，删掉会变成「图刚出来又没了」。
/// 删除失败只记日志，不阻断别的文件。
/// </para>
/// </summary>
public sealed class CoverDiskQuota
{
    /// <summary>超过这个总量就开始清理。</summary>
    public const long MaxBytes = 100L * 1024 * 1024;

    /// <summary>清理的目标总量（留一点余量，避免每次下载都触发一轮）。</summary>
    public const long TargetBytes = 90L * 1024 * 1024;

    /// <summary>最近写过的文件不参与清理。</summary>
    public static readonly TimeSpan MinAge = TimeSpan.FromMinutes(10);

    /// <summary>连续下载时把清理合并成一轮（下载完成后 60 秒再扫）。</summary>
    private static readonly TimeSpan SweepDelay = TimeSpan.FromSeconds(60);

    private readonly IFileSystem _files;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;
    private readonly string _root;
    private readonly long _maxBytes;
    private readonly long _targetBytes;
    private readonly object _sync = new();

    private Timer? _timer;
    private bool _sweeping;

    /// <summary>绑定封面根目录（<see cref="WorkshopInfoCache.CoverRoot"/> 那一层）。</summary>
    /// <param name="maxBytes">超过这个总量开始清理；默认 <see cref="MaxBytes"/>。</param>
    /// <param name="targetBytes">清理到不超过这个总量；默认 <see cref="TargetBytes"/>。</param>
    public CoverDiskQuota(
        IFileSystem files,
        IAppLogger logger,
        IClock clock,
        string root,
        long maxBytes = MaxBytes,
        long targetBytes = TargetBytes)
    {
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        if (targetBytes < 0 || maxBytes < targetBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(targetBytes), "目标总量不能为负，也不能大于上限。");
        }

        _files = files;
        _logger = logger;
        _clock = clock;
        _root = root;
        _maxBytes = maxBytes;
        _targetBytes = targetBytes;
    }

    /// <summary>目录当前占用的字节数（不存在按 0）。</summary>
    public long Measure()
    {
        long total = 0;
        foreach (var path in EnumerateFiles())
        {
            total += _files.GetFileSize(path);
        }

        return total;
    }

    /// <summary>按配额扫一轮；返回删了多少、剩下多少。失败只记日志。</summary>
    public CoverQuotaResult Enforce()
    {
        lock (_sync)
        {
            if (_sweeping)
            {
                return new CoverQuotaResult(0, 0, Measure());
            }

            _sweeping = true;
        }

        try
        {
            var now = _clock.UtcNow;
            var files = new List<CoverFile>();
            long total = 0;

            foreach (var path in EnumerateFiles())
            {
                var bytes = _files.GetFileSize(path);
                if (bytes <= 0)
                {
                    continue;
                }

                files.Add(new CoverFile(path, bytes, LastWriteUtc(path)));
                total += bytes;
            }

            if (total <= _maxBytes)
            {
                return new CoverQuotaResult(0, 0, total);
            }

            var victims = SelectVictims(files, total, _targetBytes, now, MinAge);
            var deleted = 0;
            long freed = 0;

            foreach (var victim in victims)
            {
                try
                {
                    _files.Delete(victim.Path);
                    deleted++;
                    freed += victim.Bytes;
                }
                catch (Exception ex)
                {
                    _logger.Warn("清理工坊封面失败（" + victim.Path + "）：" + ex.Message);
                }
            }

            if (deleted > 0)
            {
                _logger.Info("工坊封面目录超配额，已清理 " + deleted + " 个文件、释放 " + (freed / 1024) + " KB");
            }

            return new CoverQuotaResult(deleted, freed, total - freed);
        }
        catch (Exception ex)
        {
            _logger.Warn("扫描工坊封面目录失败：" + ex.Message);
            return new CoverQuotaResult(0, 0, 0);
        }
        finally
        {
            lock (_sync)
            {
                _sweeping = false;
            }
        }
    }

    /// <summary>启动时在后台扫一次，不阻塞首屏。</summary>
    public void SweepInBackground()
    {
        _ = Task.Run(() =>
        {
            try
            {
                Enforce();
            }
            catch (Exception ex)
            {
                _logger.Warn("启动清理工坊封面失败：" + ex.Message);
            }
        });
    }

    /// <summary>下载完一张图后调用：60 秒内只安排一轮清理。</summary>
    public void RequestSweep()
    {
        lock (_sync)
        {
            _timer ??= new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _timer.Change(SweepDelay, Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>
    /// 选出要删的文件：最久没写过的优先，最近 10 分钟写过的一律跳过；
    /// 删到剩余量不超过 <paramref name="targetBytes"/> 就停。
    /// </summary>
    public static IReadOnlyList<CoverFile> SelectVictims(
        IEnumerable<CoverFile> files,
        long totalBytes,
        long targetBytes,
        DateTimeOffset now,
        TimeSpan minAge)
    {
        ArgumentNullException.ThrowIfNull(files);

        if (totalBytes <= targetBytes)
        {
            return Array.Empty<CoverFile>();
        }

        var candidates = files
            .Where(file => file.Bytes > 0)
            .Where(file => now - file.LastWriteUtc >= minAge)
            .OrderBy(file => file.LastWriteUtc)
            .ToList();

        var victims = new List<CoverFile>();
        long remaining = totalBytes;

        foreach (var file in candidates)
        {
            if (remaining <= targetBytes)
            {
                break;
            }

            victims.Add(file);
            remaining -= file.Bytes;
        }

        return victims;
    }

    private IEnumerable<string> EnumerateFiles()
    {
        if (!_files.DirectoryExists(_root))
        {
            return Array.Empty<string>();
        }

        return _files.EnumerateFiles(_root, "*", recursive: true);
    }

    private DateTimeOffset LastWriteUtc(string path)
    {
        try
        {
            return new DateTimeOffset(File.GetLastWriteTimeUtc(path), TimeSpan.Zero);
        }
        catch (Exception)
        {
            // 读不到时间就当「很旧」，让配额能删掉它；真删不掉会在 Delete 那一步记日志。
            return DateTimeOffset.UnixEpoch;
        }
    }

    private void OnTimer(object? state)
    {
        _ = Task.Run(() =>
        {
            try
            {
                Enforce();
            }
            catch (Exception ex)
            {
                _logger.Warn("后台清理工坊封面失败：" + ex.Message);
            }
        });
    }
}

/// <summary>一个待清理的封面/截图文件。</summary>
/// <param name="Path">绝对路径。</param>
/// <param name="Bytes">文件字节数。</param>
/// <param name="LastWriteUtc">最后写入时间（UTC）。</param>
public sealed record CoverFile(string Path, long Bytes, DateTimeOffset LastWriteUtc);

/// <summary>一轮清理的结果。</summary>
/// <param name="DeletedFiles">删掉的文件数。</param>
/// <param name="DeletedBytes">释放的字节数。</param>
/// <param name="TotalBytes">清理后的目录总量。</param>
public sealed record CoverQuotaResult(int DeletedFiles, long DeletedBytes, long TotalBytes);
