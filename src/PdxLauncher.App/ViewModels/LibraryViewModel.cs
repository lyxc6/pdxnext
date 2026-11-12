namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 游戏库视图模型：卡片列表 + 选中游戏 + 卡片上的直接启动。
/// 卡片只暴露字符串路径（封面/Logo），图片由 XAML 侧转换；本类不引用任何 Microsoft.UI.Xaml 类型。
/// </summary>
public sealed partial class LibraryViewModel : LocalizedObservableObject
{
    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private GameCardItem? _selectedCard;

    /// <summary>当前状态栏文案的键与参数：切语言时用它们重算。</summary>
    private string? _statusKey;

    private object?[] _statusArgs = [];

    /// <summary>卡片网格数据源。</summary>
    public ObservableCollection<GameCardItem> Cards { get; } = new();

    public bool HasCards => Cards.Count > 0;

    public bool HasNoCards => Cards.Count == 0;

    public string CountText => T(Keys.LibraryCount, Cards.Count);

    /// <summary>用户点了卡片：页面据此导航到主页。</summary>
    public event EventHandler<GameCardItem>? GameActivated;

    /// <summary>卡片上的"启动"按钮完成（成功或失败）后触发，页面显示 InfoBar。</summary>
    public event EventHandler<ProcessStartResult>? LaunchCompleted;

    /// <summary>刷新按钮可用性：扫描进行中时禁用，避免重复触发。</summary>
    public bool IsRefreshEnabled => !IsBusy;

    /// <summary>加载游戏库：首次调用触发真正的扫描，之后只读缓存列表。</summary>
    public Task LoadAsync(CancellationToken cancellationToken) => LoadCoreAsync(force: false, cancellationToken);

    /// <summary>强制重新扫描游戏库（右上角"刷新"）。</summary>
    public Task RefreshAsync(CancellationToken cancellationToken) => LoadCoreAsync(force: true, cancellationToken);

    private async Task LoadCoreAsync(bool force, CancellationToken cancellationToken)
    {
        IsBusy = true;
        try
        {
            var games = force
                ? await GameDiscovery.RefreshAsync(cancellationToken).ConfigureAwait(true)
                : await GameDiscovery.EnsureAsync(cancellationToken).ConfigureAwait(true);
            // 卡片封面是 OneTime 绑定，纱层背景（EU4 的 20% alpha 油画）必须先摊平再绑定。
            await AppServices.PrepareCardCoversAsync(games, cancellationToken).ConfigureAwait(true);
            RebuildCards(games);
            IsLoaded = true;
            if (Cards.Count == 0)
            {
                SetStatus(Keys.LibraryNotFound);
            }
            else
            {
                SetStatus(Keys.LibraryFound, Cards.Count);
            }
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// 主题素材被后台刷新后，用内存里的最新游戏列表重建卡片（不重新扫描 Steam 库、不联网）。
    /// </summary>
    public void ReloadFromCurrentGames()
    {
        if (!AppServices.IsInitialized)
        {
            return;
        }

        _ = ReloadFromCurrentGamesAsync();
    }

    private async Task ReloadFromCurrentGamesAsync()
    {
        var games = AppServices.Games;
        try
        {
            await AppServices.PrepareCardCoversAsync(games, CancellationToken.None).ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("卡片封面预摊平失败: " + ex.Message);
        }

        RebuildCards(games);
    }

    partial void OnIsBusyChanged(bool oldValue, bool newValue) => OnPropertyChanged(nameof(IsRefreshEnabled));

    /// <summary>激活卡片：切换当前游戏并请求进入主页。</summary>
    public void Activate(GameCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        SelectedCard = card;
        AppServices.SelectGame(card.Game);
        GameActivated?.Invoke(this, card);
    }

    /// <summary>从卡片直接启动游戏（不进入主页）。</summary>
    [RelayCommand]
    private async Task LaunchAsync(GameCardItem? card)
    {
        if (card is null)
        {
            return;
        }

        var result = await LaunchGameAsync(card.Game, CancellationToken.None).ConfigureAwait(true);
        LaunchCompleted?.Invoke(this, result);
    }

    /// <summary>启动指定游戏并返回结果；失败时只记日志，不抛异常。</summary>
    private static async Task<ProcessStartResult> LaunchGameAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await AppServices.Launcher
                .LaunchAsync(game, AppServices.CreateLaunchRequest(game, continueLastSave: false), cancellationToken)
                .ConfigureAwait(true);

            if (result.Started)
            {
                AppServices.Logger.Info(
                    "已启动 " + game.DisplayName + "（pid=" + (result.ProcessId?.ToString() ?? "未知") + "）");
            }
            else
            {
                AppServices.Logger.Warn(
                    "启动失败 " + game.DisplayName + "：" + (result.ErrorMessage ?? "未知原因"));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            return ProcessStartResult.Failure(T(Keys.LibraryLaunchCanceled));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("启动游戏异常（gameId=" + game.GameId + "）", ex);
            return ProcessStartResult.Failure(ex.Message);
        }
    }

    /// <summary>重建卡片（只 Clear + Add，不替换集合实例）。</summary>
    private void RebuildCards(IReadOnlyList<GameInstallation> games)
    {
        Cards.Clear();
        foreach (var game in games)
        {
            Cards.Add(new GameCardItem(
                game,
                game.DisplayName,
                string.IsNullOrWhiteSpace(game.Version)
                    ? T(Keys.LibraryVersionUnknown)
                    : T(Keys.LibraryVersion, game.Version),
                FirstExisting(game.Theme.Background, game.Theme.Logo),
                game.Theme.Logo,
                game.Support));
        }

        OnPropertyChanged(nameof(HasCards));
        OnPropertyChanged(nameof(HasNoCards));
        OnPropertyChanged(nameof(CountText));
    }

    /// <summary>写状态栏文案，并记住键与参数，切语言时用它们重算。</summary>
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = T(key, args);
    }

    /// <summary>切语言后重算状态栏，并重建卡片（角标与版本号的文案在卡片构造时定下）。</summary>
    protected override void Relocalize()
    {
        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }

        if (AppServices.IsInitialized && IsLoaded)
        {
            RebuildCards(AppServices.Games);
        }
    }

    /// <summary>封面优先用安装目录背景图；没有背景图就退回 logo；都没有则卡片显示内置占位。</summary>
    private static string? FirstExisting(string? primary, string? secondary)
    {
        if (!string.IsNullOrWhiteSpace(primary) && AppServices.FileSystem.FileExists(primary))
        {
            return primary;
        }

        if (!string.IsNullOrWhiteSpace(secondary) && AppServices.FileSystem.FileExists(secondary))
        {
            return secondary;
        }

        return null;
    }
}
