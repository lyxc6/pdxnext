namespace PdxLauncher.App.Views;

using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PdxLauncher.App.Controls;
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

    /// <summary>重载协调器：连点游戏时取消旧重载，只应用最新那次的结果。</summary>
    private readonly ReloadCoordinator _reload = new();

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

            // 回到上次停留的分节（冷启动也生效），并把左栏高亮一起同步；
            // 缓存统计紧接着单独跑一次，所以这里不再重复触发。
            RestoreSection();

            // 缓存占用只看文件属性，独立于设置加载，失败不该影响整页。
            await _viewModel.RefreshCacheStatsAsync(_lifetime.Token).ConfigureAwait(true);

            // 本地模型与术语词典是独立于设置文件的状态，进入页面时读一次。
            _viewModel.RefreshLocalTranslationState();
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
    private void OnSectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs e)
    {
        ShowSection(CurrentSectionIndex());
    }

    private async void OnCheckUpdatesClick(object sender, RoutedEventArgs e)
    {
        await _viewModel.CheckForUpdatesAsync();
    }

    /// <summary>左侧分类用 NavigationViewItem.Tag 携带序号（"0"–"7"），与 XAML 顺序一致。</summary>
    private int CurrentSectionIndex()
    {
        if (navSettingsSections.SelectedItem is NavigationViewItem item
            && int.TryParse(item.Tag?.ToString(), out var index))
        {
            return index;
        }

        return 0;
    }

    private void ShowSection(int index, bool refreshCache = true)
    {
        var sections = new FrameworkElement?[]
        {
            secAppearance, secLaunch, secPaths, secOnline, secCache, secAdvanced, secTranslation, secAbout,
        };

        for (var i = 0; i < sections.Length; i++)
        {
            if (sections[i] is { } section)
            {
                section.Visibility = i == index ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        // 记住停在哪一节：下次进设置页（含冷启动）直接回到这里。
        if (AppServices.IsInitialized)
        {
            AppServices.Settings.LastSettingsSection = index;
            var saved = AppServices.SaveSettings();
            if (!saved.IsSuccess)
            {
                AppServices.Logger.Warn("保存设置页分节失败: " + saved.ErrorMessage);
            }
        }

        if (refreshCache && index == CacheSectionIndex && AppServices.IsInitialized)
        {
            RefreshCacheStatsInBackground();
        }
    }

    /// <summary>
    /// 恢复上次停留的分节：越界或从没记过时落到第一节，并把左栏选中项同步过去，
    /// 避免「内容在缓存节、高亮在外观节」。
    /// </summary>
    private void RestoreSection()
    {
        var items = navSettingsSections.MenuItems.OfType<NavigationViewItem>().ToList();
        var index = AppServices.IsInitialized ? AppServices.Settings.LastSettingsSection ?? 0 : 0;
        if (index < 0 || index >= items.Count)
        {
            index = 0;
        }

        if (index < items.Count)
        {
            navSettingsSections.SelectedItem = items[index];
        }

        AppServices.Logger.Info($"设置页恢复到分节 index={index}（共 {items.Count} 项）");
        ShowSection(index, refreshCache: false);
    }

    /// <summary>进入「缓存」分区时重新统计占用；失败只记日志，不打断浏览。</summary>
    private async void RefreshCacheStatsInBackground() =>
        await RunGuardedAsync(
            "SettingsPage 统计缓存占用失败",
            () => _viewModel.RefreshCacheStatsAsync(_lifetime.Token));

    private Task ReloadAsync() => _reload.RunAsync(
        token => _viewModel.LoadAsync(AppServices.CurrentGame, token),
        _lifetime.Token,
        ex => AppServices.Logger.Error("SettingsPage 加载失败", ex));

    // ------------------------------------------------------------ 主题区

    private async void OnCheckThemeClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 立即检查主题失败",
            () => _viewModel.RefreshThemeAsync(_lifetime.Token),
            ex => ShowMessageAsync(T(Keys.SettingsThemeDialogFailedTitle), ex.Message));

    // ------------------------------------------------------------ 翻译区

    /// <summary>Azure 门户的「创建翻译器资源」页（申请微软翻译密钥的入口）。</summary>
    private const string MicrosoftTranslatePortalUrl =
        "https://portal.azure.com/#create/Microsoft.CognitiveServicesTextTranslation";

    /// <summary>Google Cloud 控制台的「凭据 / API 密钥」页（申请谷歌云翻译密钥的入口）。</summary>
    private const string GoogleCloudTranslatePortalUrl =
        "https://console.cloud.google.com/apis/credentials";

    /// <summary>逐个引擎翻一句探针，把结果写进翻译区（不弹窗，失败也不崩页面）。</summary>
    private async void OnTranslateTestClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 测试翻译失败",
            () => _viewModel.TestTranslationAsync(_lifetime.Token));

    /// <summary>「打开 Azure 门户创建翻译器资源」。</summary>
    private void OnTranslateMicrosoftPortalClick(object sender, RoutedEventArgs e) =>
        OpenTranslatePortal(MicrosoftTranslatePortalUrl, "微软翻译");

    /// <summary>「打开 Google Cloud 控制台创建 API 密钥」。</summary>
    private void OnTranslateGoogleCloudPortalClick(object sender, RoutedEventArgs e) =>
        OpenTranslatePortal(GoogleCloudTranslatePortalUrl, "谷歌云翻译");

    /// <summary>把申请页交给默认浏览器；打不开就在状态里说明，不弹窗打断填写。</summary>
    /// <param name="url">申请页地址。</param>
    /// <param name="who">中文引擎名，用于日志与提示。</param>
    private void OpenTranslatePortal(string url, string who)
    {
        if (ShellLauncher.TryOpenUrl(url, "SettingsPage 打开" + who + "申请页"))
        {
            return;
        }

        _viewModel.SetStatus(Keys.SettingsTranslateStatusPortalFailed, who);
    }

    // -------------------------------------------------- 本地离线翻译

    /// <summary>下载某个本地离线模型（行内进度条会转）。</summary>
    private async void OnDownloadLocalModelClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 下载本地模型失败",
            () => sender is FrameworkElement { DataContext: LocalModelRowViewModel row }
                ? _viewModel.DownloadLocalModelAsync(row, _lifetime.Token)
                : Task.CompletedTask);

    /// <summary>删除某个已安装的本地离线模型。</summary>
    private void OnRemoveLocalModelClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: LocalModelRowViewModel row })
        {
            _viewModel.RemoveLocalModel(row);
        }
    }

    /// <summary>把输入框里的词条追加到词典列表（写盘由「保存词典」负责）。</summary>
    private void OnAddGlossaryEntryClick(object sender, RoutedEventArgs e) =>
        _viewModel.AddGlossaryEntry();

    /// <summary>从词典列表里删掉一条词条（写盘由「保存词典」负责）。</summary>
    private void OnRemoveGlossaryEntryClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: GlossaryEntryViewModel entry })
        {
            _viewModel.RemoveGlossaryEntry(entry);
        }
    }

    /// <summary>把词典列表写盘。</summary>
    private void OnSaveGlossaryClick(object sender, RoutedEventArgs e) =>
        _viewModel.SaveGlossary();

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

        OpenPath(path, T(Keys.SettingsDataDirLabel), Keys.SettingsStatusOpenedDataDir);
    }

    // ------------------------------------------------------------ 程序数据目录

    private void OnOpenAppDataDirectoryClick(object sender, RoutedEventArgs e) =>
        OpenPath(AppServices.DataRoot, T(Keys.SettingsAppDataCurrent));

    private async void OnClearLegacyDataClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 清理旧 C 盘数据失败",
            async () =>
            {
                var legacyPath = DataRootMigrator.LegacyRoot;
                if (!AppServices.FileSystem.DirectoryExists(legacyPath))
                {
                    _viewModel.SetStatus(Keys.SettingsAppDataStatusMissing, legacyPath);
                    return;
                }

                if (!await ConfirmAsync(
                        T(Keys.SettingsAppDataDialogClearTitle),
                        T(Keys.SettingsAppDataDialogClearContent, legacyPath),
                        T(Keys.CommonDelete)).ConfigureAwait(true))
                {
                    return;
                }

                AppServices.FileSystem.Delete(legacyPath);
                AppServices.Logger.Info("已清理旧 C 盘数据目录：" + legacyPath);
                _viewModel.SetStatus(Keys.SettingsAppDataStatusCleared, legacyPath);
            },
            ex => ShowMessageAsync(T(Keys.SettingsAppDataDialogClearFailedTitle), ex.Message));

    /// <summary>
    /// 用资源管理器打开一个目录；失败时写状态栏并记日志。
    /// <paramref name="openedKey"/> 为空时用通用文案拼 label + path，
    /// 否则用指定的「已打开…」状态键（数据目录的文案固定为一句，不经过 label 拼接）。
    /// </summary>
    private void OpenPath(string path, string label, string? openedKey = null)
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

            if (openedKey is null)
            {
                _viewModel.SetStatus(Keys.SettingsStatusOpened, label, path);
            }
            else
            {
                _viewModel.SetStatus(openedKey, path);
            }
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

    private async void OnRefreshCacheClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 统计缓存占用失败",
            () => _viewModel.RefreshCacheStatsAsync(_lifetime.Token));

    private async void OnClearCacheRowClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 清除缓存失败",
            () => sender is FrameworkElement { DataContext: CacheRowViewModel row }
                ? _viewModel.ClearCacheAsync(row, _lifetime.Token)
                : Task.CompletedTask);

    private async void OnClearAllCachesClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 清除全部缓存失败",
            async () =>
            {
                if (!await ConfirmAsync(
                        T(Keys.SettingsCacheDialogClearAllTitle),
                        T(Keys.SettingsCacheDialogClearAllContent),
                        T(Keys.CommonClear)).ConfigureAwait(true))
                {
                    return;
                }

                await _viewModel.ClearAllCachesAsync(_lifetime.Token).ConfigureAwait(true);
            });

    // ------------------------------------------------------------ 补丁

    private async void OnApplyPatchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.Game is null)
            {
                _viewModel.SetStatus(Keys.SettingsStatusSelectGameFirst);
                return;
            }

            var confirmed = await ConfirmOverlay.ConfirmAsync(
                T(Keys.SettingsPatchApplyDialogTitle),
                T(Keys.SettingsPatchApplyDialogContent),
                T(Keys.SettingsPatchApplyConfirm),
                T(Keys.CommonCancel));
            if (!confirmed)
            {
                return;
            }

            await _viewModel.ApplyPatchAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 打补丁失败", ex);
            await ShowMessageAsync(T(Keys.SettingsPatchApplyDialogTitle), ex.Message).ConfigureAwait(true);
        }
    }

    private async void OnRemovePatchClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_viewModel.Game is null)
            {
                _viewModel.SetStatus(Keys.SettingsStatusSelectGameFirst);
                return;
            }

            var confirmed = await ConfirmOverlay.ConfirmAsync(
                T(Keys.SettingsPatchRemoveDialogTitle),
                T(Keys.SettingsPatchRemoveDialogContent),
                T(Keys.SettingsPatchRemoveConfirm),
                T(Keys.CommonCancel));
            if (!confirmed)
            {
                return;
            }

            await _viewModel.RemovePatchAsync(_lifetime.Token).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // 页面关闭导致的取消，忽略。
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 移除补丁失败", ex);
            await ShowMessageAsync(T(Keys.SettingsPatchRemoveDialogTitle), ex.Message).ConfigureAwait(true);
        }
    }

    /// <summary>素材目录可能还不存在（首次运行），这里按需建出来再打开，免得用户对着路径干瞪眼。</summary>
    private void OnOpenPatchAssetsClick(object sender, RoutedEventArgs e)
    {
        var path = AppServices.GamePatch.AssetsDirectory;
        try
        {
            AppServices.FileSystem.EnsureDirectory(path);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 创建补丁素材目录失败：" + path, ex);
            _viewModel.SetStatus(Keys.SettingsStatusOpenFailed, ex.Message);
            return;
        }

        OpenPath(path, T(Keys.SettingsPatchOpenAssetsName));
    }

    // ------------------------------------------------------------ 危险区

    private async void OnRegenerateSignatureClick(object sender, RoutedEventArgs e) =>
        await RunGuardedAsync(
            "SettingsPage 重新生成 dlc_signature 失败",
            async () =>
            {
                if (_viewModel.Game is null)
                {
                    return;
                }

                if (!await ConfirmAsync(
                        T(Keys.SettingsDangerDialogTitle),
                        T(Keys.SettingsDangerDialogContent),
                        T(Keys.SettingsDangerDialogConfirm)).ConfigureAwait(true))
                {
                    return;
                }

                await _viewModel.RegenerateDlcSignatureAsync(_lifetime.Token).ConfigureAwait(true);
            },
            ex => ShowMessageAsync(T(Keys.SettingsDangerDialogFailedTitle), ex.Message));

    // ------------------------------------------------------------ 对话框 / 护栏

    /// <summary>统一的两键确认框：左边取消、右边确认，默认焦点落在确认上。</summary>
    private async Task<bool> ConfirmAsync(string title, string content, string primaryText)
    {
        return await ConfirmOverlay.ConfirmAsync(
            title,
            content,
            primaryText,
            T(Keys.CommonCancel));
    }

    /// <summary>
    /// 事件处理器的统一护栏：页面关闭导致的取消静默忽略，其它异常记日志并按需弹错误框。
    /// </summary>
    private async Task RunGuardedAsync(
        string logMessage,
        Func<Task> action,
        Func<Exception, Task>? onError = null)
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
            if (onError is not null)
            {
                await onError(ex).ConfigureAwait(true);
            }
        }
    }

    private async Task ShowMessageAsync(string title, string message)
    {
        try
        {
            await ConfirmOverlay.ShowMessage(title, message, T(Keys.CommonClose));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("SettingsPage 显示对话框失败", ex);
        }
    }

    private static string T(string key, params object?[] args) => Localizer.Instance.T(key, args);
}
