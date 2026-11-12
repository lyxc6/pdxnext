namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Imaging;

/// <summary>
/// 成就页：数据源是 <see cref="SteamAchievementSource"/>，真实成就通过 Steam 宿主子进程读取
/// （主进程不碰 Steam 原生 API），页面本身只消费 <see cref="AchievementSnapshot"/>。
/// 切游戏时先切到「加载中」中性态、清空旧列表，再重新取数（与 <see cref="DlcPage"/> 同一套生命周期写法）。
/// 页面自身缓存（NavigationCacheMode=Required）：快速来回切页复用同一份 ViewModel 与列表，
/// 短时间内重进不重复拉起 Steam 宿主子进程。
/// </summary>
public sealed partial class AchievementsPage : Page
{
    /// <summary>同一款游戏在这个窗口内重进页面时直接复用已有列表，不再起子进程。</summary>
    private static readonly TimeSpan ReloadFreshWindow = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 成就图标的解码尺寸：行模板显示 48 DIP，按 2 倍缩放取 96 像素；
    /// <see cref="DdsPngCache"/> 只缩不放，64×64 的 CK3 原图因此保持原样，不会被放大糊掉。
    /// </summary>
    private const int IconPngMaxDimension = 96;

    private readonly AchievementsViewModel _viewModel;
    private readonly AchievementSnapshotCache _snapshotCache;

    /// <summary>页面生命周期令牌；页面缓存复用，但取数令牌随每次进入/离开重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    /// <summary>上一次开始加载的游戏键与完成时间，用于「快速来回切页不重复起子进程」。</summary>
    private string? _lastLoadGameKey;
    private DateTimeOffset _lastLoadCompletedUtc = DateTimeOffset.MinValue;

    public AchievementsPage()
    {
        InitializeComponent();

        _snapshotCache = new AchievementSnapshotCache(
            AppServices.FileSystem,
            Path.Combine(AppServices.DataRoot, "achievement-cache"));
        var iconPngCache = new DdsPngCache(
            Path.Combine(AppServices.DataRoot, "achievement-icons"),
            IconPngMaxDimension);
        _viewModel = new AchievementsViewModel(AppServices.Achievements, _snapshotCache, iconPngCache);

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public AchievementsViewModel ViewModel => _viewModel;

    /// <summary>XAML 里 <c>{x:Bind L.Xxx, Mode=OneWay}</c> 的绑定入口。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            AppServices.CurrentGameChanged -= OnCurrentGameChanged;
            AppServices.CurrentGameChanged += OnCurrentGameChanged;

            await ReloadAsync().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("AchievementsPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;

        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        // 用户切游戏时事件就在 UI 线程上：直接开始取数，少一帧上一款游戏的残留。
        // 后台刷新（主题重建 GameInstallation）仍走派发，避免跨线程碰 ViewModel。
        if (DispatcherQueue.HasThreadAccess)
        {
            _ = ReloadAsync(force: true);
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync(force: true)))
        {
            AppServices.Logger.Warn("AchievementsPage 无法派发游戏切换事件");
        }
    }

    /// <summary>
    /// 搜索框输入：只在用户真的打字时把文本回写 ViewModel。
    /// 程序化改写（切游戏重置、绑定回写）进来时 Reason 不是 UserInput，必须直接返回，
    /// 否则会和 ViewModel 的重置互相触发，还会把光标位置抢回去。
    /// </summary>
    private void OnSearchTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            return;
        }

        var text = sender.Text ?? string.Empty;
        if (!string.Equals(text, _viewModel.SearchText, StringComparison.Ordinal))
        {
            _viewModel.SearchText = text;
        }
    }

    private Task ReloadAsync(bool force = false)
    {
        var game = AppServices.CurrentGame;
        var gameKey = GameKey(game);

        // 同一款游戏正在后台刷新：复用这次在途请求，不再拉第二个子进程。
        if (!force && _reload.IsReloading && string.Equals(gameKey, _lastLoadGameKey, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // 页面缓存命中 + 刚才已经成功取过数：直接复用列表，首屏不再等任何 IO/子进程。
        if (!force
            && game is not null
            && _viewModel.HasSnapshot
            && string.Equals(gameKey, _lastLoadGameKey, StringComparison.Ordinal)
            && DateTimeOffset.UtcNow - _lastLoadCompletedUtc < ReloadFreshWindow)
        {
            return Task.CompletedTask;
        }

        _lastLoadGameKey = gameKey;
        return _reload.RunAsync(
            async token =>
            {
                await _viewModel.LoadAsync(game, token).ConfigureAwait(true);
                if (!token.IsCancellationRequested)
                {
                    _lastLoadCompletedUtc = DateTimeOffset.UtcNow;
                }
            },
            _lifetime.Token,
            ex => AppServices.Logger.Error("AchievementsPage 加载失败", ex));
    }

    /// <summary>缓存键与 ViewModel 保持一致：Steam 游戏用 appid，其余用 gameId。</summary>
    private static string? GameKey(GameInstallation? game) =>
        game is null
            ? null
            : game.SteamAppId is > 0
                ? game.SteamAppId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : game.GameId;
}
