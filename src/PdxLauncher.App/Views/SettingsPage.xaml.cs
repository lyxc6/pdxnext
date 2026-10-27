namespace PdxLauncher.App.Views;

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using Windows.System;

/// <summary>
/// 设置页：界面语言 / 主题 / 网络代理 / 界面档位 / 启动 / 数据 / 程序数据目录 / Mod 缩略图 / 缓存 / 危险区。
/// 页面只负责对话框、确认与 shell 动作（打开目录）；持久化、统计与解析全部在
/// <see cref="SettingsViewModel"/> 里完成。所有文案经 <see cref="Localizer"/> 取，切语言即时生效。
/// </summary>
public sealed partial class SettingsPage : Page
{
    private readonly SettingsViewModel _viewModel = new();
    /// <summary>页面生命周期令牌；NavigationCacheMode=Required 会在返回页面时重建。</summary>
    private CancellationTokenSource _lifetime = new();

    private bool _isReloading;

    public SettingsPage()
    {
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public SettingsViewModel ViewModel => _viewModel;

    /// <summary>XAML 里取界面文案的入口（切语言时由 Localizer 的属性变更驱动）。</summary>
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

            AlwaysOnTopService.Changed -= OnAlwaysOnTopChanged;
            AlwaysOnTopService.Changed += OnAlwaysOnTopChanged;
            _viewModel.RefreshAlwaysOnTop();

            if (!AppServices.IsInitialized)
            {
                await _viewModel.LoadAsync(null, _lifetime.Token).ConfigureAwait(true);
                return;
            }

            await ReloadAsync().ConfigureAwait(true);

            // 分节导航的初始选中项在 XAML 里给的是第 0 项，这里兜底同步一次可见性；
            // 缓存统计紧接着单独跑一次，所以这里不再重复触发。
            ShowSection(lstSettingsSections.SelectedIndex < 0 ? 0 : lstSettingsSections.SelectedIndex, refreshCache: false);

            // 缓存占用只看文件属性，独立于设置加载，失败不该影响整页。
            await _viewModel.RefreshCacheStatsAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面已关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 初始化失败", ex);
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        AppServices.CurrentGameChanged -= OnCurrentGameChanged;
        AlwaysOnTopService.Changed -= OnAlwaysOnTopChanged;

        // 代理地址是逐键写入内存的，离开页面时统一落盘。
        _viewModel.PersistSettings();
        _lifetime.Cancel();
    }

    /// <summary>
    /// 游戏列表被后台刷新时会带着新的记录实例广播过来（主题素材回写等）。
    /// 同一款游戏走轻量刷新，避免把用户选的可执行文件、输入中的代理地址和签名状态清掉；
    /// 真的换了游戏才整页重载。
    /// </summary>
    private void OnCurrentGameChanged(object? sender, GameInstallation? game)
    {
        if (!DispatcherQueue.TryEnqueue(() => HandleCurrentGameChanged(game)))
        {
            AppServices.Logger.Warn("SettingsPage 无法派发游戏切换事件");
        }
    }

    private void HandleCurrentGameChanged(GameInstallation? game)
    {
        try
        {
            if (_viewModel.RefreshAfterGameRefresh(game))
            {
                return;
            }

            _ = ReloadAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 刷新游戏信息失败", ex);
        }
    }

    /// <summary>标题栏图钉改了置顶状态时，同步设置页的开关。</summary>
    private void OnAlwaysOnTopChanged(object? sender, bool alwaysOnTop)
    {
        if (!DispatcherQueue.TryEnqueue(_viewModel.RefreshAlwaysOnTop))
        {
            AppServices.Logger.Warn("SettingsPage 无法派发置顶状态变化");
        }
    }

    // ------------------------------------------------------------ 分节导航

    /// <summary>「缓存」在左侧六分类里的序号，与 XAML 里 secCache 的顺序一致。</summary>
    private const int CacheSectionIndex = 4;

