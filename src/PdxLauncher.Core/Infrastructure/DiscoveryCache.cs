namespace PdxLauncher.Core.Infrastructure;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 目录扫描结果的内存缓存：按 key 记住上次扫描结果，超过 TTL 或显式失效后重扫。
/// <para>
/// 存在的理由：DLC / mod 目录扫描是纯文件系统遍历，一次切换游戏会被多个页面各触发一遍
/// （主页、DLC 页、mod 页、mod 库页），实测同一次切换同一款游戏会被扫 2-3 次。
/// 缓存后同一 TTL 窗口内只扫一次，其余调用方直接拿结果。
/// </para>
/// <para>
/// 并发语义：同一 key 的并发请求共享同一个扫描任务（in-flight 去重），不会重复遍历目录；
/// 单个调用方取消只影响它自己的 <c>await</c>，不会中断共享任务，也不会把异常传染给其它调用方。
/// </para>
/// </summary>
/// <typeparam name="T">扫描结果类型。</typeparam>
public sealed class DiscoveryCache<T>
    where T : class
{
    /// <summary>默认生存期：60 秒。够挡住「切一次游戏被多处重扫」，又不会让用户装完 mod 后看不到。</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(60);

    private readonly IClock _clock;
    private readonly TimeSpan _ttl;
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<T>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    /// <summary>创建缓存。</summary>
    /// <param name="clock">时钟，用于判断 TTL。</param>
    /// <param name="ttl">生存期；null 时用 <see cref="DefaultTtl"/>。</param>
    public DiscoveryCache(IClock clock, TimeSpan? ttl = null)
    {
        ArgumentNullException.ThrowIfNull(clock);
        _clock = clock;
        _ttl = ttl ?? DefaultTtl;
    }

    /// <summary>当前缓存的条目数（含已过期但尚未被替换的）。</summary>
    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    /// <summary>
    /// 取 key 对应的扫描结果：命中未过期缓存直接返回，否则调用 <paramref name="factory"/> 扫一次并记住。
    /// </summary>
    /// <param name="key">缓存键（建议包含 gameId 与扫描根目录）。</param>
    /// <param name="factory">真正的扫描逻辑；不要在里面回写 UI。</param>
    /// <param name="cancellationToken">调用方取消令；只中断本次等待，不中断共享扫描。</param>
    /// <returns>扫描结果。</returns>
    public async Task<T> GetOrCreateAsync(
        string key,
        Func<Task<T>> factory,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        cancellationToken.ThrowIfCancellationRequested();

        Task<T> work;
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                if (_clock.UtcNow - cached.StoredAt < _ttl)
                {
                    return cached.Value;
                }

                _entries.Remove(key);
            }

            if (!_inFlight.TryGetValue(key, out var running))
            {
                // 扫描体本身在 Task.Run 里跑（factory 已自带），这里再包一层只是保证不在锁内启动。
                running = Task.Run(() => RunAsync(key, factory));
                _inFlight[key] = running;
            }

            work = running;
        }

        return await work.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>让某个 key 立即失效（用户显式刷新时调用）。</summary>
    /// <param name="key">缓存键。</param>
    /// <returns>确实删掉了条目时 true。</returns>
    public bool Invalidate(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        lock (_gate)
        {
            return _entries.Remove(key);
        }
    }

    /// <summary>清空全部缓存（测试与设置页「清缓存」用）。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private async Task<T> RunAsync(string key, Func<Task<T>> factory)
    {
        try
        {
            var value = await factory().ConfigureAwait(false);
            lock (_gate)
            {
                _entries[key] = new Entry(value, _clock.UtcNow);
            }

            return value;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(key);
            }
        }
    }

    private readonly record struct Entry(T Value, DateTimeOffset StoredAt);
}
