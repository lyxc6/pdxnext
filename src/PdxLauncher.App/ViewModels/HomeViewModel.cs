namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;

using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 当前游戏主页视图模型：主题素材路径、继续游戏信息、播放集/mod/DLC 计数、启动命令。
/// 主题的 Brush/FontFamily 由页面用 <see cref="ThemeApplier"/> 计算，本类只输出纯数据。
/// </summary>
public sealed partial class HomeViewModel : LocalizedObservableObject
{
    [ObservableProperty]
    private GameInstallation? _game;

    [ObservableProperty]
    private string _backgroundPath = string.Empty;

    [ObservableProperty]
    private string _logoPath = string.Empty;

    [ObservableProperty]
    private string _playSetName = string.Empty;

    [ObservableProperty]
    private PlaySet? _selectedPlaySet;

    [ObservableProperty]
    private int _modCount;

    [ObservableProperty]
    private int _enabledModCount;

    [ObservableProperty]
    private int _dlcCount;

    [ObservableProperty]
    private string _continueTitle = string.Empty;

    [ObservableProperty]
    private string _continueDate = string.Empty;

    [ObservableProperty]
    private string _continueDescription = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isLoaded;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    /// <summary>
    /// 公告主卡（官方槽位 main）。始终非 null —— 没有公告时是空卡，
    /// 因为 x:Bind 直接访问 NewsMain.Title 这类路径，中间对象为 null 会崩。
    /// </summary>
    [ObservableProperty]
    private NewsCardItem _newsMain = NewsCardItem.Empty(NewsFeedSlots.Main);

    /// <summary>公告副卡 1（官方槽位 secondary-1），同样始终非 null。</summary>
    [ObservableProperty]
    private NewsCardItem _newsSecondary1 = NewsCardItem.Empty(NewsFeedSlots.Secondary1);

    /// <summary>公告副卡 2（官方槽位 secondary-2），同样始终非 null。</summary>
    [ObservableProperty]
    private NewsCardItem _newsSecondary2 = NewsCardItem.Empty(NewsFeedSlots.Secondary2);

    /// <summary>载入播放列表或切换激活播放集期间为 true，避免下拉框的回写动作递归触发。</summary>
    private bool _suppressPlaySetChange;

    /// <summary>当前状态栏文案的键与参数：切语言时用它们重算，状态栏才不会停在旧语言。</summary>
    private string? _statusKey;

    private object?[] _statusArgs = [];

    /// <summary>启动完成（成功或失败）后触发，页面据此显示 InfoBar。</summary>
    public event EventHandler<ProcessStartResult>? LaunchCompleted;

    /// <summary>当前游戏的全部播放集，供主页下拉框选择。</summary>
    public ObservableCollection<PlaySet> PlaySets { get; } = new();

    public bool HasGame => Game is not null;

    public bool HasNoGame => Game is null;

    /// <summary>当前游戏能否由启动器接管播放集 / mod / DLC（只有完整适配为 true）。</summary>
    public bool SupportsContentManagement => Game?.SupportsContentManagement == true;

