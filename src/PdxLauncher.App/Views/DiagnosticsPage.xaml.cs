namespace PdxLauncher.App.Views;

using System.ComponentModel;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using TextViewControl;
using Windows.UI;

/// <summary>
/// 「性能与日志」页：概览（内存 / GC / 句柄 + 优化建议）、缓存占用、日志尾部、限流与代理、启动耗时。
/// 全部采样都是只读的本地状态：不联网、不写游戏目录；单个分区失败只记日志，不影响其它分区。
/// </summary>
public sealed partial class DiagnosticsPage : Page
{
    private readonly DiagnosticsViewModel _viewModel = new();

    /// <summary>页面生命周期令牌；页面不缓存（NavigationCacheMode=Disabled），每次进入都会重建。</summary>
    private CancellationTokenSource _lifetime = new();

    /// <summary>日志自动刷新定时器（只有开关打开且页面可见时才跑）。</summary>
    private DispatcherTimer? _autoRefreshTimer;

    private bool _autoRefreshBusy;

    /// <summary>概览实时刷新定时器（每秒采内存，每 5 拍采缓存/日志）。</summary>
    private DispatcherTimer? _liveTimer;

    private bool _liveBusy;

    private int _liveTick;

    /// <summary>恢复分节时抑制 SelectionChanged 带来的第二次采样。</summary>
    private bool _restoringSection;

    public DiagnosticsPage()
    {
        InitializeComponent();

        ConfigureLogView();
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    /// <summary>
    /// 配置 TextViewControl：整条按级别着色（正则整行匹配，异常续行由解析层继承块级别），
    /// 关闭行号与覆盖式滚动条；颜色明暗两套，与项目语义色（WARN 黄 / ERROR 红）一致。
    /// </summary>
    private void ConfigureLogView()
    {
        LogView.HighlightLanguage = new SyntaxHighlightLanguage
        {
            Name = "PdxNextLog",
            Highlights =
            {
                new HighlightRule(@"^.*\[ERROR\].*$")
                {
                    ColorLight = Color.FromArgb(255, 179, 38, 38),
                    ColorDark = Color.FromArgb(255, 255, 105, 105),
                    Bold = true,
                },
                new HighlightRule(@"^.*\[WARN\].*$")
                {
                    ColorLight = Color.FromArgb(255, 138, 90, 0),
                    ColorDark = Color.FromArgb(255, 231, 185, 107),
                    Bold = true,
                },
                new HighlightRule(@"^.*\[INFO\].*$")
                {
                    ColorLight = Color.FromArgb(255, 95, 95, 102),
                    ColorDark = Color.FromArgb(255, 154, 154, 162),
                },
            }
        };
        LogView.ShowLineNumbers = false;
        LogView.OverlayScrollbars = true;
        LogView.TextWrapEnabled = _viewModel.LogWrap;
        LogView.MaxLines = DiagnosticsViewModel.LogTailLines;
    }

    /// <summary>VM 更新日志行/换行开关后，把内容灌进 TextControl；切语言时重建本地化高亮标题。</summary>
    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            switch (e.PropertyName)
            {
                case nameof(DiagnosticsViewModel.LogDisplayLines):
                case nameof(DiagnosticsViewModel.LogFilter):
                    PushLogLines();
                    break;
                case nameof(DiagnosticsViewModel.LogWrap):
                    LogView.TextWrapEnabled = _viewModel.LogWrap;
                    break;
                case nameof(DiagnosticsViewModel.AutoRefresh):
                    SyncAutoRefreshTimer();
                    break;
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 同步日志视图失败", ex);
        }
    }

    /// <summary>把 VM 的显示行灌入 TextControl；正序渲染时让最新一行可见。</summary>
    private void PushLogLines()
    {
        var lines = _viewModel.LogDisplayLines;
        LogView.SetLines(lines.Count == 0
            ? new List<string>()
            : lines.Select(line => line.Text).ToList());
        LogTailEmptyText.Visibility = lines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    public DiagnosticsViewModel ViewModel => _viewModel;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    // 左侧五个分区的序号，与 XAML 里 secOverview…secStartup 的顺序一致。
    private const int OverviewSectionIndex = 0;
    private const int CacheSectionIndex = 1;
    private const int LogSectionIndex = 2;
    private const int NetworkSectionIndex = 3;
    private const int StartupSectionIndex = 4;

    // ------------------------------------------------------------ 生命周期

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            // 首次进入：五个分区各采一次（缓存统计是文件属性，日志只读尾部，都不联网）。
            await _viewModel.LoadAsync(_lifetime.Token).ConfigureAwait(true);

            RestoreSection();
            SyncLiveTimer();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        StopAutoRefresh();
        StopLiveTimer();
        _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
        PersistSection();
        _lifetime.Cancel();
    }

    // ------------------------------------------------------------ 分节导航