    /// <summary>切换左侧分类：只显示选中的那一节，其余折叠（同一时刻只排版一类设置）。</summary>
    private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListView { SelectedIndex: >= 0 } list)
        {
            ShowSection(list.SelectedIndex);
        }
    }

    private void ShowSection(int index, bool refreshCache = true)
    {
        var sections = new FrameworkElement?[]
        {
            secAppearance, secLaunch, secPaths, secOnline, secCache, secAdvanced,
        };

        for (var i = 0; i < sections.Length; i++)
        {
            if (sections[i] is { } section)
            {
                section.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        if (refreshCache && index == CacheSectionIndex && AppServices.IsInitialized)
        {
            RefreshCacheStatsInBackground();
        }
    }

    /// <summary>进入「缓存」分区时重新统计占用；失败只记日志，不打断浏览。</summary>
    private async void RefreshCacheStatsInBackground()
    {
        try
        {
            await _viewModel.RefreshCacheStatsAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 统计缓存占用失败", ex);
        }
    }

    private async Task ReloadAsync()
    {
        if (_isReloading)
        {
            return;
        }

        _isReloading = true;
        try
        {
            await _viewModel.LoadAsync(AppServices.CurrentGame, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 忽略：切换页面导致的取消。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 加载失败", ex);
        }
        finally
        {
            _isReloading = false;
        }
    }

    // ------------------------------------------------------------ 主题区

    private async void OnCheckThemeClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshThemeAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 立即检查主题失败", ex);
            await ShowMessageAsync(T(Keys.SettingsThemeDialogFailedTitle), ex.Message).ConfigureAwait(true);
        }
    }

    private void OnProxyUrlChanged(object sender, TextChangedEventArgs e)
    {
        try
        {
            if (sender is not TextBox box)
            {
                return;
            }

            var text = box.Text ?? string.Empty;
            if (string.Equals(text, _viewModel.ProxyUrl, StringComparison.Ordinal))
            {
                return;
            }

            _viewModel.ProxyUrl = text;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 保存代理地址失败", ex);
        }
    }

    private void OnProxyUrlLostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            // 落盘 + 重建 HttpFetcher/Metadata/Downloader 链路，新代理对下一次联网立即生效。
            _viewModel.ApplyProxySettings();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 保存代理地址失败", ex);
        }
    }

    /// <summary>回车也算「改完了」：不用先点别处再失焦。</summary>
    private void OnProxyUrlKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Enter)
        {
            return;
        }

        try
        {
            _viewModel.ApplyProxySettings();
            e.Handled = true;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 回车应用代理设置失败", ex);
        }
    }

    // ------------------------------------------------------------ 数据区

    private void OnOpenDataDirectoryClick(object sender, RoutedEventArgs e)
    {
        var path = _viewModel.GameDataPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            _viewModel.SetStatus(Keys.SettingsStatusSelectGameFirst);
            return;
        }

        OpenExplorer(path, T(Keys.SettingsDataDirLabel), Keys.SettingsStatusOpenedDataDir);
    }

    // ------------------------------------------------------------ 程序数据目录

    private void OnOpenAppDataDirectoryClick(object sender, RoutedEventArgs e) =>
        OpenDirectory(AppServices.DataRoot, T(Keys.SettingsAppDataCurrent));

    private async void OnClearLegacyDataClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var legacyPath = DataRootMigrator.LegacyRoot;
            if (!AppServices.FileSystem.DirectoryExists(legacyPath))
            {
                _viewModel.SetStatus(Keys.SettingsAppDataStatusMissing, legacyPath);
                return;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = T(Keys.SettingsAppDataDialogClearTitle),
                Content = T(Keys.SettingsAppDataDialogClearContent, legacyPath),
                PrimaryButtonText = T(Keys.CommonDelete),
                CloseButtonText = T(Keys.CommonCancel),
                DefaultButton = ContentDialogButton.Close,
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            AppServices.FileSystem.Delete(legacyPath);
            AppServices.Logger.Info("已清理旧 C 盘数据目录：" + legacyPath);
            _viewModel.SetStatus(Keys.SettingsAppDataStatusCleared, legacyPath);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 清理旧 C 盘数据失败", ex);
            await ShowMessageAsync(T(Keys.SettingsAppDataDialogClearFailedTitle), ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>用资源管理器打开一个目录；失败时写状态栏并记日志。</summary>
    private void OpenDirectory(string path, string label)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !AppServices.FileSystem.DirectoryExists(path))
            {
                _viewModel.SetStatus(Keys.SettingsStatusDirMissing, label, path);
                return;
            }

            if (!StartExplorer(path))
            {
                _viewModel.SetStatus(Keys.SettingsStatusOpenFailed, path);
                return;
            }

            _viewModel.SetStatus(Keys.SettingsStatusOpened, label, path);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 打开目录失败：" + path, ex);
            _viewModel.SetStatus(Keys.SettingsStatusOpenFailed, ex.Message);
        }
    }

    /// <summary>数据目录的文案固定为「已打开数据目录」这一句，不经过 label 拼接。</summary>
    private void OpenExplorer(string path, string label, string openedKey)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path) || !AppServices.FileSystem.DirectoryExists(path))
            {
                _viewModel.SetStatus(Keys.SettingsStatusDirMissing, label, path);
                return;
            }

            if (!StartExplorer(path))
            {
                _viewModel.SetStatus(Keys.SettingsStatusOpenFailed, path);
                return;
            }

            _viewModel.SetStatus(openedKey, path);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 打开目录失败：" + path, ex);
            _viewModel.SetStatus(Keys.SettingsStatusOpenFailed, ex.Message);
        }
    }

    /// <summary>打开资源管理器属于 shell 动作，不是文件内容读写，故不经 IFileSystem。</summary>
    private static bool StartExplorer(string path)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "explorer.exe",
            Arguments = "\"" + path + "\"",
            UseShellExecute = true,
        };

        using var process = Process.Start(startInfo);
        if (process is not null)
        {
            return true;
        }

        AppServices.Logger.Warn("SettingsPage 打开目录失败（Process.Start 返回 null）：" + path);
        return false;
    }

    // ------------------------------------------------------------ 缓存

    private async void OnRefreshCacheClick(object sender, RoutedEventArgs e)
    {
        try
        {
            await _viewModel.RefreshCacheStatsAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 统计缓存占用失败", ex);
        }
    }

    private async void OnClearCacheRowClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not FrameworkElement { DataContext: CacheRowViewModel row })
            {
                return;
            }

            await _viewModel.ClearCacheAsync(row, _lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 清除缓存失败", ex);
        }
    }

    private async void OnClearAllCachesClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = T(Keys.SettingsCacheDialogClearAllTitle),
                Content = T(Keys.SettingsCacheDialogClearAllContent),
                PrimaryButtonText = T(Keys.CommonClear),
                CloseButtonText = T(Keys.CommonCancel),
                DefaultButton = ContentDialogButton.Close,
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.ClearAllCachesAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 清除全部缓存失败", ex);
        }
    }

    // ------------------------------------------------------------ 危险区

    private async void OnRegenerateSignatureClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.Game is null)
            {
                return;
            }

            var confirm = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = T(Keys.SettingsDangerDialogTitle),
                Content = T(Keys.SettingsDangerDialogContent),
                PrimaryButtonText = T(Keys.SettingsDangerDialogConfirm),
                CloseButtonText = T(Keys.CommonCancel),
                DefaultButton = ContentDialogButton.Close,
            };

            var result = await confirm.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            await _viewModel.RegenerateDlcSignatureAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 重新生成 dlc_signature 失败", ex);
            await ShowMessageAsync(T(Keys.SettingsDangerDialogFailedTitle), ex.Message).ConfigureAwait(true);
        }
    }

    // ------------------------------------------------------------ 对话框

    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot,
                Title = title,
                Content = message,
                CloseButtonText = T(Keys.CommonClose),
            };

            await dialog.ShowAsync();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 显示对话框失败", ex);
        }
    }

    private static string T(string key, params object?[] args) => Localizer.Instance.T(key, args);
}
