namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Imaging;

/// <summary>
/// 成就页的视图模型：向 <see cref="IAchievementSource"/> 取当前游戏的成就快照，
/// 把可用性翻译成明确的空状态说明，并维护「已解锁 X / Y」统计。
/// <para>
/// 不含任何 Microsoft.UI.Xaml 类型（项目 MVVM 分层硬约束）：
/// 空状态、稀有度、解锁时间都只产出 string / bool，可见性交给 XAML 的 XamlVisibility。
/// </para>
/// <para>
/// 切游戏时先清空旧列表再取数；数据源不可用时列表为空且显示原因，
/// 绝不把「没有数据源」渲染成「这款游戏没有成就」。
/// </para>
/// </summary>
public sealed class AchievementsViewModel : LocalizedObservableObject
{
    private readonly IAchievementSource _source;
    private readonly AchievementRequestTracker _requests = new();
    private readonly AchievementSnapshotCache? _cache;
    private readonly DdsPngCache? _iconPngCache;

    private GameInstallation? _game;
    private AchievementAvailability _availability = AchievementAvailability.NotSupported;
    private bool _isBusy;
    private DateTimeOffset? _lastUpdatedUtc;
    private string? _renderedGameKey;
    private bool _hasRenderedSnapshot;

    /// <summary>构造（不带磁盘缓存，数据源直读；自检与降级路径用）。</summary>
    public AchievementsViewModel(IAchievementSource source)
        : this(source, cache: null, iconPngCache: null)
    {
    }

    /// <summary>
    /// 构造。<paramref name="cache"/> 非空时进页面先渲染上次快照，再后台刷新；
    /// 刷新失败保留缓存，<see cref="LastUpdatedText"/> 因此显示的是「上次成功更新」的时间。
    /// </summary>
    public AchievementsViewModel(IAchievementSource source, AchievementSnapshotCache? cache)
        : this(source, cache, iconPngCache: null)
    {
    }

    /// <summary>
    /// 构造。<paramref name="iconPngCache"/> 非空时把 CK3 的 DDS 图标惰性解码成 PNG 并缓存；
    /// 为空时 DDS 图标直接当作没有图标（界面显示占位方块），绝不把 DDS 路径交给 BitmapImage。
    /// </summary>
    public AchievementsViewModel(
        IAchievementSource source,
        AchievementSnapshotCache? cache,
        DdsPngCache? iconPngCache)
    {
        ArgumentNullException.ThrowIfNull(source);
        _source = source;
        _cache = cache;
        _iconPngCache = iconPngCache;
    }

