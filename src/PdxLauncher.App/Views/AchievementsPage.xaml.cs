namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就页：数据源是 <see cref="SteamAchievementSource"/>，真实成就通过 Steam 宿主子进程读取
/// （主进程不碰 Steam 原生 API），页面本身只消费 <see cref="AchievementSnapshot"/>。
/// 切游戏时先切到「加载中」中性态、清空旧列表，再重新取数（与 <see cref="DlcPage"/> 同一套生命周期写法）。
/// </summary>
public sealed partial class AchievementsPage : Page
{
    private readonly AchievementsViewModel _viewModel;

    /// <summary>页面生命周期令牌；页面不缓存（NavigationCacheMode=Disabled），每次进入都会重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

    public AchievementsPage()
    {
        InitializeComponent();

        _viewModel = new AchievementsViewModel(AppServices.Achievements);

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
            _ = ReloadAsync();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(() => _ = ReloadAsync()))
        {
            AppServices.Logger.Warn("AchievementsPage 无法派发游戏切换事件");
        }
    }

    private Task ReloadAsync() => _reload.RunAsync(
        token => _viewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("AchievementsPage 加载失败", ex));
}