    /// <summary>非完整适配游戏（如 Prison Architect）主页上的边界说明；完整适配或无游戏时为空串。</summary>
    public string SupportNotice
    {
        get
        {
            var game = Game;
            if (game is null || game.SupportsContentManagement)
            {
                return string.Empty;
            }

            return game.Support == GameSupport.LaunchOnly
                ? T(Keys.HomeSupportNoticeLaunchOnly, game.DisplayName)
                : T(Keys.HomeSupportNoticeLimited, game.DisplayName);
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.HomeNoGameSelected);

    public string VersionText
    {
        get
        {
            var version = Game?.Version;
            return string.IsNullOrWhiteSpace(version)
                ? T(Keys.HomeVersionUnknown)
                : T(Keys.HomeVersion, version);
        }
    }

    /// <summary>游戏版本原值（如 "1.13.11 (Matcha)"），背景右下角小字用。</summary>
    public string RawVersionText => Game?.Version ?? string.Empty;

    /// <summary>三张公告卡里有任意一张就显示整个公告区。</summary>
    public bool HasNews => !NewsMain.IsEmpty || !NewsSecondary1.IsEmpty || !NewsSecondary2.IsEmpty;

    public bool HasNewsMain => !NewsMain.IsEmpty;

    public bool HasNewsSecondary1 => !NewsSecondary1.IsEmpty;

    public bool HasNewsSecondary2 => !NewsSecondary2.IsEmpty;

    public bool HasContinue => !string.IsNullOrWhiteSpace(ContinueTitle);

    public bool HasNoContinue => !HasContinue;

    /// <summary>
    /// 「继续」按钮的提示：主页不再用独立存档条展示最近存档（官方启动器也没有），
    /// 存档名与时间改由按钮 ToolTip 承载。
    /// </summary>
    public string ContinueToolTip =>
        HasContinue
            ? string.IsNullOrWhiteSpace(ContinueDate)
                ? T(Keys.HomeContinueToolTipNoDate, ContinueTitle)
                : T(Keys.HomeContinueToolTip, ContinueTitle, ContinueDate)
            : T(Keys.HomeNoContinueSave);

    public bool HasPlaySets => PlaySets.Count > 0;

    public bool HasNoPlaySets => !HasPlaySets;

    // 统计卡已从主页移除（对齐官方布局），下面两个派生属性暂无绑定；保留是因为加载播放集时顺带算好了计数，后续页面可直接复用。
    public string PlaySetText =>
        HasGame && !SupportsContentManagement
            ? T(Keys.HomeManagedByGame)
            : string.IsNullOrWhiteSpace(PlaySetName)
                ? T(Keys.HomeNoActivePlaySet)
                : T(Keys.HomePlaySetNameValue, PlaySetName);

    public string CountsText =>
        HasGame && !SupportsContentManagement
            ? T(Keys.HomeManagedByGame)
            : T(Keys.HomeCounts, EnabledModCount, ModCount, DlcCount);

    /// <summary>加载当前游戏的全部主页数据（继续游戏 + 播放集 + 计数）。</summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        Game = game;
        IsBusy = true;
        try
        {
            if (game is null)
            {
                ClearState();
                SetStatus(Keys.HomeStatusNoGameSelected);
                IsLoaded = true;
                return;
            }

            BackgroundPath = game.Theme.Background ?? string.Empty;
            LogoPath = game.Theme.Logo ?? string.Empty;
            await LoadContinueGameAsync(game, cancellationToken).ConfigureAwait(true);
            await LoadPlaySetAsync(game, cancellationToken).ConfigureAwait(true);

            // 公告是装饰性弱依赖：先让页面渲染出来，再在后台补公告，绝不让主页等网络。
            _ = LoadNewsAsync(game, cancellationToken);

            IsLoaded = true;
            ClearStatus();
        }
        finally
        {
            IsBusy = false;
            RaiseDerived();
        }
    }

    /// <summary>启动当前游戏（普通启动，不带 --continuelastsave）。</summary>
    [RelayCommand]
    private Task LaunchAsync() => LaunchCoreAsync(LaunchRequest.Default);

    /// <summary>继续上次存档启动：追加 --continuelastsave。</summary>
    [RelayCommand]
    private Task LaunchContinueAsync() => LaunchCoreAsync(new LaunchRequest(null, null, true));

