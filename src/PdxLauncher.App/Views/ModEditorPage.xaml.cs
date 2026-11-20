namespace PdxLauncher.App.Views;

using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「Mod 编辑」页：分节切换、左栏显隐联动、游戏信息同步，以及四个分区的 shell 动作
/// （文件/文件夹选择、打开目录、导出轮询定时器）。四个功能的业务逻辑都在
/// <see cref="ModEditorViewModel"/> 与其复用的服务类里。全程只用本地数据，不构造 URL、不发网络请求。
/// </summary>
public sealed partial class ModEditorPage : Page
{
    private readonly ModEditorViewModel _viewModel;

    /// <summary>左栏展开宽度（DIP），收起时写 0。</summary>
    private const double LeftPaneWidth = 216;

    /// <summary>四个分区顺序，与 XAML 左栏 Tag / 右栏 StackPanel 一致。</summary>
    private static readonly string[] SectionAutomationIds =
    [
        "secModEditorExport",
        "secModEditorSearch",
        "secModEditorTemplate",
        "secModEditorLibrary",
    ];

    /// <summary>导出目录轮询定时器（1 秒，导出开始才跑，页面卸载即停）。</summary>
    private readonly DispatcherTimer _watchTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private bool _restoringSection;

    /// <summary>x:Bind 在 InitializeComponent 期间就会改 SelectedIndex；那时先不切分区。</summary>
    private bool _isLoaded;

    public ModEditorPage()
    {
        InitializeComponent();

        _viewModel = new ModEditorViewModel();
        _watchTimer.Tick += OnWatchTick;
        _viewModel.WatchStarted += OnWatchStarted;
        _viewModel.WatchStopped += OnWatchStopped;
        _viewModel.PickWatchDirectoryRequested += OnPickWatchDirectoryRequested;
        _viewModel.PickPreviewImageRequested += OnPickPreviewImageRequested;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public ModEditorViewModel ViewModel => _viewModel;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    // ------------------------------------------------------------ 生命周期

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = true;

        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        AppServices.CurrentGameChanged += OnCurrentGameChanged;
        AppServices.ModEditorPaneVisibilityChanged -= OnModEditorPaneVisibilityChanged;
        AppServices.ModEditorPaneVisibilityChanged += OnModEditorPaneVisibilityChanged;

        _viewModel.SetGame(AppServices.IsInitialized ? AppServices.CurrentGame : null);
        ApplyLeftPaneVisibility();
        RestoreSection();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _isLoaded = false;

        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        AppServices.ModEditorPaneVisibilityChanged -= OnModEditorPaneVisibilityChanged;

        StopWatchTimer();
        _viewModel.StopWatchCommand.Execute(null);
    }

