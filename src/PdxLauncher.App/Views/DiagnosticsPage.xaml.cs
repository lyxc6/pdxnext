namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;

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

    /// <summary>恢复分节时抑制 SelectionChanged 带来的第二次采样。</summary>
    private bool _restoringSection;

    public DiagnosticsPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
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

        PersistSection(index);
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

    // ------------------------------------------------------------ 日志

    private async void OnRefreshLogClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "DiagnosticsPage 读取日志失败",
            () => _viewModel.RefreshLogAsync(_lifetime.Token));

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

    private void OnLogFilterChecked(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { Tag: string tag })
            {
                return;
            }

            _viewModel.LogFilter = tag switch
            {
                "Warning" => LogLevelFilter.Warning,
                "Error" => LogLevelFilter.Error,
                _ => LogLevelFilter.All,
            };
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 切换日志级别失败", ex);
        }
    }

    private void OnAutoRefreshToggled(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is ToggleSwitch { IsOn: true })
            {
                StartAutoRefresh();
            }
            else
            {
                StopAutoRefresh();
            }
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("DiagnosticsPage 切换日志自动刷新失败", ex);
        }
    }

    private void StartAutoRefresh()
    {
        if (_autoRefreshTimer is null)
        {
            _autoRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _autoRefreshTimer.Tick += OnAutoRefreshTick;
        }

        _autoRefreshTimer.Start();
    }

    private void StopAutoRefresh()
    {
        _autoRefreshTimer?.Stop();
    }

    /// <summary>5 秒一次只重读日志；上一次还没读完就跳过这一拍，避免叠加。</summary>
    private async void OnAutoRefreshTick(object? sender, object e)
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
