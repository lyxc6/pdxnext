namespace PdxLauncher.App.Views;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「崩溃分析」页：诊断结论、崩溃记录、日志证据、启动链路。
/// 只读页面：只读取游戏崩溃包/日志，打开目录与复制报告；不写游戏目录、不删文件、不联网。
/// </summary>
public sealed partial class CrashAnalysisPage : Page
{
    private readonly CrashAnalysisViewModel _viewModel = new();

    /// <summary>页面生命周期令牌；页面不缓存（NavigationCacheMode=Disabled），每次进入都会重建。</summary>
    private CancellationTokenSource _lifetime = new();

    public CrashAnalysisPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public CrashAnalysisViewModel ViewModel => _viewModel;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
    public Localizer L => Localizer.Instance;

    // 左侧四个分区的序号，与 XAML 里 secOverview / secCrashes / secLogs / secLaunch 的顺序一致。
    private const int OverviewSectionIndex = 0;
    private const int CrashesSectionIndex = 1;
    private const int LogsSectionIndex = 2;
    private const int LaunchSectionIndex = 3;

    // ------------------------------------------------------------ 生命周期

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_lifetime.IsCancellationRequested)
            {
                _lifetime = new CancellationTokenSource();
            }

            AppServices.CurrentGameChanged += OnCurrentGameChanged;
            await _viewModel.LoadAsync(_lifetime.Token).ConfigureAwait(true);
            RestoreSection();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("CrashAnalysisPage 初始化失败", exception);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        PersistSection();
        _lifetime.Cancel();
    }

    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(async () =>
            {
                await RunGuardedAsync(
                    "CrashAnalysisPage 切换游戏后重新分析失败",
                    () => _viewModel.LoadAsync(_lifetime.Token)).ConfigureAwait(true);
            }))
        {
            AppServices.Logger.Warn("CrashAnalysisPage 切换游戏后无法回到 UI 线程重新分析");
        }
    }

    // ------------------------------------------------------------ 分节导航

    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        var index = CurrentSectionIndex();
        ShowSection(index);
        PersistSection(index);
    }

    private int CurrentSectionIndex()
    {
        if (navCrashSections.SelectedItem is NavigationViewItem item
            && int.TryParse(item.Tag?.ToString(), out var index))
        {
            return index;
        }

        return OverviewSectionIndex;
    }

    private void ShowSection(int index)
    {
        var showLogs = index == LogsSectionIndex;
        secLogs.Visibility = showLogs ? Visibility.Visible : Visibility.Collapsed;
        crashSectionScroller.Visibility = showLogs ? Visibility.Collapsed : Visibility.Visible;

        var sections = new (int Index, FrameworkElement? Element)[]
        {
            (OverviewSectionIndex, secOverview),
            (CrashesSectionIndex, secCrashes),
            (LaunchSectionIndex, secLaunch),
        };

        foreach (var (sectionIndex, element) in sections)
        {
            if (element is { } section)
            {
                section.Visibility = sectionIndex == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }
    }

    private void PersistSection(int? index = null)
    {
        try
        {
            if (!AppServices.IsInitialized)
            {
                return;
            }

            var target = index ?? CurrentSectionIndex();
            if (AppServices.Settings.LastCrashAnalysisSection == target)
            {
                return;
            }

            AppServices.Settings.LastCrashAnalysisSection = target;
            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存崩溃分析页分节失败: " + saved.ErrorMessage);
            }
        }
        catch (Exception exception)
        {
            AppServices.Logger.Warn("保存崩溃分析页分节失败: " + exception.Message);
        }
    }

    private void RestoreSection()
    {
        var items = navCrashSections.MenuItems.OfType<NavigationViewItem>().ToList();
        var index = AppServices.IsInitialized
            ? AppServices.Settings.LastCrashAnalysisSection ?? OverviewSectionIndex
            : OverviewSectionIndex;
        if (index < 0 || index >= items.Count)
        {
            index = OverviewSectionIndex;
        }

        if (index < items.Count)
        {
            navCrashSections.SelectedItem = items[index];
        }

        ShowSection(index);
    }

    // ------------------------------------------------------------ 操作

    private async void OnRefreshClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.IsBusy)
        {
            return;
        }

        _viewModel.ClearStatus();
        await RunGuardedAsync(
            "CrashAnalysisPage 重新分析失败",
            () => _viewModel.LoadAsync(_lifetime.Token));
    }

    private void OnCopyReportClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var text = _viewModel.CopyReportText;
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
            package.SetText(text);
            Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
            _viewModel.SetStatus(Keys.CrashActionCopied);
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("CrashAnalysisPage 复制报告失败", exception);
            _viewModel.SetStatus(Keys.CrashActionCopyFailed, exception.Message);
        }
    }

    private void OnOpenCrashFolderClick(object sender, RoutedEventArgs e) =>
        OpenFolder(_viewModel.LatestCrashPath);

    private void OnOpenLogFolderClick(object sender, RoutedEventArgs e) =>
        OpenFolder(_viewModel.LogFolderPath);

    private void OnOpenBundleClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string path })
        {
            OpenFolder(path);
        }
    }

    private void OpenFolder(string? path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                _viewModel.SetStatus(Keys.CrashActionOpenFailed, "-");
                return;
            }

            if (!ShellLauncher.TryStart("explorer.exe", "\"" + path + "\"", "CrashAnalysisPage"))
            {
                _viewModel.SetStatus(Keys.CrashActionOpenFailed, path);
            }
        }
        catch (Exception exception)
        {
            AppServices.Logger.Error("CrashAnalysisPage 打开目录失败", exception);
            _viewModel.SetStatus(Keys.CrashActionOpenFailed, exception.Message);
        }
    }

    // ------------------------------------------------------------ 护栏

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
        catch (Exception exception)
        {
            AppServices.Logger.Error(logMessage, exception);
        }
    }
}