    /// <summary>当前游戏变化：同步页头、模板可用性，并清掉上一款游戏的导出监视。</summary>
    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() =>
        {
            _viewModel.StopWatchCommand.Execute(null);
            _viewModel.SetGame(game);
        }))
        {
            AppServices.Logger.Warn("ModEditorPage 无法派发游戏切换事件");
        }
    }

    /// <summary>设置页改了左栏显隐：把绑定值拉回最新并重算列宽。</summary>
    private void OnModEditorPaneVisibilityChanged(object? sender, EventArgs e)
    {
        if (!DispatcherQueue.TryEnqueue(ApplyFromSettings))
        {
            AppServices.Logger.Warn("ModEditorPage 无法派发左栏显隐变化");
        }
    }

    private void ApplyFromSettings()
    {
        _viewModel.RefreshLeftPaneVisibility();
        ApplyLeftPaneVisibility();
    }

    /// <summary>左栏显隐落到 Column0 宽度：显示 216、隐藏 0。</summary>
    private void ApplyLeftPaneVisibility()
    {
        leftPaneColumn.Width = new GridLength(_viewModel.IsLeftPaneVisible ? LeftPaneWidth : 0);
    }

    // ------------------------------------------------------------ 导出轮询 / 选择器

    private void OnWatchStarted() => _watchTimer.Start();

    private void OnWatchStopped() => StopWatchTimer();

    private void StopWatchTimer()
    {
        if (_watchTimer.IsEnabled)
        {
            _watchTimer.Stop();
        }
    }

    private void OnWatchTick(object? sender, object e) => _viewModel.Tick();

    private async void OnPickWatchDirectoryRequested()
    {
        var path = await FilePickers.PickFolderAsync();
        _viewModel.SetWatchDirectory(path);
    }

    private async void OnPickPreviewImageRequested()
    {
        var path = await FilePickers.PickOpenImageAsync();
        _viewModel.SetPreviewImage(path);
    }

    private void OnOpenExportFolderClick(object sender, RoutedEventArgs e)
    {
        var directory = _viewModel.ExportDestination;
        if (string.IsNullOrWhiteSpace(directory) || !ShellLauncher.TryStart("explorer.exe", "\"" + directory + "\"", "ModEditorPage"))
        {
            _viewModel.SetStatusFromPage(Keys.ModEditorExportWatchDirInvalid);
        }
    }

    private void OnOpenLibraryFolderClick(object sender, RoutedEventArgs e)
    {
        var directory = _viewModel.LibraryFolder;
        if (string.IsNullOrWhiteSpace(directory) || !ShellLauncher.TryStart("explorer.exe", "\"" + directory + "\"", "ModEditorPage"))
        {
            _viewModel.SetStatusFromPage(Keys.ModEditorLibraryNoGame);
        }
    }

    private void OnOpenSearchFileClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ModEditorSearchRow row }
            && !string.IsNullOrWhiteSpace(row.Path)
            && !ShellLauncher.TryStart("explorer.exe", "/select,\"" + row.Path + "\"", "ModEditorPage"))
        {
            _viewModel.SetStatusFromPage(Keys.ModEditorSearchNoDocs);
        }
    }

    // ------------------------------------------------------------ 分节切换

    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        if (_restoringSection)
        {
            return;
        }

        var index = CurrentSectionIndex();
        ShowSection(index);
    }

    private int CurrentSectionIndex()
    {
        if (navModEditorSections.SelectedItem is NavigationViewItem item
            && int.TryParse(item.Tag?.ToString(), out var index)
            && index >= 0
            && index < SectionAutomationIds.Length)
        {
            return index;
        }

        return 0;
    }

    /// <summary>只显示选中的右栏分区；同步给隐藏左栏时用的 ComboBox 与左栏高亮。</summary>
    private void ShowSection(int index)
    {
        foreach (var element in RightPaneSections())
        {
            if (element is null)
            {
                continue;
            }

            var sectionIndex = Array.IndexOf(SectionAutomationIds, element.Name);
            element.Visibility = sectionIndex == index ? Visibility.Visible : Visibility.Collapsed;
        }

        if (sectionPicker.SelectedIndex != index)
        {
            _restoringSection = true;
            sectionPicker.SelectedIndex = index;
            _restoringSection = false;
        }

        // 左栏高亮也要跟上：隐藏左栏时用 ComboBox 切过分区后，再次显示左栏时
        // SelectedItem 可能停在旧项（或无高亮）。不在这里同步，点旧高亮项时
        // SelectedItem 不变、SelectionChanged 不触发，用户点它无法切回对应分区。
        if (navModEditorSections.SelectedItem is not NavigationViewItem selected
            || !int.TryParse(selected.Tag?.ToString(), out var selectedIndex)
            || selectedIndex != index)
        {
            var target = SectionItem(index);
            if (target is not null)
            {
                _restoringSection = true;
                navModEditorSections.SelectedItem = target;
                _restoringSection = false;
            }
        }

        _viewModel.SelectedSectionIndex = index;
    }

    /// <summary>按分区序号取左栏对应的 NavigationViewItem（Tag 与序号一致）。</summary>
    private NavigationViewItem? SectionItem(int index)
    {
        foreach (var item in navModEditorSections.MenuItems)
        {
            if (item is NavigationViewItem navItem
                && int.TryParse(navItem.Tag?.ToString(), out var tag)
                && tag == index)
            {
                return navItem;
            }
        }

        return null;
    }

    /// <summary>隐藏左栏时 ComboBox 改选也要切分区。</summary>
    private void OnPickerSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isLoaded || _restoringSection || sectionPicker.SelectedIndex < 0)
        {
            return;
        }

        ShowSection(sectionPicker.SelectedIndex);
    }

    private FrameworkElement?[] RightPaneSections() =>
    [
        secModEditorExport,
        secModEditorSearch,
        secModEditorTemplate,
        secModEditorLibrary,
    ];

    private void RestoreSection()
    {
        _restoringSection = true;
        ShowSection(0);
        _restoringSection = false;
    }
}