    /// <summary>当前展示的成就行；切游戏 / 重新取数时整体清空重建。</summary>
    public ObservableCollection<AchievementRowViewModel> Achievements { get; } = new();

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));

                // EmptyStateMessage 的「请先选游戏」分支依赖 Game：首次取数时 Game 从 null 变成实例，
                // 但 Availability 可能仍是 NotSupported（初值相同、不会触发通知），
                // 这里必须显式通知，否则页面会一直停在「请先在游戏库中选择一款游戏」。
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.AchievementsUnavailableNoGame);

    /// <summary>列表里是否已有可渲染的快照（来自缓存或上一次成功刷新）。</summary>
    public bool HasSnapshot => _hasRenderedSnapshot;

    /// <summary>「上次更新」文案；没有成功取过数时为空，界面隐藏该行。</summary>
    public string LastUpdatedText => _lastUpdatedUtc is { } updatedUtc
        ? T(Keys.AchievementsLastUpdated, updatedUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>是否显示「上次更新」；从没成功取过数时不显示，避免出现一个空时间。</summary>
    public bool HasLastUpdated => _lastUpdatedUtc.HasValue;

    public AchievementAvailability Availability
    {
        get => _availability;
        private set
        {
            if (SetProperty(ref _availability, value))
            {
                OnPropertyChanged(nameof(IsAvailable));
                OnPropertyChanged(nameof(IsLoading));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(EmptyStateTitle));
                OnPropertyChanged(nameof(EmptyStateMessage));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set => SetProperty(ref _isBusy, value);
    }

    /// <summary>是否正在取数：为 true 时页面只显示进度环，不显示空状态说明。</summary>
    public bool IsLoading => Availability == AchievementAvailability.Loading;

    public int TotalCount => Achievements.Count;

    public int UnlockedCount => Achievements.Count(static row => row.IsUnlocked);

    /// <summary>
    /// 顶部统计文案：「已解锁 X / Y」。
    /// <para>
    /// 不可用 / 取数中时显示破折号：显示「已解锁 0 / 0」会把「连不上 Steam」说成「一个都没解锁」。
    /// </para>
    /// </summary>
    public string CountsText => IsAvailable
        ? T(Keys.AchievementsCounts, UnlockedCount, TotalCount)
        : "—";

    /// <summary>数据源可用且列表非空；为 false 时页面显示空状态说明而不是空白列表。</summary>
    public bool IsAvailable => Availability == AchievementAvailability.Available;

    /// <summary>只有在「确定没数据且不忙」时才显示空状态，避免取数期间闪一下空白提示。</summary>
    public bool ShowEmptyState => !IsAvailable && !IsLoading;

    public string EmptyStateTitle => ShowEmptyState ? T(Keys.AchievementsEmptyTitle) : string.Empty;

    /// <summary>
    /// 空状态原因：没有当前游戏时先提示选游戏，其余按可用性给出对应文案。
    /// 三条不可用文案（未接入 / 连不上 Steam / 确实没有成就）必须各自独立，不能合并。
    /// </summary>
    public string EmptyStateMessage
    {
        get
        {
            if (Game is null)
            {
                return T(Keys.AchievementsUnavailableNoGame);
            }

            if (IsLoading)
            {
                // 取数中不展示任何「不可用」结论；页面此刻显示的是进度环。
                return string.Empty;
            }

            return Availability switch
            {
                AchievementAvailability.NotSupported => T(Keys.AchievementsUnavailableNotSupported),
                AchievementAvailability.SteamUnavailable => T(Keys.AchievementsUnavailableSteamUnavailable),
                AchievementAvailability.Empty => T(Keys.AchievementsUnavailableEmpty),
                _ => string.Empty,
            };
        }
    }

    /// <summary>
    /// 取当前游戏的成就。切游戏时旧列表立即清空；同一款游戏重新进页面时先用磁盘缓存渲染，
    /// 再后台刷新，刷新期间绝不清空列表。
    /// <para>
    /// 只有最新一代请求可以落地：旧请求被取消后仍会执行 <c>finally</c>，
    /// 由 <see cref="AchievementRequestTracker"/> 判断代次，旧请求不会关掉新请求的 <see cref="IsBusy"/>。
    /// </para>
    /// <para>
    /// 刷新失败（Steam 不可用 / 数据源未接入）且已有缓存时保留缓存与「上次更新」时间：
    /// 用户看到的是「上次成功的成就」，而不是一屏错误或空列表。
    /// </para>
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        var generation = _requests.Begin();

        var gameKey = CacheKey(game);
        var gameChanged = !string.Equals(_renderedGameKey, gameKey, StringComparison.Ordinal);
        Game = game;

        if (game is null)
        {
            _renderedGameKey = null;
            _hasRenderedSnapshot = false;
            Achievements.Clear();
            SetLastUpdated(null);
            NotifyCountsChanged();
            IsBusy = false;
            Availability = AchievementAvailability.NotSupported;
            return;
        }

        if (gameChanged)
        {
            // 切游戏：上一款的成就一条都不能留在屏幕上。
            Achievements.Clear();
            NotifyCountsChanged();
            _hasRenderedSnapshot = false;
            SetLastUpdated(null);

            var cached = TryLoadCache(game);
            if (cached is not null)
            {
                Apply(game, cached);
                _hasRenderedSnapshot = true;
                _renderedGameKey = gameKey;
                SetLastUpdated(cached.FetchedAtUtc);
            }
        }

        // 有缓存就先显示缓存（统计、列表都在），刷新期间只转圈；没有缓存才回到中性「—」态。
        if (!_hasRenderedSnapshot)
        {
            Availability = AchievementAvailability.Loading;
        }
        IsBusy = true;
        try
        {
            var snapshot = await _source.GetAsync(game.GameId, cancellationToken);
            if (!_requests.IsCurrent(generation))
            {
                // 旧请求：结果属于上一款游戏，既不能写列表，也不能改 IsBusy。
                return;
            }

            cancellationToken.ThrowIfCancellationRequested();

            if (ShouldKeepCached(snapshot))
            {
                // 刷新失败：保留缓存列表与上次更新时间，IsBusy 在 finally 关掉。
                return;
            }

            Apply(game, snapshot);
            _hasRenderedSnapshot = Achievements.Count > 0;
            _renderedGameKey = gameKey;
            SetLastUpdated(snapshot.FetchedAtUtc);

            // 只缓存「真的取到了成就」的快照：数据源说 Available 但返回空数组，
            // 在旧机器上就是「一条成就都没有」的假象，一旦写盘会被下一次启动原样复现。
            if (snapshot.Availability == AchievementAvailability.Available && snapshot.Entries is { Count: > 0 })
            {
                TrySaveCache(game, snapshot);
            }
        }
        finally
        {
            if (_requests.IsCurrent(generation))
            {
                IsBusy = false;
            }
        }
    }

    private void Apply(GameInstallation game, AchievementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var entries = snapshot.Entries ?? [];
        Achievements.Clear();
        foreach (var entry in entries)
        {
            // 惰性：行容器实化并读取 IconUrl 时才解析 / 解码，198 个 DDS 不会在进页面时同步解开。
            Achievements.Add(new AchievementRowViewModel(entry, () => ResolveIcon(game, entry)));
        }

        // 数据源说「可用」但一条都没有：按 Empty 解释，页面才有话说，而不是留一块空白。
        Availability = snapshot.Availability == AchievementAvailability.Available && entries.Count == 0
            ? AchievementAvailability.Empty
            : snapshot.Availability;

        NotifyCountsChanged();
    }

    /// <summary>
    /// 失败且已有可显示的非空快照时保留缓存；没有缓存（或缓存是空列表）则照常把失败原因写进空状态。
    /// </summary>
    private bool ShouldKeepCached(AchievementSnapshot snapshot) =>
        _hasRenderedSnapshot
        && Achievements.Count > 0
        && snapshot.Availability is AchievementAvailability.NotSupported or AchievementAvailability.SteamUnavailable;

    /// <summary>
    /// 解析一行的图标：位图直接用；DDS 先解码落 PNG 再返回 PNG 路径。
    /// 任何一步失败都返回 <see langword="null"/>（界面显示占位方块），且绝不返回 DDS 路径。
    /// </summary>
    private string? ResolveIcon(GameInstallation game, AchievementEntry entry)
    {
        try
        {
            var source = AchievementIconResolver.ResolveSource(game.InstallRoot, entry.Id, entry.IsUnlocked);
            if (source is null)
            {
                return null;
            }

            if (!source.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                return source;
            }

            if (_iconPngCache is null)
            {
                return null;
            }

            var relative = IconCacheRelativePath(game, entry);
            return relative is null ? null : _iconPngCache.Ensure(source, relative);
        }
        catch
        {
            // 图标只是装饰：解析失败就回占位方块，绝不能阻断成就列表。
            return null;
        }
    }

    /// <summary>成就图标的 PNG 缓存相对路径：<c>&lt;appid&gt;\&lt;apiName&gt;[_locked].png</c>。</summary>
    private static string? IconCacheRelativePath(GameInstallation game, AchievementEntry entry)
    {
        if (CacheKey(game) is not { } key)
        {
            return null;
        }

        var stem = entry.IsUnlocked ? entry.Id : entry.Id + "_locked";
        return Path.Combine(key, stem + ".png");
    }

    private AchievementSnapshot? TryLoadCache(GameInstallation game)
    {
        if (_cache is null || CacheKey(game) is not { } key)
        {
            return null;
        }

        var cached = _cache.TryLoad(key);
        if (cached is null)
        {
            return null;
        }

        // 旧版本把「Availability=Available 但 Entries=[]」当成功写进了缓存。
        // 这种毒快照会让老机器每次启动都直接显示「没有成就」，必须当作未命中并清掉。
        if (cached.Availability == AchievementAvailability.Available && (cached.Entries?.Count ?? 0) == 0)
        {
            _cache.TryDelete(key);
            return null;
        }

        return cached;
    }

    private void TrySaveCache(GameInstallation game, AchievementSnapshot snapshot)
    {
        if (_cache is null || CacheKey(game) is not { } key)
        {
            return;
        }

        _cache.TrySave(key, snapshot);
    }

    /// <summary>缓存键：Steam 游戏用 appid（与磁盘文件名一一对应），其余退回 gameId。</summary>
    private static string? CacheKey(GameInstallation? game)
    {
        if (game is null)
        {
            return null;
        }

        return game.SteamAppId is > 0
            ? game.SteamAppId.Value.ToString(CultureInfo.InvariantCulture)
            : game.GameId;
    }

    private void SetLastUpdated(DateTimeOffset? value)
    {
        if (_lastUpdatedUtc == value)
        {
            return;
        }

        _lastUpdatedUtc = value;
        OnPropertyChanged(nameof(LastUpdatedText));
        OnPropertyChanged(nameof(HasLastUpdated));
    }

    private void NotifyCountsChanged()
    {
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(UnlockedCount));
        OnPropertyChanged(nameof(CountsText));
    }
}
