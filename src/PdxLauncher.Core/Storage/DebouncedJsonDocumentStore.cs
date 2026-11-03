namespace PdxLauncher.Core.Storage;

using System.Text;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 给 <see cref="IJsonDocumentStore"/> 加一层「攒 500ms 再落盘」的装饰器。
/// <para>
/// 主题素材、DLC 简介、新闻、mod 缩略图四个缓存走的是 <see cref="DebouncedDocumentFile"/>；
/// 译文缓存直接吃 <see cref="IJsonDocumentStore"/>，每写一条译文就整份序列化一次，
/// 连续翻译时同一个键会被反复重写。这里按同样的语义补上：
/// 写入只进内存字典并重启计时器，计时器到点或显式 <see cref="Flush"/> 才真正落库。
/// </para>
/// <para>
/// 读的时候先看未落盘的 pending，保证调用方立刻读到最新内容；
/// 删除会取消同键的 pending；落盘失败只 Warn 并保留 pending，下次再试。
/// </para>
/// </summary>
public sealed class DebouncedJsonDocumentStore : IJsonDocumentStore, IDisposable
{
    /// <summary>默认合并窗口。</summary>
    public static readonly TimeSpan DefaultDelay = TimeSpan.FromMilliseconds(500);

    private static readonly List<WeakReference<DebouncedJsonDocumentStore>> Live = [];
    private static readonly object LiveLock = new();

    private readonly IJsonDocumentStore _inner;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _delay;
    private readonly Timer _timer;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sync = new();
    private readonly Dictionary<string, string> _pending = new(StringComparer.Ordinal);

    private bool _disposed;

    /// <summary>构造并注册进 <see cref="FlushAll"/> 的存活列表。</summary>
    /// <param name="inner">真正落库的存储。</param>
    /// <param name="logger">日志。</param>
    /// <param name="delay">合并窗口；默认 <see cref="DefaultDelay"/>，负值抛异常。</param>
    public DebouncedJsonDocumentStore(IJsonDocumentStore inner, IAppLogger logger, TimeSpan? delay = null)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(logger);

        var effective = delay ?? DefaultDelay;
        if (effective < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "合并窗口不能为负。");
        }

        _inner = inner;
        _logger = logger;
        _delay = effective;
        _timer = new Timer(OnTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        lock (LiveLock)
        {
            Live.Add(new WeakReference<DebouncedJsonDocumentStore>(this));
        }
    }

    /// <inheritdoc />
    public bool TryRead(string key, out string? json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            if (_pending.TryGetValue(key, out var pending))
            {
                json = pending;
                return true;
            }
        }

        return _inner.TryRead(key, out json);
    }

    /// <inheritdoc />
    public bool Write(string key, string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(json);

        lock (_sync)
        {
            if (_disposed)
            {
                return false;
            }

            _pending[key] = json;
            _timer.Change(_delay, Timeout.InfiniteTimeSpan);
        }

        return true;
    }

    /// <inheritdoc />
    public bool Delete(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            _pending.Remove(key);
            if (_pending.Count == 0)
            {
                _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }
        }

        return _inner.Delete(key);
    }

    /// <inheritdoc />
    public bool Exists(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        lock (_sync)
        {
            if (_pending.ContainsKey(key))
            {
                return true;
            }
        }

        return _inner.Exists(key);
    }

    /// <inheritdoc />
    public long MeasureBytes(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var total = _inner.MeasureBytes(prefix);
        lock (_sync)
        {
            foreach (var pair in _pending)
            {
                if (pair.Key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    total += Encoding.UTF8.GetByteCount(pair.Value);
                }
            }
        }

        return total;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List(string prefix)
    {
        ArgumentNullException.ThrowIfNull(prefix);

        var keys = new SortedSet<string>(_inner.List(prefix), StringComparer.Ordinal);
        lock (_sync)
        {
            foreach (var key in _pending.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    keys.Add(key);
                }
            }
        }

        return keys.ToList();
    }

    /// <summary>把攒着的写入立刻落库；失败只 Warn，pending 保留到下次。</summary>
    public bool Flush()
    {
        List<KeyValuePair<string, string>> snapshot;
        lock (_sync)
        {
            if (_disposed || _pending.Count == 0)
            {
                return true;
            }

            snapshot = [.. _pending];
        }

        var acquired = false;
        try
        {
            _writeGate.Wait();
            acquired = true;

            var allOk = true;
            foreach (var pair in snapshot)
            {
                if (!_inner.Write(pair.Key, pair.Value))
                {
                    allOk = false;
                    _logger.Warn("缓存落盘失败（" + pair.Key + "）。");
                    continue;
                }

                lock (_sync)
                {
                    // 落盘期间又写了更新的内容：保留新的，等下一轮。
                    if (_pending.TryGetValue(pair.Key, out var current) && ReferenceEquals(current, pair.Value))
                    {
                        _pending.Remove(pair.Key);
                    }
                }
            }

            return allOk;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
        catch (Exception exception)
        {
            _logger.Warn("缓存落盘失败：" + exception.Message);
            return false;
        }
        finally
        {
            if (acquired)
            {
                _writeGate.Release();
            }
        }
    }

    /// <summary>把所有存活的装饰器都 Flush 一遍（窗口关闭时调用）。</summary>
    public static void FlushAll()
    {
        List<DebouncedJsonDocumentStore> alive = [];
        lock (LiveLock)
        {
            for (var index = Live.Count - 1; index >= 0; index--)
            {
                if (Live[index].TryGetTarget(out var target))
                {
                    alive.Add(target);
                }
                else
                {
                    Live.RemoveAt(index);
                }
            }
        }

        foreach (var store in alive)
        {
            store.Flush();
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

            _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        // 先落盘再标记已释放：Flush 会检查 _disposed，顺序反了 pending 就永远写不出去。
        Flush();

        lock (_sync)
        {
            _disposed = true;
        }

        _timer.Dispose();
        _writeGate.Dispose();
    }

    private void OnTimer(object? state)
    {
        try
        {
            Flush();
        }
        catch (Exception exception)
        {
            // 计时器回调里的异常会终结进程，兜底写一条就够。
            _logger.Warn("缓存落盘计时器异常：" + exception.Message);
        }
    }
}
