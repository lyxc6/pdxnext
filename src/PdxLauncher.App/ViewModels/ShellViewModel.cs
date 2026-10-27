namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 外壳视图模型：导航状态、已发现游戏列表、当前游戏标题。
/// 只持有纯数据（GameInstallation / string / bool），不出现任何 Microsoft.UI.Xaml 类型；
/// 真正执行导航的是 MainWindow（订阅 <see cref="NavigationRequested"/>）。
/// </summary>
public sealed partial class ShellViewModel : LocalizedObservableObject
{
    private readonly NavigationService _navigation;

    [ObservableProperty]
    private string _selectedTag = NavigationService.HomeTag;

    [ObservableProperty]
    private GameInstallation? _currentGame;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isDiscovering;

    /// <summary>上次扫描到的游戏数；null 表示还没扫描过（此时没有状态栏文案可重建）。</summary>
    private int? _discoveredCount;

    public ShellViewModel(NavigationService navigation)
    {
        ArgumentNullException.ThrowIfNull(navigation);
        _navigation = navigation;
    }

    /// <summary>请求外壳导航到某个 Tag（参数为导航 Tag）。</summary>
    public event EventHandler<string>? NavigationRequested;

    /// <summary>已发现的游戏（与 <see cref="AppServices.Games"/> 保持同步）。</summary>
    public ObservableCollection<GameInstallation> Games { get; } = new();

    /// <summary>标题栏主文案：启动器名称 · 当前游戏名，未选中游戏时只显示启动器名称。</summary>
    public string HeaderText => CurrentGame is null
        ? T(Keys.AppName)
        : T(Keys.AppName) + " · " + CurrentGame.DisplayName + " ·";

    /// <summary>标题栏副标题：只含版本号（次要色）。未选中游戏或版本号为空时返回空串，TitleBar 会自动隐藏该段。</summary>
    public string SubtitleText =>
        CurrentGame is null || string.IsNullOrWhiteSpace(CurrentGame.Version)
            ? string.Empty
            : T(Keys.ShellVersion, CurrentGame.Version);

    public bool HasCurrentGame => CurrentGame is not null;

    public bool HasNoGames => Games.Count == 0;

    /// <summary>是否停靠在游戏库页（决定"当前游戏"导航项是否可用）。</summary>
    public bool CanOpenCurrentGame => CurrentGame is not null;

    /// <summary>加载外壳数据：回填游戏列表并同步当前游戏。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        IsDiscovering = true;
        try
        {
            var games = await GameDiscovery.EnsureAsync(cancellationToken).ConfigureAwait(true);
            ReplaceGames(games);
            CurrentGame = AppServices.CurrentGame;
            _discoveredCount = games.Count;
            StatusMessage = BuildStatusMessage(games.Count);
        }
        finally
        {
            IsDiscovering = false;
        }
    }

    /// <summary>语言切换后重建"缓存到字段里"的状态栏文案。</summary>
    protected override void Relocalize()
    {
        if (_discoveredCount is int count)
        {
            StatusMessage = BuildStatusMessage(count);
        }
    }

    private static string BuildStatusMessage(int count) => count == 0
        ? T(Keys.ShellStatusNoGames)
        : T(Keys.ShellStatusGamesFound, count);

    /// <summary>切换当前游戏（写回 AppServices，触发全局 CurrentGameChanged）。</summary>
    public void SelectGame(GameInstallation? game)
    {
        AppServices.SelectGame(game);
        CurrentGame = AppServices.CurrentGame;
    }

    /// <summary>请求导航（由 NavigationView 的选中项调用）。</summary>
    public void RequestNavigation(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return;
        }

        SelectedTag = tag;
        NavigationRequested?.Invoke(this, tag);
    }

    /// <summary>把游戏列表同步到集合里（只 Clear + Add，不整体替换集合实例）。</summary>
    public void ReplaceGames(IReadOnlyList<GameInstallation> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        Games.Clear();
        foreach (var game in games)
        {
            Games.Add(game);
        }

        OnPropertyChanged(nameof(HasNoGames));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(CanOpenCurrentGame));
    }

    partial void OnCurrentGameChanged(GameInstallation? value)
    {
        OnPropertyChanged(nameof(HeaderText));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(HasCurrentGame));
        OnPropertyChanged(nameof(CanOpenCurrentGame));
    }

    [RelayCommand]
    private async Task DiscoverGamesAsync()
    {
        await InitializeAsync(CancellationToken.None).ConfigureAwait(true);
    }
}
