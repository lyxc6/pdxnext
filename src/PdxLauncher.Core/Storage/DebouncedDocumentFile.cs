namespace PdxLauncher.Core.Storage;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 文档写入的「攒一攒再落盘」装饰器：<see cref="WriteText"/> 只更新内存里的待写内容并重置计时器，
/// 500 毫秒内没有新写入才真正落到内层 <see cref="IDocumentFile"/>。
/// <para>
/// 为什么需要：各缓存的 <c>Save()</c> 是「整份序列化 + 整行覆盖写」，一次列表刷新会连着写十几遍；
/// 工坊页那种边抓边存的场景甚至每秒都写。防抖后同一批改动只落一次盘，UI 线程不再被同步写库卡住。
/// </para>
/// <para>
/// 单飞：落盘用 <see cref="SemaphoreSlim"/> 串行，计时器到点与关机时的 <see cref="Flush"/> 不会互相踩。
/// 关机路径调用 <see cref="FlushAll"/> 把还在等计时的内容写掉，避免退出时丢最后几百毫秒的缓存。
/// </para>
/// </summary>
public sealed class DebouncedDocumentFile : IDocumentFile, IDisposable
{
    private static readonly List<WeakReference<DebouncedDocumentFile>> Live = new();
    private static readonly object LiveLock = new();

    private readonly IDocumentFile _inner;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();

    private string? _pending;
    private bool _disposed;

    /// <summary>包一层防抖；<paramref name="delay"/> 省略时用 500 毫秒。</summary>
    public DebouncedDocumentFile(IDocumentFile inner, IAppLogger logger, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);

        if (delay is { } custom && custom < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "防抖间隔不能为负。");
        }

        _inner = inner;
        _logger = logger;
        _delay = delay ?? TimeSpan.FromMilliseconds(500);
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (LiveLock)
        {
            Live.Add(new WeakReference<DebouncedDocumentFile>(this));
        }
    }

    /// <inheritdoc />
    public string DisplayPath => _inner.DisplayPath;

    /// <inheritdoc />
    public bool Exists() => _inner.Exists();

    /// <summary>读的时候要读「最新的那份」：还在等计时的内容优先于磁盘上的旧版本。</summary>
    public string? ReadText()
    {
        lock (_sync)
        {
            if (_pending is not null)
            {
                return _pending;
            }
        }

        return _inner.ReadText();
    }

    /// <summary>只记在内存里并重置计时器；真正写盘由计时器或 <see cref="Flush"/> 完成。</summary>
    public bool WriteText(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            _pending = json;
        }

        _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        return true;
    }

    /// <summary>删除是立即的：先丢掉待写内容，免得删完又被计时器写回来。</summary>
    public bool Delete()
    {
        lock (_sync)
        {
            _pending = null;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        return _inner.Delete();
    }

    /// <summary>立刻把待写内容落盘；没有待写内容返回 true。写失败保留待写内容，下次再试。</summary>
    public bool Flush()
    {
        string? json;
        lock (_sync)
        {
            json = _pending;
        }

        if (json is null)
        {
            return true;
        }

        var acquired = false;
        try
        {
            _writeGate.Wait();
            acquired = true;

            lock (_sync)
            {
                json = _pending;
                if (json is null)
                {
                    return true;
                }
            }

            if (!_inner.WriteText(json))
            {
                return false;
            }

            lock (_sync)
            {
                // 写盘期间又有新内容进来就留着，不能把新版本一起清掉。
                if (ReferenceEquals(_pending, json))
                {
                    _pending = null;
                }
            }

            return true;
        }
        catch (ObjectDisposedException)
        {
            // Dispose 已经关掉了信号量：进程都要退了，剩下的内容只能放弃。
            return false;
        }
        catch (Exception ex)
        {
            _logger.Warn("缓存落盘失败（" + DisplayPath + "）：" + ex.Message);
            return false;
        }
        finally
        {
            if (acquired)
            {
                _writeGate.Release();
            }
        }    }

    /// <summary>把当前进程里所有还活着的防抖文档立刻落盘（关机路径调用）。</summary>
    public static void FlushAll()
    {
        List<DebouncedDocumentFile> alive = new();

        lock (LiveLock)
        {
            for (var i = Live.Count - 1; i >= 0; i--)
            {
                if (Live[i].TryGetTarget(out var target))
                {
                    alive.Add(target);
                }
                else
                {
                    Live.RemoveAt(i);
                }
            }
        }

        foreach (var document in alive)
        {
            document.Flush();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        Flush();
        _timer.Dispose();
        _writeGate.Dispose();
    }

    private void OnTimer(object? state)
    {
        try
        {
            Flush();
        }
        catch (Exception ex)
        {
            // 计时器回调里的异常会终结进程，这里必须兜住；落盘本身失败已在 Flush 里记过日志。
            _logger.Warn("缓存防抖落盘异常（" + DisplayPath + "）：" + ex.Message);
        }
    }
}