    private async Task LaunchCoreAsync(LaunchRequest request)
    {
        var game = Game;
        if (game is null)
        {
            LaunchCompleted?.Invoke(this, ProcessStartResult.Failure(T(Keys.HomeStatusNoGameSelected)));
            return;
        }

        IsBusy = true;
        try
        {
            var result = await AppServices.Launcher
                .LaunchAsync(game, request, CancellationToken.None)
                .ConfigureAwait(true);

            if (result.Started)
            {
                AppServices.Logger.Info(
                    "已启动 " + game.DisplayName + "（pid=" + (result.ProcessId?.ToString() ?? "未知") + "）");
                SetStatus(Keys.HomeStatusLaunched, game.DisplayName);
            }
            else
            {
                if (result.ErrorMessage is { } errorMessage)
                {
                    SetStatus(Keys.HomeStatusLaunchFailed, errorMessage);
                }
                else
                {
                    SetStatus(Keys.HomeStatusLaunchFailedUnknown);
                }
            }

            LaunchCompleted?.Invoke(this, result);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("启动游戏异常（gameId=" + game.GameId + "）", ex);
            SetStatus(Keys.HomeStatusLaunchFailed, ex.Message);
            LaunchCompleted?.Invoke(this, ProcessStartResult.Failure(ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>把选中的播放集设为激活项，并刷新 mod 计数。</summary>
    private async Task ApplyPlaySetSelectionAsync(GameInstallation game, PlaySet playSet)
    {
        try
        {
            await AppServices.PlaySets
                .SetActivePlaySetAsync(game, playSet.Id, CancellationToken.None)
                .ConfigureAwait(true);

            PlaySetName = playSet.Name;

            var mods = await AppServices.PlaySets
                .GetPlaySetModsAsync(game, playSet.Id, CancellationToken.None)
                .ConfigureAwait(true);
            ModCount = mods.Count;
            EnabledModCount = mods.Count(static m => m.Enabled);

            AppServices.Logger.Info("已切换激活播放集: " + playSet.Name);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("切换激活播放集失败: " + playSet.Name, ex);
            SetStatus(Keys.HomeStatusPlaySetSwitchFailed, ex.Message);
        }
    }

    private async Task LoadContinueGameAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        try
        {
            var info = await AppServices.ContinueGame
                .ReadAsync(game, cancellationToken)
                .ConfigureAwait(true);
            if (info is null)
            {
                ContinueTitle = string.Empty;
                ContinueDate = string.Empty;
                ContinueDescription = string.Empty;
                return;
            }

            ContinueTitle = info.Title ?? string.Empty;
            ContinueDate = info.Date ?? string.Empty;
            ContinueDescription = info.Description ?? string.Empty;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 继续游戏信息是可选的：读不到只记日志，主页照常显示。
            AppServices.Logger.Warn("读取 continue_game.json 失败: " + ex.Message);
            ContinueTitle = string.Empty;
            ContinueDate = string.Empty;
            ContinueDescription = string.Empty;
        }
    }

    private async Task LoadPlaySetAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        if (!game.SupportsContentManagement)
        {
            // 仅启动的游戏：播放集 / mod / DLC 全归游戏本体，连读取都不做（避免无谓 IO 与误报）。
            ClearPlaySetState();
            return;
        }

        try
        {
            var active = await AppServices.PlaySets
                .GetActivePlaySetAsync(game, cancellationToken)
                .ConfigureAwait(true);
            PlaySetName = active?.Name ?? string.Empty;

            var all = await AppServices.PlaySets
                .GetPlaySetsAsync(game, cancellationToken)
                .ConfigureAwait(true);

            _suppressPlaySetChange = true;
            try
            {
                PlaySets.Clear();
                foreach (var playSet in all)
                {
                    PlaySets.Add(playSet);
                }

                SelectedPlaySet = all.FirstOrDefault(p => p.Id == active?.Id)
                    ?? all.FirstOrDefault(static p => p.IsActive)
                    ?? all.FirstOrDefault();
            }
            finally
            {
                _suppressPlaySetChange = false;
            }

            OnPropertyChanged(nameof(HasPlaySets));
            OnPropertyChanged(nameof(HasNoPlaySets));

            if (active is null)
            {
                ModCount = 0;
                EnabledModCount = 0;
            }
            else
            {
                var mods = await AppServices.PlaySets
                    .GetPlaySetModsAsync(game, active.Id, cancellationToken)
                    .ConfigureAwait(true);
                ModCount = mods.Count;
                EnabledModCount = mods.Count(static m => m.Enabled);
            }

            var dlcs = await AppServices.Dlcs
                .DiscoverAsync(game, cancellationToken)
                .ConfigureAwait(true);
            DlcCount = dlcs.Count;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // 计数属于装饰信息，失败不影响启动主流程。
            AppServices.Logger.Warn("读取播放集/DLC 统计失败: " + ex.Message);
            PlaySetName = string.Empty;
            ModCount = 0;
            EnabledModCount = 0;
            DlcCount = 0;
        }
    }

    /// <summary>
    /// 读取当前游戏的官方公告。公告是弱依赖：先渲染页面，再补公告，失败不影响主页
    /// （与 DlcViewModel.FillDescriptionsAsync 同一套取舍，失败只 Warn，不弹 InfoBar）。
    /// </summary>
    private async Task LoadNewsAsync(GameInstallation game, CancellationToken cancellationToken)
    {
        // 换游戏时先把上一款游戏的公告清掉，避免旧卡在新公告到来前继续显示。
        NewsMain = NewsCardItem.Empty(NewsFeedSlots.Main);
        NewsSecondary1 = NewsCardItem.Empty(NewsFeedSlots.Secondary1);
        NewsSecondary2 = NewsCardItem.Empty(NewsFeedSlots.Secondary2);

        try
        {
            var feed = await AppServices.NewsFeed
                .GetAsync(game, AppServices.Settings.NewsFeedOnlineEnabled, cancellationToken)
                .ConfigureAwait(true);

            NewsMain = NewsCardItem.FromFeedItem(feed.Find(NewsFeedSlots.Main))
                ?? NewsCardItem.Empty(NewsFeedSlots.Main);
            NewsSecondary1 = NewsCardItem.FromFeedItem(feed.Find(NewsFeedSlots.Secondary1))
                ?? NewsCardItem.Empty(NewsFeedSlots.Secondary1);
            NewsSecondary2 = NewsCardItem.FromFeedItem(feed.Find(NewsFeedSlots.Secondary2))
                ?? NewsCardItem.Empty(NewsFeedSlots.Secondary2);

            if (feed.IsEmpty)
            {
                AppServices.Logger.Info("主页没有官方公告（gameId=" + game.GameId + "）");
            }
            else
            {
                // 公告卡几乎没有文字，图没下回来就等于空卡（HasNews 为假），会把整块广告栏隐藏掉。
                var hidden = 0;
                foreach (var item in feed.Items)
                {
                    if (item.ImageUrl is not null && item.ImagePath is null)
                    {
                        hidden++;
                    }
                }

                if (hidden > 0)
                {
                    AppServices.Logger.Info("官方公告：有 " + hidden.ToString(CultureInfo.InvariantCulture)
                        + " 张卡因图片未下载成功而隐藏（gameId=" + game.GameId + "）");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 页面切走导致的取消不算错误，也不再改动界面。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("主页公告获取失败（只影响公告区）: " + ex.Message);
        }
    }

    private void ClearState()
    {
        BackgroundPath = string.Empty;
        LogoPath = string.Empty;
        NewsMain = NewsCardItem.Empty(NewsFeedSlots.Main);
        NewsSecondary1 = NewsCardItem.Empty(NewsFeedSlots.Secondary1);
        NewsSecondary2 = NewsCardItem.Empty(NewsFeedSlots.Secondary2);
        PlaySetName = string.Empty;
        ModCount = 0;
        EnabledModCount = 0;
        DlcCount = 0;
        ContinueTitle = string.Empty;
        ContinueDate = string.Empty;
        ContinueDescription = string.Empty;

        ClearPlaySetState();
    }

    /// <summary>清空播放集相关状态（含派生文案），切到仅启动游戏时也走这里。</summary>
    private void ClearPlaySetState()
    {
        PlaySetName = string.Empty;
        ModCount = 0;
        EnabledModCount = 0;
        DlcCount = 0;

        _suppressPlaySetChange = true;
        try
        {
            PlaySets.Clear();
            SelectedPlaySet = null;
        }
        finally
        {
            _suppressPlaySetChange = false;
        }

        OnPropertyChanged(nameof(HasPlaySets));
        OnPropertyChanged(nameof(HasNoPlaySets));
    }

    /// <summary>设置状态栏文案并记下键与参数，供切语言时重算。页面在服务未就绪等场景也用它写状态。</summary>
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = T(key, args);
    }

    /// <summary>清空状态栏，同时丢掉上一段文案的键。</summary>
    private void ClearStatus()
    {
        _statusKey = null;
        _statusArgs = [];
        StatusMessage = string.Empty;
    }

    /// <summary>语言切换后把状态栏文案按当前语言重算（其余派生属性由基类统一发通知即可）。</summary>
    protected override void Relocalize()
    {
        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(GameTitle));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(RawVersionText));
        OnPropertyChanged(nameof(HasContinue));
        OnPropertyChanged(nameof(HasNoContinue));
        OnPropertyChanged(nameof(ContinueToolTip));
        OnPropertyChanged(nameof(SupportsContentManagement));
        OnPropertyChanged(nameof(SupportNotice));
        OnPropertyChanged(nameof(PlaySetText));
        OnPropertyChanged(nameof(CountsText));
    }

    partial void OnGameChanged(GameInstallation? value)
    {
        OnPropertyChanged(nameof(HasGame));
        OnPropertyChanged(nameof(HasNoGame));
        OnPropertyChanged(nameof(GameTitle));
        OnPropertyChanged(nameof(VersionText));
        OnPropertyChanged(nameof(RawVersionText));
        OnPropertyChanged(nameof(ContinueToolTip));
        OnPropertyChanged(nameof(SupportsContentManagement));
        OnPropertyChanged(nameof(SupportNotice));
        OnPropertyChanged(nameof(PlaySetText));
        OnPropertyChanged(nameof(CountsText));
    }

    partial void OnNewsMainChanged(NewsCardItem value)
    {
        OnPropertyChanged(nameof(HasNews));
        OnPropertyChanged(nameof(HasNewsMain));
    }

    partial void OnNewsSecondary1Changed(NewsCardItem value)
    {
        OnPropertyChanged(nameof(HasNews));
        OnPropertyChanged(nameof(HasNewsSecondary1));
    }

    partial void OnNewsSecondary2Changed(NewsCardItem value)
    {
        OnPropertyChanged(nameof(HasNews));
        OnPropertyChanged(nameof(HasNewsSecondary2));
    }

    partial void OnContinueTitleChanged(string value)
    {
        OnPropertyChanged(nameof(HasContinue));
        OnPropertyChanged(nameof(HasNoContinue));
        OnPropertyChanged(nameof(ContinueToolTip));
    }

    partial void OnContinueDateChanged(string value) => OnPropertyChanged(nameof(ContinueToolTip));

    partial void OnPlaySetNameChanged(string value) => OnPropertyChanged(nameof(PlaySetText));

    partial void OnModCountChanged(int value) => OnPropertyChanged(nameof(CountsText));

    partial void OnEnabledModCountChanged(int value) => OnPropertyChanged(nameof(CountsText));

    partial void OnDlcCountChanged(int value) => OnPropertyChanged(nameof(CountsText));

    partial void OnSelectedPlaySetChanged(PlaySet? value)
    {
        if (_suppressPlaySetChange || value is null)
        {
            return;
        }

        var game = Game;
        if (game is null)
        {
            return;
        }

        _ = ApplyPlaySetSelectionAsync(game, value);
    }
}
