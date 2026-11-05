namespace PdxLauncher.App.Services;

using System.Globalization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 启动预热：主窗口可见后（以及切游戏 debounce 之后）在后台读一次当前游戏的成就并写快照缓存。
/// <para>
/// 目的只有一个：把 <c>SteamAPI_Init</c> 与 <c>RequestUserStats</c> 的等待挪到用户还没点进成就页的时候。
/// 之后进页面先渲染缓存、后台再刷新；即使预热失败也只是少一次「瞬间出结果」，绝不影响任何功能，
/// 所以这里的所有异常都只记日志、不弹窗、不重试、不阻塞启动。
/// </para>
/// <para>
/// 缓存键与成就页完全一致（<c>SteamAppId &gt; 0 ? appid : gameId</c>），目录同样是
/// <c>&lt;data&gt;\achievement-cache</c>；写入走 <see cref="AchievementSnapshotCache.TrySave"/> 的
/// tmp+rename 原子写，页面同时刷新也不会读到半截文件。
/// </para>
/// </summary>
public sealed class AchievementPrewarmService : IDisposable
{
    /// <summary>切游戏后的去抖：连续切几个游戏只预热最后停下的那个。</summary>
    public static readonly TimeSpan SwitchDebounce = TimeSpan.FromSeconds(2);

    /// <summary>启动预热的延迟：让首屏、主题、看门狗先跑完，别和主线程抢磁盘 / CPU。</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(2);

    /// <summary>成就快照缓存目录名（与成就页共用）。</summary>
    public const string CacheDirectoryName = "achievement-cache";

    private readonly IAchievementSource _source;
    private readonly AchievementSnapshotCache _cache;
    private readonly Func<GameInstallation?> _currentGame;
    private readonly IAppLogger _logger;
    private readonly TimeSpan _switchDebounce;
    private readonly TimeSpan _startupDelay;
    private readonly object _state = new();

    private CancellationTokenSource? _pending;
    private Task _pendingTask = Task.CompletedTask;
    private bool _started;
    private bool _disposed;
    private int _prewarmCount;

    /// <summary>构造。</summary>
    /// <param name="source">成就数据源（真实实现会拉起 / 复用 Steam 宿主）。</param>
    /// <param name="cache">快照缓存。</param>
    /// <param name="currentGame">取当前游戏；每次预热时现取。</param>
    /// <param name="logger">日志。</param>
    /// <param name="switchDebounce">切游戏去抖（自检可缩短）。</param>
    /// <param name="startupDelay">启动延迟（自检可缩短）。</param>
    public AchievementPrewarmService(
        IAchievementSource source,
        AchievementSnapshotCache cache,
        Func<GameInstallation?> currentGame,
        IAppLogger logger,
        TimeSpan? switchDebounce = null,
        TimeSpan? startupDelay = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _currentGame = currentGame ?? throw new ArgumentNullException(nameof(currentGame));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _switchDebounce = switchDebounce is { Ticks: >= 0 } debounce ? debounce : SwitchDebounce;
        _startupDelay = startupDelay is { Ticks: >= 0 } delay ? delay : StartupDelay;
    }

    /// <summary>是否已经 Start 过（Start 幂等）。</summary>
    public bool IsStarted
    {
        get
        {
            lock (_state)
            {
                return _started;
            }
        }
    }

    /// <summary>成功写回快照的次数（自检用）。</summary>
    public int PrewarmCount => Volatile.Read(ref _prewarmCount);

    /// <summary>最后一次成功写回的缓存键（自检用）。</summary>
    public string? LastCacheKey { get; private set; }

    /// <summary>最后一次失败说明（自检用；成功不影响它，便于定位「为什么没写」）。</summary>
    public string? LastError { get; private set; }

    /// <summary>第一次预热要等多久（App 在窗口 Activate 之后调用）。</summary>
    public void Start()
    {
        lock (_state)
        {
            if (_started || _disposed)
            {
                return;
            }

            _started = true;
        }

        Schedule(_startupDelay);
    }

    /// <summary>当前游戏变了：去抖后预热新游戏；未 Start 时忽略（用户还停在首屏）。</summary>
    public void NotifyGameChanged()
    {
        lock (_state)
        {
            if (!_started || _disposed)
            {
                return;
            }
        }

        Schedule(_switchDebounce);
    }

    /// <summary>等当前这次（去抖 + 取数）跑完；自检用。</summary>
    public Task WaitForIdleAsync()
    {
        lock (_state)
        {
            return _pendingTask;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        CancellationTokenSource? pending;
        lock (_state)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            pending = _pending;
            _pending = null;
        }

        try
        {
            pending?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // 已经跑完了。
        }
    }

    private void Schedule(TimeSpan delay)
    {
        CancellationTokenSource cts;
        lock (_state)
        {
            if (_disposed)
            {
                return;
            }

            var previous = _pending;
            cts = new CancellationTokenSource();
            _pending = cts;
            _pendingTask = RunAsync(delay, cts);
            if (previous is not null)
            {
                try
                {
                    previous.Cancel();
                }
                catch (ObjectDisposedException)
                {
                    // 上一轮已经结束。
                }
            }
        }
    }

    private async Task RunAsync(TimeSpan delay, CancellationTokenSource cts)
    {
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cts.Token).ConfigureAwait(false);
            }

            await PrewarmAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 切游戏 / 退出：正常路径，不记日志。
        }
        catch (Exception ex)
        {
            LastError = ex.GetType().Name + "：" + ex.Message;
            _logger.Warn("预热成就失败（不影响功能）：" + LastError);
        }
    }

    private async Task PrewarmAsync(CancellationToken cancellationToken)
    {
        var game = _currentGame();
        if (game is null)
        {
            return;
        }

        // 只预热真的是 Steam 游戏、且数据源认得的条目；其余留给页面自己的空状态。
        if (game.SteamAppId is not > 0)
        {
            return;
        }

        var cacheKey = game.SteamAppId.Value.ToString(CultureInfo.InvariantCulture);
        if (_cache.PathFor(cacheKey) is null)
        {
            return;
        }

        _logger.Info("开始预热成就（appid " + cacheKey + "）");

        var snapshot = await _source.GetAsync(game.GameId, cancellationToken).ConfigureAwait(false);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        // 空快照不能落盘：一旦把「Available + 0 条」写进缓存，页面之后就会一直复用这份空快照
        // （缓存命中直接渲染），哪怕下一次宿主能正常返回。等下一次真正读到条目再覆盖。
        if (snapshot.Availability != AchievementAvailability.Available || snapshot.Entries is not { Count: > 0 })
        {
            _logger.Info("预热成就跳过（appid " + cacheKey + "）：" + snapshot.Availability + "，" + (snapshot.Entries?.Count ?? 0) + " 条");
            return;
        }

        // 去抖窗口里可能又切走了；旧游戏的快照写回去会盖掉新游戏的缓存。
        var current = _currentGame();
        if (current is null
            || !string.Equals(current.GameId, game.GameId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (_cache.TrySave(cacheKey, snapshot))
        {
            LastCacheKey = cacheKey;
            Interlocked.Increment(ref _prewarmCount);
            _logger.Info("预热成就已写快照（appid " + cacheKey + "，" + (snapshot.Entries?.Count ?? 0) + " 条）");
        }
        else
        {
            LastError = "写快照失败";
            _logger.Warn("预热成就已取到数据，但写快照失败（appid " + cacheKey + "）");
        }
    }
}