    /// <summary>切换左侧分区：只显示选中那一节，并顺手刷新该分区（开销都是本地读取）。</summary>
    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        var index = CurrentSectionIndex();
        ShowSection(index);
        if (!_restoringSection)
        {
            RefreshSection(index);
        }
    }

    private int CurrentSectionIndex()
    {
        if (navDiagnosticsSections.SelectedItem is NavigationViewItem item
            && int.TryParse(item.Tag?.ToString(), out var index))
        {
            return index;
        }

        return OverviewSectionIndex;
    }

    private void ShowSection(int index)
    {
        // 日志分区不放滚动容器：正文要跟着窗口高度伸缩，所以和滚动区互斥显示。
        var showLog = index == LogSectionIndex;
        secLog.Visibility = showLog ? Visibility.Visible : Visibility.Collapsed;
        diagSectionScroller.Visibility = showLog ? Visibility.Collapsed : Visibility.Visible;

        var sections = new (int Index, FrameworkElement? Element)[]
        {
            (OverviewSectionIndex, secOverview),
            (CacheSectionIndex, secCache),
            (NetworkSectionIndex, secNetwork),
            (StartupSectionIndex, secStartup),
        };

        foreach (var (sectionIndex, element) in sections)
        {
            if (element is { } section)
            {
                section.Visibility = sectionIndex == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        SyncAutoRefreshTimer();
        PersistSection(index);
        SyncLiveTimer();
    }

    /// <summary>记住停在哪一节：下次进页面（含冷启动）直接回到这里。</summary>
    private void PersistSection(int? index = null)
    {
        try
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            var target = index ?? CurrentSectionIndex();
            if (AppServices.Settings.LastDiagnosticsSection == target)
            {
                return;
            }

            AppServices.Settings.LastDiagnosticsSection = target;
            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存性能与日志页分节失败: " + saved.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("保存性能与日志页分节失败: " + ex.Message);
        }
    }

    /// <summary>恢复上次停留的分区：越界或从没记过时落到概览，并把左栏选中项同步过去。</summary>
    private void RestoreSection()
    {
        var items = navDiagnosticsSections.MenuItems.OfType<NavigationViewItem>().ToList();
        var index = AppServices.IsInitialized ? AppServices.Settings.LastDiagnosticsSection ?? OverviewSectionIndex : OverviewSectionIndex;
        if (index < 0 || index >= items.Count)
        {
            index = OverviewSectionIndex;
        }

        _restoringSection = true;
        try
        {
            if (index < items.Count)
            {
                navDiagnosticsSections.SelectedItem = items[index];
            }
        }
        finally
        {
            _restoringSection = false;
        }

        ShowSection(index);
    }

    /// <summary>切到某个分区时刷新一次；LoadAsync 刚跑完，所以恢复分节时不再调用。</summary>
    private async void RefreshSection(int index)
    {
        try
        {
            switch (index)
            {
                case CacheSectionIndex:
                    await RunGuardedAsync(
                        "DiagnosticsPage 统计缓存占用失败",
                        () => _viewModel.RefreshCacheStatsAsync(_lifetime.Token)).ConfigureAwait(true);
                    break;
                case LogSectionIndex:
                    await RunGuardedAsync(
                        "DiagnosticsPage 读取日志失败",
                        () => _viewModel.RefreshLogAsync(_lifetime.Token)).ConfigureAwait(true);
                    break;
                case NetworkSectionIndex:
                    await RunGuardedAsync(
                        "DiagnosticsPage 读取网络状态失败",
                        () => _viewModel.RefreshNetworkAsync(_lifetime.Token)).ConfigureAwait(true);
                    break;
                case StartupSectionIndex:
                    _viewModel.RefreshStartup();
                    break;
                default:
                    await RunGuardedAsync(
                        "DiagnosticsPage 重新采样失败",
                        () => _viewModel.SampleAsync(_lifetime.Token)).ConfigureAwait(true);
                    break;
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 切换分区刷新失败", ex);
        }
    }

    // ------------------------------------------------------------ 概览 / 缓存

    private async void OnRefreshOverviewClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "DiagnosticsPage 重新采样失败",
            () => _viewModel.SampleAsync(_lifetime.Token));

    private async void OnRefreshCacheClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "DiagnosticsPage 统计缓存占用失败",
            () => _viewModel.RefreshCacheStatsAsync(_lifetime.Token));

    private async void OnClearCacheRowClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "DiagnosticsPage 清除缓存失败",
            () => sender is FrameworkElement { DataContext: CacheRowViewModel row }
                ? _viewModel.ClearCacheAsync(row, _lifetime.Token)
                : Task.CompletedTask);

    private async void OnClearAllCachesClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "DiagnosticsPage 清除全部缓存失败",
            async () =>
            {
                if (!await ConfirmAsync(
                        T(Keys.SettingsCacheDialogClearAllTitle),
                        T(Keys.SettingsCacheDialogClearAllContent),
                        T(Keys.CommonClear),
                        InitialFocus.Cancel).ConfigureAwait(true))
                {
                    return;
                }

                await _viewModel.ClearAllCachesAsync(_lifetime.Token).ConfigureAwait(true);
            });

    // ------------------------------------------------------------ 概览实时刷新

    private void OnLiveToggled(object sender, RoutedEventArgs e) => SyncLiveTimer();

    /// <summary>概览分节可见且开关打开时启动实时刷新；离开分节或暂停时停掉。</summary>
    private void SyncLiveTimer()
    {
        var shouldRun = _viewModel.LiveEnabled
                        && CurrentSectionIndex() == OverviewSectionIndex
                        && !_lifetime.IsCancellationRequested
                        && IsLoaded;

        if (shouldRun)
        {
            if (_liveTimer is null)
            {
                _liveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _liveTimer.Tick += OnLiveTick;
            }

            _liveTick = 0;
            _liveTimer.Start();
        }
        else
        {
            StopLiveTimer();
        }
    }

    private void StopLiveTimer()
    {
        _liveTimer?.Stop();
    }

    /// <summary>
    /// 每秒采一次内存快照（纯进程计数）；每 5 拍顺带统计缓存与日志（文件属性读取），
    /// 降低磁盘 IO。上一次还没跑完就跳过这一拍，避免叠加。
    /// </summary>
    private async void OnLiveTick(object? sender, object e)
    {
        if (_liveBusy || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _liveBusy = true;
        try
        {
            _liveTick++;
            await _viewModel.SampleAsync(_lifetime.Token).ConfigureAwait(true);
            if (_liveTick % 5 == 0)
            {
                await _viewModel.RefreshCacheStatsAsync(_lifetime.Token).ConfigureAwait(true);
                await _viewModel.RefreshLogAsync(_lifetime.Token).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 概览实时刷新失败", ex);
        }
        finally
        {
            _liveBusy = false;
        }
    }

    // ------------------------------------------------------------ 日志

    private async void OnRefreshLogClick(object sender, RoutedEventArgs e) =>
        await RefreshLogOnceAsync();

    /// <summary>打开日志目录（只读查看，不删不改）。</summary>
    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var directory = AppServices.FileSystem.GetDirectoryName(AppServices.LogPath);
            if (string.IsNullOrWhiteSpace(directory)
                || !ShellLauncher.TryStart("explorer.exe", "\"" + directory + "\"", "DiagnosticsPage"))
            {
                _viewModel.SetStatus(Keys.DiagnosticsLogOpenFailed, directory ?? string.Empty);
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 打开日志目录失败", ex);
            _viewModel.SetStatus(Keys.DiagnosticsLogOpenFailed, ex.Message);
        }
    }

    /// <summary>
    /// 自动刷新定时器只在「开关打开 + 当前停在日志分区 + 页面可见」三个条件都满足时运行。
    /// 由开关变更与分区切换共同驱动，对齐自动换行「状态跟着设置走」的行为。
    /// </summary>
    private void SyncAutoRefreshTimer()
    {
        if (_viewModel.AutoRefresh && CurrentSectionIndex() == LogSectionIndex && !_lifetime.IsCancellationRequested)
        {
            StartAutoRefresh();
        }
        else
        {
            StopAutoRefresh();
        }
    }

    private void StartAutoRefresh()
    {
        if (_autoRefreshTimer is not null)
        {
            if (_autoRefreshTimer.IsEnabled)
            {
                return;
            }

            _autoRefreshTimer.Start();
            return;
        }

        _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoRefreshTimer.Tick += OnAutoRefreshTick;
        _autoRefreshTimer.Start();
    }

    private void StopAutoRefresh() =>
        _autoRefreshTimer?.Stop();

    /// <summary>5 秒一次只重读日志；上一次还没读完就跳过这一拍，避免叠加。</summary>
    private async void OnAutoRefreshTick(object? sender, object e) =>
        await RefreshLogOnceAsync();

    /// <summary>手动刷新与自动刷新共用：读完后 VM 的属性通知会把新行灌进 TextControl。</summary>
    private async Task RefreshLogOnceAsync()
    {
        if (_autoRefreshBusy || _lifetime.IsCancellationRequested)
        {
            return;
        }

        _autoRefreshBusy = true;
        try
        {
            await _viewModel.RefreshLogAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 自动刷新日志失败", ex);
        }
        finally
        {
            _autoRefreshBusy = false;
        }
    }

    // ------------------------------------------------------------ 对话框 / 护栏

    /// <summary>
    /// 统一的两键确认框：左边取消、右边确认。默认焦点落在确认上；
    /// 清空缓存这类破坏性操作传 <see cref="InitialFocus.Cancel"/>。
    /// </summary>
    private async Task<bool> ConfirmAsync(
        string title,
        string content,
        string primaryText,
        InitialFocus initialFocus = InitialFocus.Confirm) =>
        await ConfirmOverlay.ConfirmAsync(
            title,
            content,
            primaryText,
            T(Keys.CommonCancel),
            initialFocus: initialFocus);

    /// <summary>事件处理器的统一护栏：页面关闭导致的取消静默忽略，其它异常只记日志。</summary>
    private async Task RunGuardedAsync(string logMessage, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error(logMessage, ex);
        }
    }

    private static string T(string key, params object?[] args) => Localizer.Instance.T(key, args);
}
