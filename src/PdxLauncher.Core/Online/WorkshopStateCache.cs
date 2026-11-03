namespace PdxLauncher.Core.Online;

using System;
using System.Collections.Generic;

/// <summary>
/// 「我这个账号在这款游戏下订阅了哪些、各自什么状态」的内存缓存，按 appid 分键。
/// <para>
/// 每次查询都要拉起一个 <c>--steam-host</c> 子进程（约 1 秒），而换页、切排序、刷新缩略图都会
/// 重新要一遍同样的数据。这里给一个短 TTL 的缓存，避免一屏内反复起进程。
/// </para>
/// <para>
/// 只放内存、不落盘：Steam 状态随时在变（下载在跑、用户可能在 Steam 客户端里退订），
/// 落盘的旧状态比没有状态更误导。
/// </para>
/// </summary>
public sealed class WorkshopStateCache
{
    /// <summary>默认新鲜期：1 分钟。下载进度会变，再长就明显过时了。</summary>
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(1);

    private readonly object _gate = new();
    private readonly Dictionary<int, Entry> _entries = new();
    private readonly TimeSpan _ttl;
    private readonly Func<DateTimeOffset> _now;

    /// <summary>构造。</summary>
    /// <param name="ttl">新鲜期；为 null 时用 <see cref="DefaultTtl"/>。</param>
    /// <param name="now">取当前时间（测试可注入）。</param>
    public WorkshopStateCache(TimeSpan? ttl = null, Func<DateTimeOffset>? now = null)
    {
        _ttl = ttl ?? DefaultTtl;
        _now = now ?? (static () => DateTimeOffset.UtcNow);
    }

    /// <summary>缓存键：同一个 appid 一份。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <returns>键字符串。</returns>
    public static string Key(int appId) => "steam/" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>
    /// 取一份仍然新鲜的快照；没有或已过期时返回 null。
    /// </summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="states">状态表（工坊 id → 状态）。</param>
    /// <returns>是否命中且新鲜。</returns>
    public bool TryGet(int appId, out IReadOnlyDictionary<ulong, Steam.WorkshopInstallState> states)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(appId, out var entry) && _now() - entry.FetchedUtc < _ttl)
            {
                states = entry.States;
                return true;
            }
        }

        states = EmptyStates;
        return false;
    }

    /// <summary>是否有一份（哪怕过期的）旧数据，用于请求失败时回退显示。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="states">旧状态表。</param>
    /// <returns>是否存在。</returns>
    public bool TryGetStale(int appId, out IReadOnlyDictionary<ulong, Steam.WorkshopInstallState> states)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(appId, out var entry))
            {
                states = entry.States;
                return true;
            }
        }

        states = EmptyStates;
        return false;
    }

    /// <summary>写入一份新快照。</summary>
    /// <param name="appId">Steam appid。</param>
    /// <param name="states">状态表。</param>
    public void Set(int appId, IReadOnlyDictionary<ulong, Steam.WorkshopInstallState> states)
    {
        ArgumentNullException.ThrowIfNull(states);
        lock (_gate)
        {
            _entries[appId] = new Entry(new Dictionary<ulong, Steam.WorkshopInstallState>(states), _now());
        }
    }

    /// <summary>丢掉某个 appid 的快照（写操作成功后调用，逼下次重读）。</summary>
    /// <param name="appId">Steam appid。</param>
    public void Invalidate(int appId)
    {
        lock (_gate)
        {
            _entries.Remove(appId);
        }
    }

    /// <summary>清空全部缓存。</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
        }
    }

    private static readonly IReadOnlyDictionary<ulong, Steam.WorkshopInstallState> EmptyStates =
        new Dictionary<ulong, Steam.WorkshopInstallState>();

    private sealed record Entry(IReadOnlyDictionary<ulong, Steam.WorkshopInstallState> States, DateTimeOffset FetchedUtc);
}
