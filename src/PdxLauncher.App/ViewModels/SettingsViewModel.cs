namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation.Local;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Storage;
using PdxLauncher.Core.Updates;

/// <summary>
/// 设置页的视图模型：界面语言、主题来源与联网检查、网络代理、界面档位、启动参数、
/// 数据目录、七类联网缓存与危险区（dlc_signature）。
/// 只读展示 + 少量可写设置，全部持久化到 AppServices.Settings 并立即 SaveSettings()。
/// 不含任何 Microsoft.UI.Xaml 类型；ContentDialog 确认与"打开目录"等 shell 动作由页面负责。
/// 文案统一走 <see cref="LocalizedObservableObject"/>：语言切换后基类重发属性通知，
/// 状态栏这类拼好的句子用 (键, 参数) 记住来源，在 <see cref="Relocalize"/> 里按新语言重建。
/// </summary>
public sealed class SettingsViewModel : LocalizedObservableObject
{
    /// <summary>
    /// 主题缓存 TTL：24 小时。索引最后检查时间 + TTL 只用来展示「下次检查」；
    /// 真正的刷新判定在 ThemeResolver/ThemeRefreshDecision，退避与 force 都优先于 TTL。
    /// </summary>
    public static readonly TimeSpan ThemeCacheTtl =
        TimeSpan.FromMilliseconds(ThemeRefreshDecision.DefaultTtlMilliseconds);

    private GameInstallation? _game;
    private ThemeSpec? _theme;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _isRelocalizing;
    /// <summary>安装形态判定结果；null 表示注册表还没读完（界面显示「正在检查」）。</summary>
    private InstallMode? _installMode;
    /// <summary>卸载记录里的目录；PathMismatch 时展示，便于用户判断是不是装了两份。</summary>
    private string? _recordedInstallDirectory;
    private string _statusMessage = string.Empty;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _proxyUrl = string.Empty;
    private string _proxyStatusText = string.Empty;
    private string _detectedProxyText = string.Empty;

    /// <summary>检测到的代理地址（null = 没检测到）。</summary>
    private string? _detectedProxyUrl;
    private readonly ObservableCollection<NetworkProbeRowViewModel> _probeRows = new();

    /// <summary>最近一次连通性自检的原始结果；null = 还没测过。切语言后按它重建行文案。</summary>
    private NetworkProbeReport? _probeReport;
    private bool _isProbeRunning;
    private string _probeSummaryText = string.Empty;
    private string _steamApiStatusText = string.Empty;
    private long _cacheTotalBytes;
    private ProxyModeOption _selectedProxyMode =
        ProxyModeCatalog.Resolve(AppServices.Settings.ProxyMode, AppServices.Settings.ProxyUrl);
    private AlternativeExecutable? _selectedAlternative;
    private string _lastSignature = string.Empty;
    private GamePatchReport? _patchReport;
    private string _patchStatusText = string.Empty;
    private string _patchDetailText = string.Empty;
    private PlaySetPageDisplayModeOption _selectedPlaySetDisplayMode =
        PlaySetPageDisplayModeCatalog.Resolve(AppServices.Settings.PlaySetPageDisplayMode);
    private RetentionDaysOption _selectedModThumbnailRetention =
        RetentionDaysCatalog.ModThumbnails.Resolve(AppServices.Settings.ModThumbnailCacheDays);
    private RetentionDaysOption _selectedDlcDescriptionRetention =
        RetentionDaysCatalog.DlcDescriptions.Resolve(AppServices.Settings.DlcDescriptionCacheDays);
    private BackgroundDisplayModeOption _selectedBackgroundDisplayMode =
        BackgroundDisplayModeCatalog.Resolve(AppServices.Settings.BackgroundDisplayMode);
    private ModThumbnailSizeOption _selectedModThumbnailSize =
        ModThumbnailSizeCatalog.Resolve(AppServices.Settings.ModThumbnailSize);
    private UiLanguageOption _selectedUiLanguage =
        UiLanguageCatalog.Resolve(AppServices.Settings.UiLanguage);
    private TranslationEngineOption _selectedTranslationEngine =
        TranslationEngineCatalog.Resolve(AppServices.Settings.TranslateEngineMode);
    private TranslationTargetLanguageOption _selectedTranslationLanguage =
        TranslationTargetLanguageCatalog.Resolve(AppServices.Settings.TranslateTargetLanguage);
    private string _translateBaseUrl = string.Empty;
    private string _translateApiKey = string.Empty;
    private string _translateModel = string.Empty;
    private string _translateMicrosoftKey = string.Empty;
    private string _translateMicrosoftRegion = string.Empty;
    private string _translateGoogleKey = string.Empty;
    private bool _isTranslateTesting;
    private string _translateTestText = string.Empty;
    private IReadOnlyList<TranslationProbeResult>? _translateTestResults;
    private string _glossarySourceInput = string.Empty;
    private string _glossaryTargetInput = string.Empty;
    private readonly ObservableCollection<LocalModelRowViewModel> _localModelRows = new();
    private readonly ObservableCollection<GlossaryEntryViewModel> _glossaryEntries = new();
    private bool _isCheckingUpdates;
    private string _updateStatusText = string.Empty;

    public SettingsViewModel()
    {
        CacheRows = new ObservableCollection<CacheRowViewModel>(CacheRowCatalog.Create());
    }

    /// <summary>
    /// Steam 工坊直连订阅开关（实验性，默认关）：写入 AppSettings.SteamWorkshopSubscribeEnabled 并登记延迟落盘。
    /// 关掉时不碰任何 Steamworks 接口，详情页按钮退回「在 Steam 客户端里打开」。
    /// </summary>
    public bool SteamSubscribeEnabled
    {
        get => AppServices.Settings.SteamWorkshopSubscribeEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.SteamWorkshopSubscribeEnabled,
            v => AppServices.Settings.SteamWorkshopSubscribeEnabled = v,
            Keys.SettingsSteamSubscribeStatusOn,
            Keys.SettingsSteamSubscribeStatusOff,
            afterSave: RefreshSteamApiStatus);
    }

    /// <summary>
    /// Steam 工坊状态开关（默认开）：只为读订阅/安装/下载状态而拉起宿主子进程。
    /// 它是只读的，不会订阅、退订或改 Steam 的任何东西；每次查询会让 Steam 有几秒以为游戏在跑。
    /// </summary>
    public bool SteamStateEnabled
    {
        get => AppServices.Settings.SteamWorkshopStateEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.SteamWorkshopStateEnabled,
            v => AppServices.Settings.SteamWorkshopStateEnabled = v,
            Keys.SettingsSteamStateOn,
            Keys.SettingsSteamStateOff,
            afterSave: RefreshSteamApiStatus);
    }

    /// <summary>steam_api64.dll 的检测结果（找到 / 没找到及原因），供设置页直显。</summary>
    public string SteamApiStatusText => _steamApiStatusText;

    /// <summary>
    /// 重新检测 steam_api64.dll：与本启动器实际加载时用的是同一套查找顺序
    /// （手填路径 → 程序目录 → 仓库 sdk/ → 游戏目录），所以这里显示的结果就是订阅时用的结果。
    /// </summary>
    public void RefreshSteamApiStatus()
    {
        try
        {
            var locator = new SteamApiLocator(AppServices.FileSystem, AppServices.Logger);
            var resolved = locator.Resolve(
                AppServices.Settings.SteamApiDllPath,
                AppContext.BaseDirectory,
                AppServices.CurrentGame);

            SetSteamApiStatus(resolved is null
                ? T(Keys.SettingsSteamApiMissing, DescribeSteamApiCandidates(locator))
                : T(Keys.SettingsSteamApiReady));
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("检测 steam_api64.dll 失败: " + ex.Message);
            SetSteamApiStatus(T(Keys.SettingsSteamApiMissing, ex.Message));
        }
    }

    /// <summary>把候选路径压成一句短说明（只列前两条，避免状态栏过长）。</summary>
    private static string DescribeSteamApiCandidates(SteamApiLocator locator)
    {
        var candidates = locator.Candidates(
            AppServices.Settings.SteamApiDllPath,
            AppContext.BaseDirectory,
            AppServices.CurrentGame);

        return candidates.Count switch
        {
            0 => "无候选路径",
            1 => candidates[0],
            _ => string.Join(" / ", candidates.Take(2)),
        };
    }

    private void SetSteamApiStatus(string text)
    {
        if (string.Equals(_steamApiStatusText, text, StringComparison.Ordinal))
        {
            return;
        }

        _steamApiStatusText = text;
        OnPropertyChanged(nameof(SteamApiStatusText));
    }

    public GameInstallation? Game
    {
        get => _game;
        private set
        {
            if (SetProperty(ref _game, value))
            {
                OnPropertyChanged(nameof(GameTitle));
            }
        }
    }

    public string GameTitle => Game?.DisplayName ?? T(Keys.SettingsGameNone);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanApplyPatch));
                OnPropertyChanged(nameof(CanRemovePatch));
            }
        }
    }

    public bool IsLoaded
    {
        get => _isLoaded;
        private set => SetProperty(ref _isLoaded, value);
    }

    /// <summary>状态栏文案（已经是拼好的句子）。</summary>
    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    /// <summary>是否已经有一条可显示的状态文案（空状态时把状态栏收起来，避免留一条空框）。</summary>
    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    // ------------------------------------------------------------ 主题区

    /// <summary>当前主题来源：联网缓存 / 安装目录 / 内置素材 / Steam 封面 / 内置占位。</summary>
    public string ThemeSourceText
    {
        get
        {
            var text = T(ThemeSourceKey);
            if (!GetThemeDiagnostics().MetadataMissing)
            {
                return text;
            }

            // 用 Steam 官方库页图时，「元数据缺失」只是补充说明，不能再说成「用内置素材」。
            return CurrentThemeSource == ThemeAssetSource.SteamHero
                ? text + " · " + T(Keys.SettingsThemeMetadataMissingNoMetadata)
                : text + " · " + T(Keys.SettingsThemeMetadataMissing);
        }
    }

    /// <summary>当前主题来源（展示文案与状态栏共用）。</summary>
    private ThemeAssetSource CurrentThemeSource =>
        _theme?.Source ?? Game?.Theme.Source ?? ThemeAssetSource.BuiltInPlaceholder;

    /// <summary>当前主题来源对应的本地化键（展示文案与状态栏共用，避免两处 switch 漂移）。</summary>
    private string ThemeSourceKey
    {
        get
        {
            return CurrentThemeSource switch
            {
                ThemeAssetSource.OnlineCache => Keys.SettingsThemeSourceOnline,
                ThemeAssetSource.InstallDir => Keys.SettingsThemeSourceInstallDir,
                ThemeAssetSource.SteamCapsule => Keys.SettingsThemeSourceSteam,
                ThemeAssetSource.SteamHero => Keys.SettingsThemeSourceSteamHero,
                ThemeAssetSource.Bundled => Keys.SettingsThemeSourceBundled,
                _ => Keys.SettingsThemeSourceBuiltIn,
            };
        }
    }

    /// <summary>上次联网检查时间（本地时间）；从未检查过显示"从未检查"。</summary>
    public string LastCheckText
    {
        get
        {
            var last = GetLastCheckUtc();
            return last is null ? T(Keys.SettingsThemeNeverChecked) : FormatLocal(last.Value);
        }
    }

    /// <summary>
    /// 下次检查时间：有失败退避时优先显示退避到点时间，否则 = 上次检查 + 24 小时；
    /// 从未检查时显示「下次启动时」。
    /// </summary>
    public string NextCheckText
    {
        get
        {
            var diagnostics = GetThemeDiagnostics();
            if (diagnostics.RetryAfterUtc is { } retryAfter)
            {
                return FormatLocal(retryAfter);
            }

            var last = diagnostics.LastCheckedUtc;
            return last is null
                ? T(Keys.SettingsThemeNextOnStartup)
                : FormatLocal(last.Value + ThemeCacheTtl);
        }
    }

    /// <summary>当前背景文件路径 + 本地修改时间；没有背景时显示「无」。</summary>
    public string BackgroundFileText
    {
        get
        {
            var path = _theme?.Background ?? Game?.Theme.Background;
            if (string.IsNullOrWhiteSpace(path))
            {
                return T(Keys.SettingsThemeFileNone);
            }

            try
            {
                var modified = File.GetLastWriteTimeUtc(path)
                    .ToLocalTime()
                    .ToString("g", CultureInfo.CurrentCulture);
                return path + " · " + modified;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return path;
            }
        }
    }

    /// <summary>上次失败原因 + 时间；没有失败时显示「无」。</summary>
    public string LastFailureText
    {
        get
        {
            var diagnostics = GetThemeDiagnostics();
            if (string.IsNullOrWhiteSpace(diagnostics.LastError))
            {
                return T(Keys.SettingsThemeFailureNone);
            }

            return diagnostics.LastErrorUtc is { } at
                ? diagnostics.LastError + " · " + FormatLocal(at)
                : diagnostics.LastError;
        }
    }

    /// <summary>联网刷新开关；写入 AppSettings.OnlineThemeRefreshEnabled 并登记延迟落盘。</summary>
    public bool OnlineRefreshEnabled
    {
        get => AppServices.Settings.OnlineThemeRefreshEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.OnlineThemeRefreshEnabled,
            v => AppServices.Settings.OnlineThemeRefreshEnabled = v,
            Keys.SettingsThemeStatusOn,
            Keys.SettingsThemeStatusOff);
    }

    /// <summary>DLC 简介联网开关；写入 AppSettings.OnlineDlcDescriptionEnabled 并登记延迟落盘。</summary>
    public bool DlcDescriptionOnlineEnabled
    {
        get => AppServices.Settings.OnlineDlcDescriptionEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.OnlineDlcDescriptionEnabled,
            v => AppServices.Settings.OnlineDlcDescriptionEnabled = v,
            Keys.SettingsDataStatusDlcOn,
            Keys.SettingsDataStatusDlcOff,
            statusArgs: new object?[] { AppServices.Settings.DlcDescriptionCacheDays });
    }

    /// <summary>官方公告联网开关；写入 AppSettings.NewsFeedOnlineEnabled 并登记延迟落盘。</summary>
    public bool NewsFeedOnlineEnabled
    {
        get => AppServices.Settings.NewsFeedOnlineEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.NewsFeedOnlineEnabled,
            v => AppServices.Settings.NewsFeedOnlineEnabled = v,
            Keys.SettingsDataStatusNewsOn,
            Keys.SettingsDataStatusNewsOff);
    }

    /// <summary>可选的网络代理模式（直接连接 / 跟随系统 / 手动配置）。</summary>
    public IReadOnlyList<ProxyModeOption> ProxyModeOptions => ProxyModeCatalog.All;

    /// <summary>
    /// 网络代理模式。改动立即写 settings.json 并重建联网链路——与界面上「修改后立即生效」一致。
    /// 切到「直接连接」时不再看 proxyUrl / 系统代理 / 环境变量。
    /// </summary>
    public ProxyModeOption SelectedProxyMode
    {
        get => _selectedProxyMode;
        set
        {
            if (value is null || ReferenceEquals(_selectedProxyMode, value))
            {
                return;
            }

            _selectedProxyMode = value;
            AppServices.Settings.ProxyMode = value.Key;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsManualProxy));
            ApplyProxySettings();
        }
    }

    /// <summary>只有手动配置模式才显示地址输入框。</summary>
    public bool IsManualProxy =>
        string.Equals(_selectedProxyMode.Key, ProxyModes.Manual, StringComparison.Ordinal);

    /// <summary>
    /// 手动配置模式下的代理地址（如 127.0.0.1:7890）；空字符串表示直连。
    /// 输入框每次按键都会走 setter，这里只更新内存中的 AppSettings，
    /// 落盘交给 <see cref="PersistSettings"/>（输入框失焦 / 回车 / 页面卸载时调用），避免逐键写盘。
    /// </summary>
    public string ProxyUrl
    {
        get => _proxyUrl;
        set
        {
            if (SetProperty(ref _proxyUrl, value))
            {
                AppServices.Settings.ProxyUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }
    }

    /// <summary>当前实际生效的代理（解析系统代理 / 环境变量后的结果），显示在下拉框下面一行。</summary>
    public string ProxyStatusText
    {
        get => _proxyStatusText;
        private set => SetProperty(ref _proxyStatusText, value);
    }

    /// <summary>
    /// 「检测到但没在用」的代理地址（只读探测系统代理 / 环境变量，不改任何设置）。
    /// <para>
    /// 很多人明明开着代理，程序却在直连 —— 直连遇到 DNS 被污染时会一直转圈。
    /// 把检测到的地址摆出来并给一键切换，比让人自己猜要省事。
    /// </para>
    /// </summary>
    public string DetectedProxyText
    {
        get => _detectedProxyText;
        private set => SetProperty(ref _detectedProxyText, value);
    }

    /// <summary>检测到了可用代理但当前没在用：给「使用」按钮用。</summary>
    public bool HasDetectedProxy => _detectedProxyUrl is not null;

    /// <summary>
    /// 一键使用检测到的代理：写成「手动配置」并立即重建联网链路。
    /// 返回是否成功；失败原因写进状态栏（不抛异常）。
    /// </summary>
    public bool UseDetectedProxy()
    {
        var url = _detectedProxyUrl;
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        _proxyUrl = url;
        OnPropertyChanged(nameof(ProxyUrl));
        AppServices.Settings.ProxyUrl = url;

        _selectedProxyMode = ProxyModeCatalog.Resolve(ProxyModes.Manual, url);
        OnPropertyChanged(nameof(SelectedProxyMode));
        OnPropertyChanged(nameof(IsManualProxy));

        return ApplyProxySettings();
    }

    /// <summary>
    /// 连通性自检的结果行（一台主机一行）。
    /// <para>
    /// 创意工坊的列表/详情是启动器自己发的 HTTP，不走 Steam 客户端；加速器默认只接管 Steam 进程。
    /// 这张表让用户直接看到「本程序能不能连上」，而不是猜。
    /// </para>
    /// </summary>
    public ObservableCollection<NetworkProbeRowViewModel> ProbeRows => _probeRows;

    /// <summary>自检是否在跑（按钮禁用 + 转圈）。</summary>
    public bool IsProbeRunning
    {
        get => _isProbeRunning;
        private set
        {
            if (SetProperty(ref _isProbeRunning, value))
            {
                OnPropertyChanged(nameof(CanRunNetworkProbe));
                OnPropertyChanged(nameof(ProbeRunText));
            }
        }
    }

    /// <summary>没在跑时才能再点。</summary>
    public bool CanRunNetworkProbe => !_isProbeRunning;

    /// <summary>按钮文案：测试 / 测试中…</summary>
    public string ProbeRunText =>
        T(_isProbeRunning ? Keys.SettingsProxyProbeRunning : Keys.SettingsProxyProbeRun);

    /// <summary>测过至少一次才显示结果表。</summary>
    public bool HasProbeRows => _probeRows.Count > 0;

    /// <summary>当前不直连时才有「直连」对比列。</summary>
    public bool HasDirectProbeColumn => _probeReport?.Direct is not null;

    /// <summary>结果摘要：全通或几项失败。</summary>
    public string ProbeSummaryText
    {
        get => _probeSummaryText;
        private set => SetProperty(ref _probeSummaryText, value);
    }

    /// <summary>
    /// 跑一次连通性自检：按当前代理模式测三台 Steam 主机，非直连时再直连测一遍做对比。
    /// 失败只记日志并把摘要写成「失败」，不抛异常（按钮点击不该崩页面）。
    /// </summary>
    public async Task RunNetworkProbeAsync()
    {
        if (_isProbeRunning)
        {
            return;
        }

        IsProbeRunning = true;
        try
        {
            var proxy = ProxyResolver.Resolve(AppServices.Settings, AppServices.Logger);
            _probeReport = await NetworkProbe.RunAsync(AppServices.Logger, proxy).ConfigureAwait(true);
            RebuildProbeRows();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppServices.Logger.Error("设置页连通性自检失败", ex);
            _probeReport = null;
            _probeRows.Clear();
            ProbeSummaryText = T(Keys.SettingsProxyProbeError);
            OnPropertyChanged(nameof(HasProbeRows));
            OnPropertyChanged(nameof(HasDirectProbeColumn));
        }
        finally
        {
            IsProbeRunning = false;
        }
    }

    /// <summary>把内存中的 AppSettings 落盘（页面失焦 / 卸载时调用）。</summary>
    public void PersistSettings()
    {
        var result = AppServices.SaveSettings();
        if (!result.IsSuccess)
        {
            SetStatus(Keys.SettingsStatusSaveFailed, result.ErrorMessage ?? string.Empty);
        }
    }

    // ------------------------------------------------------------ 关于与更新

    /// <summary>启动时是否自动检查更新（只影响静默检查，手动检查不受影响）。</summary>
    public bool AutoCheckUpdates
    {
        get => AppServices.Settings.AutoCheckUpdates;
        set
        {
            if (AppServices.Settings.AutoCheckUpdates == value)
            {
                return;
            }

            AppServices.Settings.AutoCheckUpdates = value;
            OnPropertyChanged();
            PersistSettings();
        }
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        private set
        {
            if (SetProperty(ref _isCheckingUpdates, value))
            {
                OnPropertyChanged(nameof(CanCheckUpdates));
            }
        }
    }

    public bool CanCheckUpdates => !_isCheckingUpdates;

    public string LauncherVersionText => AppServices.Updates?.CurrentVersion ?? "1.0.0";

    /// <summary>
    /// 安装形态文案。判定只认 Core 的 <see cref="InstallModeResolver"/>：已安装时按 Scope 区分
    /// 当前用户 / 所有用户；便携运行时按 <see cref="InstallModeReason"/> 说明差在哪条证据。
    /// 旧实现只比较 <c>%LOCALAPPDATA%\Programs</c> 前缀，装在 D 盘就必然显示「便携」。
    /// </summary>
    public string InstallModeText
    {
        get
        {
            var mode = _installMode;
            if (mode is null)
            {
                return T(Keys.SettingsAboutInstallModeChecking);
            }

            if (mode.IsInstalled)
            {
                return mode.Scope == InstallScope.PerMachine
                    ? T(Keys.SettingsAboutInstallModeInstalledPerMachine)
                    : T(Keys.SettingsAboutInstallModeInstalledPerUser);
            }

            return mode.Reason switch
            {
                InstallModeReason.PathMismatch => T(Keys.SettingsAboutInstallModePathMismatch),
                InstallModeReason.ExeMissing => T(Keys.SettingsAboutInstallModeExeMissing),
                InstallModeReason.MarkerOnly => T(Keys.SettingsAboutInstallModeMarkerOnly),
                _ => T(Keys.SettingsAboutInstallModeNoEntry),
            };
        }
    }

    /// <summary>卸载项里的 DisplayVersion；没有就整行不显示。</summary>
    public string InstallModeVersionText =>
        string.IsNullOrWhiteSpace(_installMode?.DisplayVersion)
            ? string.Empty
            : T(Keys.SettingsAboutInstallModeVersion, _installMode!.DisplayVersion);

    public bool HasInstallModeVersion => !string.IsNullOrEmpty(InstallModeVersionText);

    /// <summary>
    /// 要展示的目录：已安装 / 主程序缺失时是本程序目录；目录不一致时是卸载记录里的目录，
    /// 让用户能自己判断是不是装了两份。
    /// </summary>
    public string InstallModeDirectory
    {
        get
        {
            var mode = _installMode;
            if (mode is null)
            {
                return string.Empty;
            }

            return mode.Reason == InstallModeReason.PathMismatch
                ? _recordedInstallDirectory ?? string.Empty
                : mode.InstallDirectory ?? string.Empty;
        }
    }

    public bool HasInstallModeDirectory => InstallModeDirectory.Length > 0;

    /// <summary>「打开安装目录」按钮：目录不存在或为空时禁用。</summary>
    public bool CanOpenInstallModeDirectory =>
        HasInstallModeDirectory && AppServices.FileSystem.DirectoryExists(InstallModeDirectory);

    /// <summary>目录一行的标题：路径不一致时写「记录中的目录」，避免误导成当前程序目录。</summary>
    public string InstallModeDirectoryLabel => _installMode?.Reason == InstallModeReason.PathMismatch
        ? T(Keys.SettingsAboutInstallModeRecordedDirectory)
        : T(Keys.SettingsAboutInstallModeDirectory);

    /// <summary>主程序缺失 / 只有 marker 时补一句解释，其余情况不占位。</summary>
    public bool HasInstallModeHint =>
        _installMode?.Reason is InstallModeReason.ExeMissing or InstallModeReason.MarkerOnly;

    public string InstallModeHintText => _installMode?.Reason switch
    {
        InstallModeReason.ExeMissing => T(Keys.SettingsAboutInstallModeExeMissingHint),
        InstallModeReason.MarkerOnly => T(Keys.SettingsAboutInstallModeMarkerOnlyHint),
        _ => string.Empty,
    };

    /// <summary>当前数据目录（与「程序数据目录」区同一来源，只读展示）。</summary>
    public string InstallModeDataDirectory => AppServices.DataRoot;

    /// <summary>
    /// Program Files 里的绿色拷贝会出现「设置页说便携、数据却落在每机器安装的位置」。
    /// 这时把数据目录也摆出来并解释一句，别让两处说法打架。
    /// </summary>
    public bool ShowInstallModeDataNote
    {
        get
        {
            var mode = _installMode;
            if (mode is null || mode.IsInstalled)
            {
                return false;
            }

            var perMachineDataRoot = Path.Combine(
                PdxDataRoot.LocalAppDataDirectory,
                PdxDataRoot.PerMachineDataFolderName);
            return string.Equals(
                Path.TrimEndingDirectorySeparator(AppServices.DataRoot),
                Path.TrimEndingDirectorySeparator(perMachineDataRoot),
                StringComparison.OrdinalIgnoreCase);
        }
    }

    public string InstallModeDataNoteText =>
        T(Keys.SettingsAboutInstallModeDataNote, AppServices.DataRoot);

    /// <summary>
    /// 后台读注册表并判定安装形态。注册表可能读得慢，判定整体放在线程池上；
    /// 任何失败都退化成「便携运行（未找到安装记录）」，绝不把异常抛给页面。
    /// </summary>
    public async Task RefreshInstallModeAsync(CancellationToken cancellationToken)
    {
        if (_installMode is not null)
        {
            return;
        }

        var baseDirectory = AppContext.BaseDirectory;
        InstallMode mode;
        string? recordedDirectory;
        try
        {
            var result = await Task.Run(
                () =>
                {
                    var marker = PdxDataRoot.ReadInstallMode(baseDirectory);
                    var source = new RegistryUninstallEntrySource(
                        AppServices.IsInitialized ? AppServices.Logger : null);
                    var entries = source.ReadEntries();
                    return (Mode: InstallModeResolver.Resolve(baseDirectory, marker, entries), Entries: entries);
                },
                cancellationToken).ConfigureAwait(true);

            mode = result.Mode;
            recordedDirectory = result.Entries
                .Select(entry => entry.AppPath)
                .FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            (AppServices.IsInitialized ? AppServices.Logger : null)?.Warn(
                "读取安装记录失败，安装模式按便携处理: " + ex.Message);
            mode = InstallMode.PortableRun(InstallModeReason.NoEntry);
            recordedDirectory = null;
        }

        _installMode = mode;
        _recordedInstallDirectory = recordedDirectory;
        OnPropertyChanged(nameof(InstallModeText));
        OnPropertyChanged(nameof(InstallModeVersionText));
        OnPropertyChanged(nameof(HasInstallModeVersion));
        OnPropertyChanged(nameof(InstallModeDirectory));
        OnPropertyChanged(nameof(HasInstallModeDirectory));
        OnPropertyChanged(nameof(CanOpenInstallModeDirectory));
        OnPropertyChanged(nameof(InstallModeDirectoryLabel));
        OnPropertyChanged(nameof(HasInstallModeHint));
        OnPropertyChanged(nameof(InstallModeHintText));
        OnPropertyChanged(nameof(InstallModeDataDirectory));
        OnPropertyChanged(nameof(ShowInstallModeDataNote));
        OnPropertyChanged(nameof(InstallModeDataNoteText));
    }

    public string LastUpdateCheckText
    {
        get
        {
            var last = AppServices.Settings.LastUpdateCheckUtc;
            return last is null
                ? T(Keys.SettingsAboutNever)
                : last.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
        }
    }

    public string UpdateStatusText
    {
        get => _updateStatusText;
        private set => SetProperty(ref _updateStatusText, value);
    }

    /// <summary>设置页的「立即检查」：手动检查总是访问网络，发现更新会弹全局浮层。</summary>
    public async Task CheckForUpdatesAsync()
    {
        if (_isCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        UpdateStatusText = T(Keys.SettingsAboutChecking);
        try
        {
            var result = await UpdateFlow.CheckAsync(manual: true).ConfigureAwait(true);
            UpdateStatusText = result?.Status switch
            {
                UpdateCheckStatus.UpToDate => T(Keys.SettingsAboutStatusUpToDate),
                UpdateCheckStatus.Available when result.Manifest is { } manifest =>
                    T(Keys.SettingsAboutStatusAvailable, manifest.Version),
                UpdateCheckStatus.NotDue => T(Keys.SettingsAboutStatusNotDue),
                UpdateCheckStatus.Failed => T(Keys.SettingsAboutCheckFailed, result.Message),
                UpdateCheckStatus.Skipped when result.Manifest is { } skipped =>
                    T(Keys.SettingsAboutStatusAvailable, skipped.Version),
                _ => string.Empty,
            };
        }
        catch (Exception ex)
        {
            AppServices.Logger.Warn("手动检查更新失败: " + ex.Message);
            UpdateStatusText = T(Keys.SettingsAboutCheckFailed, ex.Message);
        }
        finally
        {
            IsCheckingUpdates = false;
            OnPropertyChanged(nameof(LastUpdateCheckText));
        }
    }

    // ------------------------------------------------------------ 界面区

    /// <summary>可选的界面语言（跟随系统 / 简体中文 / English）。</summary>
    public IReadOnlyList<UiLanguageOption> LanguageOptions => UiLanguageCatalog.All;

    /// <summary>
    /// 界面语言；改动立即写 settings.json 并当场切换文案（DLC 简介在下次加载时按新语言获取）。
    /// 注意顺序：先落盘再切语言，切换动作本身会重发本页所有属性通知。
    /// </summary>
    public UiLanguageOption SelectedUiLanguage
    {
        get => _selectedUiLanguage;
        set => ApplyOption(
            value,
            _selectedUiLanguage,
            Keys.SettingsInterfaceStatusLanguage,
            v =>
            {
                _selectedUiLanguage = v;
                AppServices.Settings.UiLanguage = v.Key;
            },
            // 切语言后 T() 已经读新表，这里写进状态栏的就是新语言的句子。
            afterSave: () => Localizer.Instance.Apply(value.Key),
            statusArgs: v => new object?[] { v.Title });
    }

    /// <summary>可选的播放集页面显示方式。</summary>
    public IReadOnlyList<PlaySetPageDisplayModeOption> PlaySetDisplayModes =>
        PlaySetPageDisplayModeCatalog.All;

    /// <summary>
    /// 播放集页面显示方式；改动立即写 settings.json，播放集页面下次加载时生效。
    /// </summary>
    public PlaySetPageDisplayModeOption SelectedPlaySetDisplayMode
    {
        get => _selectedPlaySetDisplayMode;
        set => ApplyOption(
            value,
            _selectedPlaySetDisplayMode,
            Keys.SettingsInterfaceStatusPlaySet,
            v =>
            {
                _selectedPlaySetDisplayMode = v;
                AppServices.Settings.PlaySetPageDisplayMode = PlaySetPageDisplayModes.Normalize(v.Key);
            },
            statusArgs: v => new object?[] { v.Title });
    }

    /// <summary>可选的整窗背景显示方式。</summary>
    public IReadOnlyList<BackgroundDisplayModeOption> BackgroundDisplayModeOptions =>
        BackgroundDisplayModeCatalog.All;

    /// <summary>
    /// 整窗背景显示方式；改动登记延迟落盘并通知主窗口重画（只是换 Stretch / 对齐，不重新解码大图）。
    /// </summary>
    public BackgroundDisplayModeOption SelectedBackgroundDisplayMode
    {
        get => _selectedBackgroundDisplayMode;
        set => ApplyOption(
            value,
            _selectedBackgroundDisplayMode,
            Keys.SettingsInterfaceStatusBackground,
            v =>
            {
                _selectedBackgroundDisplayMode = v;
                AppServices.Settings.BackgroundDisplayMode = BackgroundDisplayModes.Normalize(v.Key);
            },
            afterSave: AppServices.NotifyBackgroundDisplayChanged,
            statusArgs: v => new object?[] { v.Title });
    }

    /// <summary>可选的 Mod 缩略图尺寸。</summary>
    public IReadOnlyList<ModThumbnailSizeOption> ModThumbnailSizeOptions => ModThumbnailSizeCatalog.All;

    /// <summary>
    /// Mod 缩略图尺寸；改动登记延迟落盘。缩略图尺寸在行视图模型构造时定下，
    /// 所以要等 Mod 库 / 播放集页面重新加载才看得出变化。
    /// </summary>
    public ModThumbnailSizeOption SelectedModThumbnailSize
    {
        get => _selectedModThumbnailSize;
        set => ApplyOption(
            value,
            _selectedModThumbnailSize,
            Keys.SettingsInterfaceStatusThumbnailSize,
            v =>
            {
                _selectedModThumbnailSize = v;
                AppServices.Settings.ModThumbnailSize = ModThumbnailSizes.Normalize(v.Key);
            },
            statusArgs: v => new object?[] { v.Title, v.Pixels });
    }

    /// <summary>
    /// 窗口置顶开关：经 <see cref="AlwaysOnTopService"/> 应用到窗口并立即落盘。
    /// 与标题栏图钉等价；应用失败时通知绑定回头显示旧值。
    /// </summary>
    public bool AlwaysOnTop
    {
        get => AppServices.Settings.AlwaysOnTop;
        set
        {
            if (AppServices.Settings.AlwaysOnTop == value)
            {
                return;
            }

            if (!AlwaysOnTopService.Set(value, out var error))
            {
                SetStatusText(error ?? T(Keys.SettingsInterfaceStatusAlwaysOnTopFailed));
                OnPropertyChanged();
                return;
            }

            OnPropertyChanged();
            SetStatus(value
                ? Keys.SettingsInterfaceStatusAlwaysOnTop
                : Keys.SettingsInterfaceStatusAlwaysOnTopOff);
        }
    }

    /// <summary>标题栏图钉改变置顶后，刷新设置页开关的绑定值。</summary>
    public void RefreshAlwaysOnTop() => OnPropertyChanged(nameof(AlwaysOnTop));

    // ------------------------------------------------------------ 启动区

    /// <summary>实际启动的可执行文件（绝对路径，只读展示）。</summary>
    public string ExePath => Game?.ExePath ?? string.Empty;

    /// <summary>settings 里配置的额外启动参数（空格拼接，只读展示）。</summary>
    public string ExeArgsText
    {
        get
        {
            var args = Game?.ExeArgs;
            return args is null || args.Count == 0
                ? T(Keys.SettingsLaunchArgsNone)
                : string.Join(' ', args);
        }
    }

    /// <summary>可选启动器列表：第一个恒为"标准可执行文件（默认）"，其后是 alternativeExecutables。</summary>
    public ObservableCollection<AlternativeExecutable> Alternatives { get; } = new();

    public AlternativeExecutable? SelectedAlternative
    {
        get => _selectedAlternative;
        set
        {
            if (!SetProperty(ref _selectedAlternative, value))
            {
                return;
            }

            OnPropertyChanged(nameof(SelectedAlternativeText));
            if (_isRelocalizing)
            {
                // 切语言后重建列表会重置选中项，此时不该把状态栏改成「已选择启动器」。
                return;
            }

            if (value is null)
            {
                SetStatusText(string.Empty);
            }
            else
            {
                SetStatus(Keys.SettingsLaunchStatusSelected, value.DisplayLabel, value.ExePath);
            }
        }
    }

    public string SelectedAlternativeText => SelectedAlternative is null
        ? T(Keys.SettingsLaunchNoneSelected)
        : SelectedAlternative.DisplayLabel + " — " + SelectedAlternative.ExePath;

    // ------------------------------------------------------------ 数据区

    /// <summary>游戏数据目录（%USERPROFILE%\Documents\Paradox Interactive\&lt;Game&gt;）。</summary>
    public string GameDataPath => Game?.GameDataDir ?? string.Empty;

    /// <summary>DLC 扫描根目录（通常为安装目录上一级的 game）。</summary>
    public string DlcPath => Game?.DlcPath ?? string.Empty;

    /// <summary>Mod 目录。</summary>
    public string ModDir => Game?.ModDir ?? string.Empty;

    /// <summary>formatVersion：0 / 1 / 1.1（枚举 <see cref="LoadFileFormat"/>）。</summary>
    public string FormatVersionText => Game is null
        ? T(Keys.SettingsGameNone)
        : Game.Format switch
        {
            LoadFileFormat.DlcLoadV0 => T(Keys.SettingsDataFormatV0),
            LoadFileFormat.ContentLoadV1 => T(Keys.SettingsDataFormatV1),
            LoadFileFormat.ContentLoadV1_1 => T(Keys.SettingsDataFormatV11),
            _ => Game.Format.ToString(),
        };

    /// <summary>加载文件绝对路径：gameDataDir 下的 dlc_load.json 或 content_load.json。</summary>
    public string LoadFilePath
    {
        get
        {
            if (Game is null || !AppServices.IsInitialized)
            {
                return string.Empty;
            }

            var fileName = Game.Format == LoadFileFormat.DlcLoadV0
                ? "dlc_load.json"
                : "content_load.json";
            return AppServices.FileSystem.Combine(Game.GameDataDir, fileName);
        }
    }

    /// <summary>加载文件当前是否存在（不存在说明游戏从未启动过或目录被清空）。</summary>
    public string LoadFileStateText
    {
        get
        {
            var path = LoadFilePath;
            if (string.IsNullOrEmpty(path) || !AppServices.IsInitialized)
            {
                return string.Empty;
            }

            return AppServices.FileSystem.FileExists(path)
                ? T(Keys.SettingsDataFileExists)
                : T(Keys.SettingsDataFileMissing);
        }
    }

    /// <summary>DLC 简介缓存保留天数（1 / 3 / 7 / 14 / 30）。</summary>
    public IReadOnlyList<RetentionDaysOption> DlcDescriptionRetentions =>
        RetentionDaysCatalog.DlcDescriptions.All;

    /// <summary>DLC 简介缓存天数；改动登记延迟落盘，下一次查询按新值判断过期。</summary>
    public RetentionDaysOption SelectedDlcDescriptionRetention
    {
        get => _selectedDlcDescriptionRetention;
        set => ApplyOption(
            value,
            _selectedDlcDescriptionRetention,
            Keys.SettingsDataStatusDlcDays,
            v =>
            {
                _selectedDlcDescriptionRetention = v;
                AppServices.Settings.DlcDescriptionCacheDays = v.Days;
            },
            statusArgs: v => new object?[] { v.Days });
    }

    // ------------------------------------------------------------ 补丁

    /// <summary>补丁状态文案（按当前语言渲染；语言切换后由 <see cref="RefreshPatchState"/> 重建）。</summary>
    public string PatchStatusText
    {
        get => _patchStatusText;
        private set => SetProperty(ref _patchStatusText, value);
    }

    /// <summary>补丁状态的技术细节（实际目录、appId、失败原因）；无内容时为空串。</summary>
    public string PatchDetailText
    {
        get => _patchDetailText;
        private set
        {
            if (SetProperty(ref _patchDetailText, value))
            {
                OnPropertyChanged(nameof(HasPatchDetail));
            }
        }
    }

    public bool HasPatchDetail => !string.IsNullOrEmpty(PatchDetailText);

    /// <summary>
    /// 上一次「打补丁」因为版本未验证被暂停，等待用户确认后继续。
    /// 页面据此弹二次确认，再以 <c>allowUnverifiedVersion</c> 重试；Core 不弹窗。
    /// </summary>
    public bool PatchNeedsVersionConfirm { get; private set; }

    /// <summary>没选游戏时整个补丁区只显示提示，按钮都不可用。</summary>
    public bool HasPatchInfo => Game is not null;

    public bool CanApplyPatch => !IsBusy && _patchReport?.CanApply == true;

    public bool CanRemovePatch => !IsBusy && _patchReport?.CanRevert == true;

    /// <summary>按磁盘现状重读补丁状态（切换游戏、后台刷新、语言切换后都会调用）。</summary>
    public void RefreshPatchState()
    {
        OnPropertyChanged(nameof(HasPatchInfo));

        if (Game is null)
        {
            _patchReport = null;
            PatchStatusText = T(Keys.SettingsPatchStateNoGame);
            PatchDetailText = string.Empty;
            RaisePatchProps();
            return;
        }

        GamePatchReport report;
        try
        {
            report = AppServices.GamePatch.Inspect(Game);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("检查补丁状态失败（gameId=" + Game.GameId + "）", ex);
            _patchReport = null;
            PatchStatusText = T(Keys.SettingsPatchStateUnsupported);
            PatchDetailText = ex.Message;
            RaisePatchProps();
            return;
        }

        _patchReport = report;
        PatchStatusText = T(PatchStatusKey(report.Status));
        PatchDetailText = BuildPatchDetail(report);
        RaisePatchProps();
    }

    /// <summary>
    /// 一键打补丁（页面必须先弹 ContentDialog 确认）。
    /// 先备份当前 steam_api 再覆盖 loader，失败只在状态栏说明原因，不动已备份的原文件。
    /// <para>
    /// 目标文件不在已知原版登记表里时，Core 返回 <see cref="GamePatchStatus.UnverifiedVersion"/> 且不写盘：
    /// 这里只记下 <see cref="PatchNeedsVersionConfirm"/>，由页面提示用户确认后再带
    /// <paramref name="allowUnverifiedVersion"/> 调一次。
    /// </para>
    /// </summary>
    public async Task ApplyPatchAsync(CancellationToken cancellationToken, bool allowUnverifiedVersion = false)
    {
        if (Game is null)
        {
            SetStatus(Keys.SettingsStatusSelectGameFirst);
            return;
        }

        IsBusy = true;
        PatchNeedsVersionConfirm = false;
        try
        {
            var game = Game;
            var report = await Task.Run(
                    () => AppServices.GamePatch.Apply(game, allowUnverifiedVersion),
                    cancellationToken)
                .ConfigureAwait(true);

            _patchReport = report;
            PatchStatusText = T(PatchStatusKey(report.Status));
            PatchDetailText = BuildPatchDetail(report);
            RaisePatchProps();

            // DLC 页拿的是 30 秒缓存；补丁文件刚变过，必须让它下一次立刻重探。
            AppServices.DlcOwnership.Invalidate();

            if (report.Status == GamePatchStatus.Patched)
            {
                SetStatus(
                    Keys.SettingsPatchStatusApplied,
                    report.ApiFileName ?? string.Empty,
                    report.SteamAppId?.ToString(CultureInfo.InvariantCulture) ?? "-");
                AppServices.Logger.Info("已打补丁（gameId=" + game.GameId + "，dir=" + report.ApiDirectory + "）");
            }
            else if (report.Status == GamePatchStatus.UnverifiedVersion)
            {
                PatchNeedsVersionConfirm = true;
                SetStatus(Keys.SettingsPatchStatusNeedsConfirm);
                AppServices.Logger.Info("补丁暂停：版本未验证，等待用户确认（gameId=" + game.GameId + "）");
            }
            else
            {
                SetStatus(Keys.SettingsPatchStatusFailed, report.Detail ?? T(PatchStatusKey(report.Status)));
                AppServices.Logger.Warn("打补丁未完成（gameId=" + game.GameId + "）：" + report.Detail);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("打补丁失败（gameId=" + Game.GameId + "）", ex);
            SetStatus(Keys.SettingsPatchStatusFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>移除补丁、把原文件移回原位（页面必须先弹 ContentDialog 确认）。</summary>
    public async Task RemovePatchAsync(CancellationToken cancellationToken)
    {
        if (Game is null)
        {
            SetStatus(Keys.SettingsStatusSelectGameFirst);
            return;
        }

        IsBusy = true;
        try
        {
            var game = Game;
            var report = await Task.Run(() => AppServices.GamePatch.Revert(game), cancellationToken)
                .ConfigureAwait(true);

            _patchReport = report;
            PatchStatusText = T(PatchStatusKey(report.Status));
            PatchDetailText = BuildPatchDetail(report);
            RaisePatchProps();

            // 移除补丁同样改变了探测对象，别让 DLC 页吃旧缓存。
            AppServices.DlcOwnership.Invalidate();

            if (report.Status == GamePatchStatus.Vanilla)
            {
                SetStatus(Keys.SettingsPatchStatusReverted, report.ApiFileName ?? string.Empty);
                AppServices.Logger.Info("已移除补丁（gameId=" + game.GameId + "）");
            }
            else
            {
                SetStatus(Keys.SettingsPatchStatusFailed, report.Detail ?? T(PatchStatusKey(report.Status)));
                AppServices.Logger.Warn("移除补丁未完成（gameId=" + game.GameId + "）：" + report.Detail);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("移除补丁失败（gameId=" + Game.GameId + "）", ex);
            SetStatus(Keys.SettingsPatchStatusFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static string PatchStatusKey(GamePatchStatus status) => status switch
    {
        GamePatchStatus.Unsupported => Keys.SettingsPatchStateUnsupported,
        GamePatchStatus.NotDetected => Keys.SettingsPatchStateNotDetected,
        GamePatchStatus.Vanilla => Keys.SettingsPatchStateVanilla,
        GamePatchStatus.Patched => Keys.SettingsPatchStatePatched,
        GamePatchStatus.PatchedOther => Keys.SettingsPatchStatePatchedOther,
        GamePatchStatus.SteamOverwrote => Keys.SettingsPatchStateSteamOverwrote,
        GamePatchStatus.UnverifiedVersion => Keys.SettingsPatchStateUnverifiedVersion,
        _ => Keys.SettingsPatchStateUnsupported,
    };

    /// <summary>细节行：实际目录 + appId + 版本，失败/特殊情况再加一行原因。</summary>
    private string BuildPatchDetail(GamePatchReport report)
    {
        var parts = new List<string>();
        if (!string.IsNullOrEmpty(report.ApiDirectory))
        {
            parts.Add(T(Keys.SettingsPatchDetailPath, report.ApiDirectory));
        }

        if (report.SteamAppId is { } appId)
        {
            parts.Add(T(Keys.SettingsPatchDetailAppId, appId.ToString(CultureInfo.InvariantCulture)));
        }

        if (report.VersionState == PatchVersionState.Known && !string.IsNullOrEmpty(report.MatchedVersion))
        {
            parts.Add(T(Keys.SettingsPatchDetailVersion, report.MatchedVersion, report.MatchedBuildId ?? "-"));
        }
        else if (report.VersionState == PatchVersionState.Unknown)
        {
            parts.Add(T(Keys.SettingsPatchDetailVersionUnknown));
        }

        parts.Add(T(AppServices.GamePatch.UsesExternalAssets
            ? Keys.SettingsPatchAssetsExternal
            : Keys.SettingsPatchAssetsBuiltIn));

        if (report.BackupSuspect)
        {
            parts.Add(T(Keys.SettingsPatchDetailBackupSuspect, report.BackupArchivePath ?? "-"));
        }

        // 「未验证版本」的 Detail 是给日志看的技术串，界面已经用本地化文案说明，不再重复。
        if (!string.IsNullOrEmpty(report.Detail) && report.Status != GamePatchStatus.UnverifiedVersion)
        {
            parts.Add(report.Detail);
        }

        return string.Join(Environment.NewLine, parts);
    }

    private void RaisePatchProps()
    {
        OnPropertyChanged(nameof(CanApplyPatch));
        OnPropertyChanged(nameof(CanRemovePatch));
    }

    // ------------------------------------------------------------ 危险区

    /// <summary>是否允许写 dlc_signature（默认关闭）。</summary>
    public bool WriteDlcSignature
    {
        get => AppServices.Settings.WriteDlcSignature;
        set => ApplyFlag(
            value,
            AppServices.Settings.WriteDlcSignature,
            v => AppServices.Settings.WriteDlcSignature = v,
            Keys.SettingsDangerStatusAllowed,
            Keys.SettingsDangerStatusForbidden,
            afterSave: () => OnPropertyChanged(nameof(CanRegenerateSignature)));
    }

    /// <summary>开关关闭时禁用"重新生成"按钮。</summary>
    public bool CanRegenerateSignature => AppServices.Settings.WriteDlcSignature;

    /// <summary>最近一次计算出的签名（32 位小写 hex）。</summary>
    public string LastSignature
    {
        get => _lastSignature;
        private set
        {
            if (SetProperty(ref _lastSignature, value))
            {
                OnPropertyChanged(nameof(HasLastSignature));
            }
        }
    }

    public bool HasLastSignature => !string.IsNullOrEmpty(LastSignature);

    // ------------------------------------------------------------ 加载

    /// <summary>
    /// 切换游戏后刷新整页展示（纯本地，不联网）。
    /// 同一款游戏被后台刷新（主题素材回写 / 游戏列表重建）时会带着新的记录实例再次调用：
    /// 这时只重读展示值，不重置 dlc_signature、不覆盖用户选的「其它可执行文件」，
    /// 也不把正在输入的代理地址回滚成 settings.json 里的旧值。
    /// </summary>
    public async Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        // 安装形态与游戏无关，每进程读一次（首次成功后 RefreshInstallModeAsync 直接复用判定结果）；
        // 注册表读取在线程池上，不压 UI 线程。
        await RefreshInstallModeAsync(cancellationToken).ConfigureAwait(true);

        var sameGame = IsSameGame(_game, game);

        RefreshOptionSelections();
        Game = game;
        _theme = game?.Theme;
        if (!sameGame)
        {
            LastSignature = string.Empty;
        }

        Alternatives.Clear();
        if (game is null)
        {
            SelectedAlternative = null;
            IsLoaded = false;
            SetStatus(Keys.SettingsStatusSelectGameFirst);
            RaiseThemeProps();
            RefreshPatchState();
            return;
        }

        _isRelocalizing = true;
        try
        {
            // RebuildAlternatives 内部按 ExePath 保留已选项；换游戏时下面再重置为推荐项。
            RebuildAlternatives(game);
        }
        finally
        {
            _isRelocalizing = false;
        }

        if (!sameGame)
        {
            SelectedAlternative = Alternatives.Count > 0 ? Alternatives[0] : null;
            _proxyUrl = AppServices.Settings.ProxyUrl ?? string.Empty;
            _selectedProxyMode =
                ProxyModeCatalog.Resolve(AppServices.Settings.ProxyMode, AppServices.Settings.ProxyUrl);
            OnPropertyChanged(nameof(ProxyUrl));
            OnPropertyChanged(nameof(SelectedProxyMode));
            OnPropertyChanged(nameof(IsManualProxy));
        }

        RefreshProxyStatus();
        RefreshSteamApiStatus();
        IsLoaded = true;

        RaiseThemeProps();
        RaiseGameProps();

        // 主题素材已由 GameDiscovery 在发现后合并好（缓存层 + 联网刷新），
        // 这里只展示 game.Theme，不再重复解析、也不在页面加载路径上发网络请求。
        _theme = game.Theme;
        RaiseThemeProps();
        RefreshPatchState();
        return;
    }

    /// <summary>
    /// 同一款游戏被后台刷新后的轻量刷新：只换记录引用与主题展示，不动页面上的用户输入。
    /// 返回 true 表示已处理；传入不同游戏或尚未加载过时返回 false，由调用方走完整重载。
    /// </summary>
    public bool RefreshAfterGameRefresh(GameInstallation? game)
    {
        if (game is null || !IsSameGame(_game, game))
        {
            return false;
        }

        Game = game;
        _theme = game.Theme;

        _isRelocalizing = true;
        try
        {
            RebuildAlternatives(game);
        }
        finally
        {
            _isRelocalizing = false;
        }

        RefreshProxyStatus();
        RaiseThemeProps();
        RaiseGameProps();
        RefreshPatchState();
        return true;
    }

    /// <summary>是否同一款游戏：GameId + 安装根目录一致（后台刷新会替换记录实例）。</summary>
    private static bool IsSameGame(GameInstallation? left, GameInstallation? right)
    {
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null)
        {
            return false;
        }

        return string.Equals(left.GameId, right.GameId, StringComparison.Ordinal)
            && string.Equals(left.InstallRoot, right.InstallRoot, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"立即检查"：强制联网刷新一次主题（TTL 从此刻重新计时，主窗口同帧换图）。</summary>
    public async Task RefreshThemeAsync(CancellationToken cancellationToken)
    {
        if (Game is null)
        {
            SetStatus(Keys.SettingsStatusSelectGameFirst);
            return;
        }

        if (!AppServices.Settings.OnlineThemeRefreshEnabled)
        {
            SetStatus(Keys.SettingsThemeStatusOff);
            return;
        }

        IsBusy = true;
        var startedAt = DateTimeOffset.UtcNow;
        try
        {
            // 真正的 force：绕过 24 小时 TTL 与 10 分钟退避，仍拿同一把 Gate。
            // 下载完成后由 ThemeUpdateService 回写 AppServices.Games 并广播，
            // 主窗口背景随之更新；这里只负责把结果反映到设置页文案。
            await ThemeUpdateService
                .RefreshAsync(Game, forceRefresh: true, cancellationToken, onlyThatGame: true)
                .ConfigureAwait(true);

            _theme = AppServices.Games
                .FirstOrDefault(g => string.Equals(g.GameId, Game.GameId, StringComparison.OrdinalIgnoreCase))
                ?.Theme ?? Game.Theme;
            OnPropertyChanged(nameof(ThemeSourceText));
            RaiseThemeProps();

            var sourceKey = ThemeSourceKey;
            var diagnostics = GetThemeDiagnostics();
            if (!diagnostics.MetadataMissing
                && diagnostics.LastErrorUtc is { } failedAt
                && failedAt >= startedAt
                && !string.IsNullOrWhiteSpace(diagnostics.LastError))
            {
                // ResolveAsync 会吞掉刷新异常并写入索引：这里必须把本轮失败如实反馈，
                // 不能让用户看到「检查完成」却还是旧图。
                SetStatus(Keys.SettingsThemeStatusFailed, diagnostics.LastError);
                AppServices.Logger.Warn(
                    "主题检查失败（gameId=" + Game.GameId + "）: " + diagnostics.LastError);
            }
            else
            {
                SetStatus(Keys.SettingsThemeStatusDone, T(sourceKey), NextCheckText);
                AppServices.Logger.Info(
                    "主题检查完成（gameId=" + Game.GameId + "，source=" + T(sourceKey) + "）");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("主题检查失败（gameId=" + Game.GameId + "）", ex);
            SetStatus(Keys.SettingsThemeStatusFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// "重新生成 dlc_signature"（页面必须先弹 ContentDialog 确认）。
    /// 先按磁盘真实 DLC 重算签名并展示，再写回 dlc_signature 文件。
    /// </summary>
    public async Task RegenerateDlcSignatureAsync(CancellationToken cancellationToken)
    {
        if (Game is null)
        {
            SetStatus(Keys.SettingsStatusSelectGameFirst);
            return;
        }

        if (!CanRegenerateSignature)
        {
            SetStatus(Keys.SettingsDangerStatusForbiddenFirst);
            return;
        }

        IsBusy = true;
        try
        {
            var dlcs = await AppServices.Dlcs
                .DiscoverAsync(Game, cancellationToken)
                .ConfigureAwait(true);
            var signature = await AppServices.DlcSignatures
                .ComputeAsync(Game, dlcs, cancellationToken)
                .ConfigureAwait(true);
            LastSignature = signature;

            await AppServices.DlcSignatures
                .WriteAsync(Game, dlcs, cancellationToken)
                .ConfigureAwait(true);

            SetStatus(Keys.SettingsDangerStatusDone, signature, dlcs.Count);
            AppServices.Logger.Info("已重新生成 dlc_signature（gameId=" + Game.GameId + "，signature=" + signature + "）");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("重新生成 dlc_signature 失败（gameId=" + Game.GameId + "）", ex);
            SetStatus(Keys.SettingsDangerStatusFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ------------------------------------------------------------ 缓存卡片

    /// <summary>七类联网缓存各一行（顺序即界面顺序，定义在 <see cref="CacheRowCatalog"/>）。</summary>
    public ObservableCollection<CacheRowViewModel> CacheRows { get; }

    /// <summary>合计占用文案（只统计字节，文件数在每行里）。</summary>
    public string CacheTotalText => T(Keys.SettingsCacheSummary, SizeFormatter.Format(_cacheTotalBytes));

    /// <summary>重新统计七类缓存的占用（只看文件属性，不联网）。</summary>
    public async Task RefreshCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        var rows = CacheRows.ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var usages = await CacheRowCatalog.MeasureAsync(rows, cancellationToken).ConfigureAwait(true);

        long totalBytes = 0;
        for (var index = 0; index < rows.Length; index++)
        {
            rows[index].UpdateUsage(usages[index]);
            totalBytes += usages[index].Bytes;
        }

        _cacheTotalBytes = totalBytes;
        OnPropertyChanged(nameof(CacheTotalText));
    }

    /// <summary>清除一类缓存，然后重算占用并回写状态栏。</summary>
    public async Task ClearCacheAsync(CacheRowViewModel row, CancellationToken cancellationToken)
    {
        if (row.IsBusy)
        {
            return;
        }

        var before = row.Usage;
        row.SetBusy(true);
        try
        {
            await Task.Run(row.Clear, cancellationToken).ConfigureAwait(true);
            await RefreshCacheStatsAsync(cancellationToken).ConfigureAwait(true);
            SetStatus(Keys.SettingsCacheStatusCleared, row.Title, SizeFormatter.Format(before.Bytes));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("清除缓存失败（" + row.Key + "）", ex);
            SetStatus(Keys.SettingsCacheStatusFailed, ex.Message);
        }
        finally
        {
            row.SetBusy(false);
        }
    }

    /// <summary>清除全部七类缓存。</summary>
    public async Task ClearAllCachesAsync(CancellationToken cancellationToken)
    {
        var before = _cacheTotalBytes;
        try
        {
            await CacheRowCatalog.ClearAllAsync(CacheRows, cancellationToken).ConfigureAwait(true);
            await RefreshCacheStatsAsync(cancellationToken).ConfigureAwait(true);
            SetStatus(Keys.SettingsCacheStatusClearedAll, SizeFormatter.Format(before));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("清除全部缓存失败", ex);
            SetStatus(Keys.SettingsCacheStatusFailed, ex.Message);
        }
    }

    /// <summary>清缓存前先量一次占用（页面已统计过就不必重复）。</summary>
    public long CacheTotalBytes => _cacheTotalBytes;

    /// <summary>供页面在 shell 动作（打开数据目录等）后回写状态栏文案。</summary>
    public void SetStatus(string key, params object?[] args)
    {
        _statusKey = key;
        _statusArgs = args;
        StatusMessage = T(key, args);
    }

    /// <summary>回写一段外部拼好的文案（例如异常消息）；语言切换后不会重译。</summary>
    public void SetStatusText(string message)
    {
        _statusKey = null;
        _statusArgs = Array.Empty<object?>();
        StatusMessage = message;
    }

    /// <summary>
    /// 代理设置改完后落盘并重建联网服务，让新代理立刻生效。
    /// 返回是否成功；失败原因写进状态栏（不抛异常，输入框失焦不该崩页面）。
    /// </summary>
    public bool ApplyProxySettings()
    {
        AppServices.Settings.ProxyMode = _selectedProxyMode.Key;
        var saved = AppServices.SaveSettings();
        if (!saved.IsSuccess)
        {
            SetStatus(Keys.SettingsStatusSaveFailed, saved.ErrorMessage ?? string.Empty);
            return false;
        }

        var rebuild = AppServices.TryRebuildNetworkServices();
        if (!rebuild.Success)
        {
            if (rebuild.NotInitialized)
            {
                SetStatus(Keys.SettingsProxyStatusNotInitialized);
            }
            else
            {
                SetStatus(Keys.SettingsProxyStatusRebuildFailed, rebuild.Detail ?? string.Empty);
            }

            RefreshProxyStatus();
            RaiseThemeProps();
            return false;
        }

        RefreshProxyStatus();
        SetStatus(Keys.SettingsProxyStatusApplied, ProxyStatusText);
        AppServices.Logger.Info("代理设置已写入并生效：" + ProxyStatusText);
        RaiseThemeProps();
        return true;
    }

    // ------------------------------------------------------------ 数据目录 / 联网图片

    /// <summary>本启动器数据根目录（settings.json / 日志 / 各缓存都在这里）。</summary>
    public string DataDirectoryPath => AppServices.DataRoot;

    /// <summary>旧版本的 C 盘数据目录；迁移完成后可以手动清理。</summary>
    public string LegacyDataDirectoryPath => DataRootMigrator.LegacyRoot;

    /// <summary>联网补全 Mod 缩略图开关；写入 AppSettings.OnlineModThumbnailEnabled 并登记延迟落盘。</summary>
    public bool ModThumbnailOnlineEnabled
    {
        get => AppServices.Settings.OnlineModThumbnailEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.OnlineModThumbnailEnabled,
            v => AppServices.Settings.OnlineModThumbnailEnabled = v,
            Keys.SettingsModThumbnailsStatusOnlineOn,
            Keys.SettingsModThumbnailsStatusOnlineOff);
    }

    /// <summary>可选的 Mod 缩略图保留天数。</summary>
    public IReadOnlyList<RetentionDaysOption> ModThumbnailRetentions =>
        RetentionDaysCatalog.ModThumbnails.All;

    /// <summary>Mod 缩略图缓存保留天数；改动登记延迟落盘，下次加载 Mod 库时按新值清理。</summary>
    public RetentionDaysOption SelectedModThumbnailRetention
    {
        get => _selectedModThumbnailRetention;
        set => ApplyOption(
            value,
            _selectedModThumbnailRetention,
            Keys.SettingsModThumbnailsStatusRetention,
            v =>
            {
                _selectedModThumbnailRetention = v;
                AppServices.Settings.ModThumbnailCacheDays = v.Days;
            },
            statusArgs: v => new object?[] { v.Days });
    }

    /// <summary>Mod 缩略图缓存目录。</summary>
    public string ModThumbnailCachePath => AppServices.ModThumbnailCacheRoot;

    /// <summary>缓存目录里现有的缩略图张数文案。</summary>
    public string ModThumbnailCacheSummary =>
        T(Keys.SettingsModThumbnailsCount, AppServices.ModThumbnails.Cache.Count);

    // ------------------------------------------------------------ 翻译区

    /// <summary>翻译总开关；关闭后不再发起任何翻译请求，页面上的译文会保留到下次刷新。</summary>
    public bool TranslateEnabled
    {
        get => AppServices.Settings.TranslateEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.TranslateEnabled,
            v => AppServices.Settings.TranslateEnabled = v,
            Keys.SettingsTranslateStatusEnabled,
            Keys.SettingsTranslateStatusDisabled,
            afterSave: NotifyTranslationSettingsChanged);
    }

    /// <summary>
    /// 本地翻译模型是否常驻内存；关掉后空闲 5 分钟自动卸载，把约 1.3 GB 还给系统。
    /// </summary>
    public bool KeepTranslationModelLoaded
    {
        get => AppServices.Settings.KeepTranslationModelLoaded;
        set => ApplyFlag(
            value,
            AppServices.Settings.KeepTranslationModelLoaded,
            v => AppServices.Settings.KeepTranslationModelLoaded = v,
            Keys.SettingsTranslateStatusKeepLoadedOn,
            Keys.SettingsTranslateStatusKeepLoadedOff,
            afterSave: NotifyTranslationSettingsChanged);
    }

    /// <summary>可选的翻译引擎模式（自动 / 仅自填接口 / 仅内置免费服务）。</summary>
    public IReadOnlyList<TranslationEngineOption> TranslationEngineOptions =>        TranslationEngineCatalog.All;

    /// <summary>翻译引擎模式；改动立即写 settings.json，并作废在途的翻译请求。</summary>
    public TranslationEngineOption SelectedTranslationEngine
    {
        get => _selectedTranslationEngine;
        set => ApplyOption(
            value,
            _selectedTranslationEngine,
            Keys.SettingsTranslateStatusEngine,
            v =>
            {
                _selectedTranslationEngine = v;
                AppServices.Settings.TranslateEngineMode = v.Key;
                RaiseTranslationEngineFieldVisibility();
            },
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: v => new object?[] { v.Title });
    }

    /// <summary>是否显示自填 OpenAI 兼容接口的三项（地址 / 密钥 / 模型）。</summary>
    public bool HasOpenAiFields => IsEngineMode(TranslationEngineModes.Auto) || IsEngineMode(TranslationEngineModes.Custom);

    /// <summary>是否显示微软翻译的密钥与区域两项。</summary>
    public bool HasMicrosoftFields => IsEngineMode(TranslationEngineModes.Auto) || IsEngineMode(TranslationEngineModes.Microsoft);

    /// <summary>是否显示谷歌云翻译的 API Key 一项。</summary>
    public bool HasGoogleCloudFields => IsEngineMode(TranslationEngineModes.Auto) || IsEngineMode(TranslationEngineModes.GoogleCloud);

    /// <summary>可选的译文语言（跟随界面语言 / 简体中文 / English）。</summary>
    public IReadOnlyList<TranslationTargetLanguageOption> TranslationLanguageOptions =>
        TranslationTargetLanguageCatalog.All;

    /// <summary>译文语言；留空表示跟随界面语言。</summary>
    public TranslationTargetLanguageOption SelectedTranslationLanguage
    {
        get => _selectedTranslationLanguage;
        set => ApplyOption(
            value,
            _selectedTranslationLanguage,
            Keys.SettingsTranslateStatusLanguage,
            v =>
            {
                _selectedTranslationLanguage = v;
                AppServices.Settings.TranslateTargetLanguage = v.Key;
            },
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: v => new object?[] { v.Title });
    }

    /// <summary>自填的 OpenAI 兼容接口地址；只保存在本机 settings.json。</summary>
    public string TranslateBaseUrl
    {
        get => _translateBaseUrl;
        set
        {
            if (SetProperty(ref _translateBaseUrl, value))
            {
                AppServices.Settings.TranslateBaseUrl = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>自填的接口密钥；只保存在本机 settings.json，日志里不打印。</summary>
    public string TranslateApiKey
    {
        get => _translateApiKey;
        set
        {
            if (SetProperty(ref _translateApiKey, value))
            {
                AppServices.Settings.TranslateApiKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>当前引擎模式是否与给定模式一致（未知值按「自动」处理）。</summary>
    private bool IsEngineMode(string mode) =>
        string.Equals(
            TranslationEngineModes.Normalize(AppServices.Settings.TranslateEngineMode),
            mode,
            StringComparison.Ordinal);

    /// <summary>翻译引擎模式变化后，刷新各凭据输入区的可见性。</summary>
    private void RaiseTranslationEngineFieldVisibility()
    {
        OnPropertyChanged(nameof(HasOpenAiFields));
        OnPropertyChanged(nameof(HasMicrosoftFields));
        OnPropertyChanged(nameof(HasGoogleCloudFields));
    }

    /// <summary>自填接口使用的模型名称；留空时按默认模型处理。</summary>
    public string TranslateModel
    {
        get => _translateModel;
        set
        {
            if (SetProperty(ref _translateModel, value))
            {
                AppServices.Settings.TranslateModel = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>微软翻译的订阅密钥；只保存在本机 settings.json，日志里不打印。</summary>
    public string TranslateMicrosoftKey
    {
        get => _translateMicrosoftKey;
        set
        {
            if (SetProperty(ref _translateMicrosoftKey, value))
            {
                AppServices.Settings.TranslateMicrosoftKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>微软翻译资源所在区域（例如 eastasia / global）。</summary>
    public string TranslateMicrosoftRegion
    {
        get => _translateMicrosoftRegion;
        set
        {
            if (SetProperty(ref _translateMicrosoftRegion, value))
            {
                AppServices.Settings.TranslateMicrosoftRegion = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>谷歌云翻译的 API Key；只保存在本机 settings.json，日志里不打印。</summary>
    public string TranslateGoogleKey
    {
        get => _translateGoogleKey;
        set
        {
            if (SetProperty(ref _translateGoogleKey, value))
            {
                AppServices.Settings.TranslateGoogleKey = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                SaveTranslationSettings();
            }
        }
    }

    /// <summary>译文缓存保留天数（1–365 天）。</summary>
    public string TranslateCacheDaysText
    {
        get => AppServices.Settings.TranslateCacheDays.ToString(CultureInfo.InvariantCulture);
        set
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days))
            {
                AppServices.Settings.TranslateCacheDays = days;
                SaveTranslationSettings();
                SetStatus(Keys.SettingsTranslateStatusCacheDays, AppServices.Settings.TranslateCacheDays);
            }
        }
    }

    /// <summary>是否翻译 Mod 与工坊的名称。</summary>
    public bool TranslateNamesEnabled
    {
        get => AppServices.Settings.TranslateNamesEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.TranslateNamesEnabled,
            v => AppServices.Settings.TranslateNamesEnabled = v,
            Keys.SettingsTranslateStatusScope,
            Keys.SettingsTranslateStatusScope,
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: new object?[] { T(Keys.SettingsTranslateScopeNames) });
    }

    /// <summary>是否翻译简介。</summary>
    public bool TranslateDescriptionEnabled
    {
        get => AppServices.Settings.TranslateDescriptionEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.TranslateDescriptionEnabled,
            v => AppServices.Settings.TranslateDescriptionEnabled = v,
            Keys.SettingsTranslateStatusScope,
            Keys.SettingsTranslateStatusScope,
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: new object?[] { T(Keys.SettingsTranslateScopeDescription) });
    }

    /// <summary>是否翻译更新日志。</summary>
    public bool TranslateChangelogEnabled
    {
        get => AppServices.Settings.TranslateChangelogEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.TranslateChangelogEnabled,
            v => AppServices.Settings.TranslateChangelogEnabled = v,
            Keys.SettingsTranslateStatusScope,
            Keys.SettingsTranslateStatusScope,
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: new object?[] { T(Keys.SettingsTranslateScopeChangelog) });
    }

    /// <summary>是否翻译评论区正文。</summary>
    public bool TranslateCommentsEnabled
    {
        get => AppServices.Settings.TranslateCommentsEnabled;
        set => ApplyFlag(
            value,
            AppServices.Settings.TranslateCommentsEnabled,
            v => AppServices.Settings.TranslateCommentsEnabled = v,
            Keys.SettingsTranslateStatusScope,
            Keys.SettingsTranslateStatusScope,
            afterSave: NotifyTranslationSettingsChanged,
            statusArgs: new object?[] { T(Keys.SettingsTranslateScopeComments) });
    }

    /// <summary>「测试翻译」是否正在进行（按钮据此禁用，避免连点）。</summary>
    public bool IsTranslateTesting
    {
        get => _isTranslateTesting;
        private set
        {
            if (SetProperty(ref _isTranslateTesting, value))
            {
                OnPropertyChanged(nameof(CanTestTranslation));
            }
        }
    }

    /// <summary>「测试翻译」按钮当前是否可点。</summary>
    public bool CanTestTranslation => !_isTranslateTesting;

    /// <summary>
    /// 各引擎的探测结果，一行一个：「✓ 引擎名：译文」或「✗ 引擎名：原因」。
    /// </summary>
    public string TranslateTestText
    {
        get => _translateTestText;
        private set => SetProperty(ref _translateTestText, value);
    }

    // ------------------------------------------------------ 本地离线翻译

    /// <summary>
    /// 本地离线模型清单：每个模型一行，可下载 / 删除。
    /// <para>
    /// 没有本地运行时（理论上不会出现）时为空列表，设置页就看不到这一块。
    /// </para>
    /// </summary>
    public IReadOnlyList<LocalModelRowViewModel> LocalModelRows => _localModelRows;

    /// <summary>术语词典的词条列表（可逐条编辑，保存后写盘）。</summary>
    public IReadOnlyList<GlossaryEntryViewModel> GlossaryEntries => _glossaryEntries;

    /// <summary>新增词条的「原文」输入框内容。</summary>
    public string GlossarySourceInput
    {
        get => _glossarySourceInput;
        set
        {
            if (SetProperty(ref _glossarySourceInput, value))
            {
                OnPropertyChanged(nameof(CanAddGlossaryEntry));
            }
        }
    }

    /// <summary>新增词条的「译文」输入框内容。</summary>
    public string GlossaryTargetInput
    {
        get => _glossaryTargetInput;
        set
        {
            if (SetProperty(ref _glossaryTargetInput, value))
            {
                OnPropertyChanged(nameof(CanAddGlossaryEntry));
            }
        }
    }

    /// <summary>「添加词条」按钮当前是否可点：两边都填了内容。</summary>
    public bool CanAddGlossaryEntry =>
        !string.IsNullOrWhiteSpace(_glossarySourceInput) && !string.IsNullOrWhiteSpace(_glossaryTargetInput);

    /// <summary>模型当前占用的磁盘总量文案。</summary>
    public string LocalModelSizeText
    {
        get
        {
            var runtime = AppServices.LocalTranslation;
            if (runtime is null)
            {
                return string.Empty;
            }

            long total = 0;
            foreach (var row in _localModelRows)
            {
                total += runtime.Store.GetInstalledSize(row.Kind);
            }

            return total <= 0 ? string.Empty : SizeFormatter.Format(total);
        }
    }

    /// <summary>
    /// 操作结果文案在行内停留的时长；到点自动清空。
    /// <para>
    /// 用户停在这一页不动时，旧的「已删除」/「校验通过」不该一直挂着——
    /// 它只说明刚才那一下发生了什么，不代表当前状态。
    /// </para>
    /// </summary>
    private static readonly TimeSpan LocalModelOperationLifetime = TimeSpan.FromSeconds(8);

    /// <summary>从 Core 的错误文本里挑出绝对路径，上屏前换成相对路径或文件名。</summary>
    private static readonly Regex AbsolutePathPattern = new(
        @"(?:[A-Za-z]:\\|\\\\)[^\s""'“”\r\n；;，,]*",
        RegexOptions.Compiled);

    /// <summary>
    /// 下载某个本地模型；下载中该行显示进度，完成后重新读 Core 的安装状态。
    /// </summary>
    /// <param name="row">模型行。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task DownloadLocalModelAsync(LocalModelRowViewModel row, CancellationToken cancellationToken)
    {
        var runtime = AppServices.LocalTranslation;
        if (runtime is null || row is null || row.IsBusy)
        {
            return;
        }

        row.IsBusy = true;
        row.SetOperation(T(Keys.SettingsTranslateLocalModelDownloading, 0));
        try
        {
            var progress = new Progress<double>(value => row.SetOperation(
                T(Keys.SettingsTranslateLocalModelDownloading, (int)Math.Round(value * 100))));
            var result = await runtime.DownloadAsync(row.Kind, progress, cancellationToken).ConfigureAwait(true);

            // 不再手写本行的安装布尔：以 Core 的重新探测为准，
            // 这样「下完了但 config.txt 没写成」也会如实显示为 ConfigMissing。
            ApplyLocalModelState(row, runtime);
            if (result.Success)
            {
                ShowLocalModelOperation(
                    row,
                    T(Keys.SettingsTranslateLocalModelStatusDownloaded, row.Title),
                    cancellationToken);
            }
            else
            {
                ShowLocalModelOperation(
                    row,
                    T(
                        Keys.SettingsTranslateLocalModelStatusFailed,
                        ToDisplayText(result.Message, row.Directory)),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            row.ClearOperation();
            ApplyLocalModelState(row, runtime);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("下载本地翻译模型失败", ex);
            ApplyLocalModelState(row, runtime);
            ShowLocalModelOperation(
                row,
                T(
                    Keys.SettingsTranslateLocalModelStatusFailed,
                    ToDisplayText(ex.Message, row.Directory)),
                cancellationToken);
        }
        finally
        {
            row.IsBusy = false;
            OnPropertyChanged(nameof(LocalModelSizeText));
        }
    }

    /// <summary>
    /// 删除某个本地模型：先确认没有在途翻译（Core 的 <c>Remove</c> 契约要求调用方保证），
    /// 再在后台线程删除；期间该行按钮禁用，删完按 <c>Remove</c> 的结果三态给出可自证的文案，
    /// 并重新读安装状态。
    /// </summary>
    /// <param name="row">模型行。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task RemoveLocalModelAsync(LocalModelRowViewModel row, CancellationToken cancellationToken)
    {
        var runtime = AppServices.LocalTranslation;
        if (runtime is null || row is null || row.IsBusy)
        {
            return;
        }

        // Core 的 Remove 契约要求调用方保证此刻没有在途翻译：本地引擎可能还在翻译线程里
        // 握着模型句柄，这时 ReleaseEngines 会拆掉正在使用的引擎。协调器的活动计数是现成的
        // 「此刻有没有翻译在跑」，在途就不删，等这一批结束再让用户重试。
        if (AppServices.IsInitialized && AppServices.Translation.Activity.IsActive)
        {
            ShowLocalModelOperation(
                row,
                T(Keys.SettingsTranslateLocalModelRemoveTranslationBusy),
                cancellationToken);
            return;
        }

        row.IsBusy = true;
        row.ClearOperation();
        try
        {
            // 删除会逐个删文件并复核目录；1.3 GB 的模型不该卡在 UI 线程上。
            // ReleaseEngines 放在后台线程里调，删除前先释放模型文件句柄。
            var result = await Task.Run(
                    () => runtime.Store.Remove(row.Kind, () => runtime.ReleaseEngines()),
                    cancellationToken)
                .ConfigureAwait(true);

            ApplyLocalModelState(row, runtime);
            ShowLocalModelOperation(row, DescribeRemoveResult(result), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            ApplyLocalModelState(row, runtime);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("删除本地翻译模型失败", ex);
            ApplyLocalModelState(row, runtime);
            ShowLocalModelOperation(
                row,
                T(Keys.SettingsTranslateLocalModelRemoveFailed, ToDisplayText(ex.Message, row.Directory)),
                cancellationToken);
        }
        finally
        {
            row.IsBusy = false;
            OnPropertyChanged(nameof(LocalModelSizeText));
        }
    }

    /// <summary>
    /// 深度校验某个本地模型（SHA-256），后台执行；结果说明通过或哪些文件不符。
    /// </summary>
    /// <param name="row">模型行。</param>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task VerifyLocalModelAsync(LocalModelRowViewModel row, CancellationToken cancellationToken)
    {
        var runtime = AppServices.LocalTranslation;
        if (runtime is null || row is null || row.IsBusy || !row.CanVerify)
        {
            return;
        }

        row.IsBusy = true;
        row.SetOperation(T(Keys.SettingsTranslateLocalModelStatusVerifying));
        try
        {
            // Index-Translate 的 gguf 有 1.3 GB，哈希必须离开 UI 线程慢慢算。
            var result = await Task.Run(
                    () => runtime.Store.VerifyInstalled(row.Kind, deep: true),
                    cancellationToken)
                .ConfigureAwait(true);

            ApplyLocalModelState(row, runtime);
            if (result.IsValid)
            {
                var hashedFiles = runtime.Catalog.Get(row.Kind)?.Files
                    .Count(file => !string.IsNullOrEmpty(file.Sha256)) ?? 0;
                ShowLocalModelOperation(
                    row,
                    T(Keys.SettingsTranslateLocalModelStatusVerifyPassed, hashedFiles),
                    cancellationToken);
            }
            else
            {
                ShowLocalModelOperation(
                    row,
                    T(
                        Keys.SettingsTranslateLocalModelStatusVerifyFailed,
                        DescribeVerifyFailure(result)),
                    cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            row.ClearOperation();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("校验本地翻译模型失败", ex);
            ShowLocalModelOperation(
                row,
                T(
                    Keys.SettingsTranslateLocalModelStatusVerifyFailed,
                    ToDisplayText(ex.Message, row.Directory)),
                cancellationToken);
        }
        finally
        {
            row.IsBusy = false;
        }
    }

    /// <summary>把 Core 的删除结果翻译成能自证的行内文案。</summary>
    private static string DescribeRemoveResult(LocalTranslationModelRemoveResult result)
    {
        switch (result.Status)
        {
            case LocalTranslationModelRemoveStatus.Removed:
                // 只有 Core 复核过「目录确实不存在」才会报 Removed，文案要把这一点说出来。
                return T(Keys.SettingsTranslateLocalModelStatusRemovedVerified);

            case LocalTranslationModelRemoveStatus.NotFound:
                return T(Keys.SettingsTranslateLocalModelStatusRemoveNotFound);

            case LocalTranslationModelRemoveStatus.RemovedWithResidue:
                if (result.Residue.Count == 1 && result.Residue[0] == ".")
                {
                    return T(Keys.SettingsTranslateLocalModelStatusResidueDirectoryOnly);
                }

                // 残留清单只说明「还剩什么」，不说明「为什么删不掉」：把 Core 记下的原因
                // 也同样脱敏后接在清单后面，用户才知道该去关哪个进程。
                var residueText = FormatResidue(result.Residue);
                var reason = ToDisplayText(result.Error, result.Directory);
                return reason.Length == 0
                    ? T(Keys.SettingsTranslateLocalModelStatusRemovedWithResidue, residueText)
                    : T(
                        Keys.SettingsTranslateLocalModelStatusResidueReason,
                        residueText,
                        reason);

            default:
                return T(
                    Keys.SettingsTranslateLocalModelRemoveFailed,
                    ToDisplayText(result.Error, result.Directory));
        }
    }

    private static string DescribeVerifyFailure(LocalTranslationModelVerifyResult result)
    {
        var parts = new List<string>(4);
        var state = result.State;
        if (state.MissingFiles.Count > 0)
        {
            parts.Add(T(Keys.SettingsTranslateLocalModelStatusVerifyMissing, state.MissingFiles.Count));
        }

        if (state.SizeMismatches.Count > 0)
        {
            parts.Add(T(Keys.SettingsTranslateLocalModelStatusVerifySize, state.SizeMismatches.Count));
        }

        if (state.Status == LocalTranslationModelInstallStatus.ConfigMissing)
        {
            parts.Add(T(Keys.SettingsTranslateLocalModelStatusVerifyConfigMissing));
        }

        if (result.HashMismatches.Count > 0)
        {
            parts.Add(T(
                Keys.SettingsTranslateLocalModelStatusVerifyHashMismatch,
                string.Join(", ", result.HashMismatches.Select(mismatch => mismatch.FileName))));
        }

        return parts.Count == 0
            ? T(Keys.SettingsTranslateLocalModelNotInstalled)
            : string.Join("; ", parts);
    }

    /// <summary>
    /// 残留清单最多列 6 项，其余用「总数」收尾（不是「另有 N 项」），避免用户把 6 项当成全部。
    /// </summary>
    private static string FormatResidue(IReadOnlyList<string> residue)
    {
        const int MaxItems = 6;
        var shown = string.Join(", ", residue.Take(MaxItems));
        return residue.Count > MaxItems
            ? shown + T(Keys.SettingsTranslateLocalModelResidueMore, residue.Count)
            : shown;
    }

    /// <summary>把 Core 的安装状态应用到行上（状态、目录是否存在、按钮可用性）。</summary>
    private static void ApplyLocalModelState(LocalModelRowViewModel row, LocalTranslationRuntime runtime)
    {
        var state = runtime.Store.GetInstallState(row.Kind);
        var directoryExists = AppServices.IsInitialized
            && AppServices.FileSystem.DirectoryExists(state.Directory);
        row.ApplyState(state, directoryExists);
    }

    /// <summary>写一条会过期的操作文案，并安排到点清除。</summary>
    private void ShowLocalModelOperation(
        LocalModelRowViewModel row,
        string text,
        CancellationToken cancellationToken)
    {
        var expiresAt = DateTimeOffset.Now + LocalModelOperationLifetime;
        row.SetOperation(text, expiresAt);
        _ = ClearLocalModelOperationLaterAsync(row, expiresAt, cancellationToken);
    }

    private static async Task ClearLocalModelOperationLaterAsync(
        LocalModelRowViewModel row,
        DateTimeOffset expiresAt,
        CancellationToken cancellationToken)
    {
        var delay = expiresAt - DateTimeOffset.Now;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                // 页面关闭时令牌取消，不留下后台任务。
                await Task.Delay(delay, cancellationToken).ConfigureAwait(true);
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }

        row.ExpireOperation(DateTimeOffset.Now);
    }

    /// <summary>
    /// 把 Core 错误文本里的绝对路径换成相对路径或文件名；日志里仍保留完整原文。
    /// </summary>
    private static string ToDisplayText(string? text, string? directory)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var value = text;
        if (!string.IsNullOrEmpty(directory))
        {
            // 先替换已知的模型目录，保留其下的相对层级（如 index\model.bin）。
            var replacement = Path.GetFileName(directory.TrimEnd('\\', '/'));
            value = value.Replace(
                directory,
                string.IsNullOrEmpty(replacement) ? "." : replacement,
                StringComparison.OrdinalIgnoreCase);
        }

        return AbsolutePathPattern.Replace(value, match =>
        {
            var raw = match.Value.TrimEnd('.', '。', ' ', ':', '：', '\\', '/', '"', '\'');
            var name = Path.GetFileName(raw);
            return string.IsNullOrWhiteSpace(name) ? "." : name;
        });
    }

    /// <summary>把新增词条追加到列表（不写盘，等用户点「保存词典」）。</summary>
    public void AddGlossaryEntry()
    {
        var source = _glossarySourceInput.Trim();
        var target = _glossaryTargetInput.Trim();
        if (source.Length == 0 || target.Length == 0)
        {
            return;
        }

        var existing = _glossaryEntries.FirstOrDefault(entry =>
            string.Equals(entry.Source, source, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Target = target;
        }
        else
        {
            _glossaryEntries.Add(new GlossaryEntryViewModel(source, target));
        }

        GlossarySourceInput = string.Empty;
        GlossaryTargetInput = string.Empty;
    }

    /// <summary>从列表里移除一条词条（不写盘，等用户点「保存词典」）。</summary>
    /// <param name="entry">词条。</param>
    public void RemoveGlossaryEntry(GlossaryEntryViewModel entry)
    {
        if (entry is null)
        {
            return;
        }

        _glossaryEntries.Remove(entry);
    }

    /// <summary>把当前词条列表写盘。</summary>
    public void SaveGlossary()
    {
        var runtime = AppServices.LocalTranslation;
        if (runtime is null)
        {
            return;
        }

        try
        {
            var entries = _glossaryEntries.Select(entry => entry.ToEntry()).ToList();
            runtime.Glossary.Save(entries);
            SetStatus(Keys.SettingsTranslateGlossaryStatusSaved, entries.Count);
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("保存术语词典失败", ex);
            SetStatus(Keys.SettingsTranslateGlossaryStatusFailed, ex.Message);
        }
    }

    /// <summary>重新读取本地模型与术语词典（进入设置页、下完模型后调用）。</summary>
    public void RefreshLocalTranslationState()
    {
        RefreshLocalModelRows();

        _glossaryEntries.Clear();
        var runtime = AppServices.LocalTranslation;
        if (runtime is not null)
        {
            foreach (var entry in runtime.Glossary.Entries)
            {
                _glossaryEntries.Add(new GlossaryEntryViewModel(entry.Source, entry.Target));
            }
        }

        OnPropertyChanged(nameof(GlossaryEntries));
    }

    /// <summary>
    /// 重新读取本地模型清单（安装状态、状态文案）。
    /// <para>
    /// 只重建模型行，不动术语词典：词典里的词条是用户正在编辑的数据，
    /// 切语言时不该被丢掉。
    /// </para>
    /// </summary>
    private void RefreshLocalModelRows()
    {
        var runtime = AppServices.LocalTranslation;
        _localModelRows.Clear();
        if (runtime is null)
        {
            OnPropertyChanged(nameof(LocalModelRows));
            OnPropertyChanged(nameof(LocalModelSizeText));
            return;
        }

        foreach (var model in runtime.Catalog.Models)
        {
            var titleKey = model.Kind switch
            {
                LocalTranslationModelKind.IndexTranslate => Keys.SettingsTranslateLocalModelIndexTranslate,
                LocalTranslationModelKind.BergamotEnZh => Keys.SettingsTranslateLocalModelBergamotEnZh,
                LocalTranslationModelKind.BergamotZhEn => Keys.SettingsTranslateLocalModelBergamotZhEn,
                _ => Keys.SettingsTranslateLocalModelIndexTranslate,
            };

            // 状态全部来自 Core 的 GetInstallState：Installed / ConfigMissing / 缺文件 /
            // 尺寸不符 / 未安装五态各有各的文案，不再用单个 IsInstalled 布尔糊过去。
            var row = new LocalModelRowViewModel(model.Kind, titleKey, model.DisplayName);
            ApplyLocalModelState(row, runtime);
            _localModelRows.Add(row);
        }

        OnPropertyChanged(nameof(LocalModelRows));
        OnPropertyChanged(nameof(LocalModelSizeText));
    }

    /// <summary>
    /// 逐个引擎各翻一句探针文本，把结果写进翻译区。
    /// <para>
    /// 这是给「开了翻译却什么都没变」准备的诊断入口：它绕开缓存、也不看当前模式，
    /// 直接告诉用户哪条链路通、哪条不通，不用去翻日志。
    /// </para>
    /// </summary>
    /// <param name="cancellationToken">页面生命周期取消令牌。</param>
    public async Task TestTranslationAsync(CancellationToken cancellationToken)
    {
        if (_isTranslateTesting)
        {
            return;
        }

        IsTranslateTesting = true;
        TranslateTestText = T(Keys.SettingsTranslateTestRunning);
        try
        {
            var results = await AppServices.Translation.ProbeAsync(cancellationToken).ConfigureAwait(true);
            _translateTestResults = results;
            TranslateTestText = FormatProbeResults(results);
        }
        catch (OperationCanceledException)
        {
            TranslateTestText = string.Empty;
        }
        finally
        {
            IsTranslateTesting = false;
        }
    }

    /// <summary>把探测结果拼成多行文案（✓/✗ 是符号，不随语言变）。</summary>
    private string FormatProbeResults(IReadOnlyList<TranslationProbeResult> results)
    {
        if (results.Count == 0)
        {
            return string.Empty;
        }

        var lines = new List<string>(results.Count);
        foreach (var result in results)
        {
            var title = TranslationEngineCatalog.Resolve(result.Key).Title;
            lines.Add(T(
                Keys.SettingsTranslateTestResult,
                (result.Ok ? "✓ " : "✗ ") + title,
                result.Detail));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>翻译相关设置变化后作废在途请求，并让下一次翻译用新配置。</summary>
    private void NotifyTranslationSettingsChanged()
    {
        AppServices.Translation.NotifySettingsChanged();
    }    /// <summary>文本框类翻译设置落盘：延迟写盘 + 作废在途请求。</summary>
    private void SaveTranslationSettings()
    {
        PersistSettings();
        NotifyTranslationSettingsChanged();
    }

    /// <summary>翻译区的属性通知（语言切换后重建文案）。</summary>
    private void RaiseTranslationProps()
    {
        OnPropertyChanged(nameof(TranslateEnabled));
        OnPropertyChanged(nameof(TranslationEngineOptions));
        OnPropertyChanged(nameof(SelectedTranslationEngine));
        OnPropertyChanged(nameof(TranslationLanguageOptions));
        OnPropertyChanged(nameof(SelectedTranslationLanguage));
        OnPropertyChanged(nameof(TranslateBaseUrl));
        OnPropertyChanged(nameof(TranslateApiKey));
        OnPropertyChanged(nameof(TranslateModel));
        OnPropertyChanged(nameof(TranslateMicrosoftKey));
        OnPropertyChanged(nameof(TranslateMicrosoftRegion));
        OnPropertyChanged(nameof(TranslateGoogleKey));
        OnPropertyChanged(nameof(HasOpenAiFields));
        OnPropertyChanged(nameof(HasMicrosoftFields));
        OnPropertyChanged(nameof(HasGoogleCloudFields));
        OnPropertyChanged(nameof(TranslateCacheDaysText));
        OnPropertyChanged(nameof(TranslateNamesEnabled));
        OnPropertyChanged(nameof(TranslateDescriptionEnabled));
        OnPropertyChanged(nameof(TranslateChangelogEnabled));
        OnPropertyChanged(nameof(TranslateCommentsEnabled));
        OnPropertyChanged(nameof(IsTranslateTesting));
        OnPropertyChanged(nameof(CanTestTranslation));
        OnPropertyChanged(nameof(TranslateTestText));
    }

    // ------------------------------------------------------------ 内部

    /// <summary>语言切换后要把拼好的句子按新语言重建。</summary>
    protected override void Relocalize()
    {
        RefreshProxyStatus();

        // 自检结果是拼好的句子，切语言后按新语言重建（原始结果留着，不重测）。
        RebuildProbeRows();
        OnPropertyChanged(nameof(CacheTotalText));
        RefreshAlternativeLabels();
        RefreshPatchState();
        RaiseTranslationProps();

        // 本地模型行自己会在语言切换时重发文案（基类按弱登记逐个通知），
        // 这里不再重建行——重建会把在途的下载/校验状态和刚弹的操作结果一起丢掉。
        // 术语词典是用户数据，同样不动。

        // 探测结果是拼好的句子，语言切换后要按新语言重建。
        if (_translateTestResults is { Count: > 0 })
        {
            TranslateTestText = FormatProbeResults(_translateTestResults);
        }

        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }
    }

    private DateTimeOffset? GetLastCheckUtc() => GetThemeDiagnostics().LastCheckedUtc;

    private ThemeCacheDiagnostics GetThemeDiagnostics() =>
        Game is null
            ? new ThemeCacheDiagnostics(null, null, null, null, MetadataMissing: false)
            : AppServices.ThemeResolver.GetDiagnostics(Game.GameId);

    private static string FormatLocal(DateTimeOffset utc) =>
        utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    /// <summary>
    /// 开关型设置的统一落盘路径：值没变直接返回；变了才赋值、落盘、发属性通知、跑附加动作，最后写状态栏。
    /// 属性名由 setter 经 <see cref="CallerMemberNameAttribute"/> 传入，省掉每个属性手写 nameof。
    /// </summary>
    private void ApplyFlag(
        bool value,
        bool current,
        Action<bool> assign,
        string onStatusKey,
        string offStatusKey,
        Action? afterSave = null,
        object?[]? statusArgs = null,
        [CallerMemberName] string? propertyName = null)
    {
        if (value == current)
        {
            return;
        }

        assign(value);
        SaveSettings();
        OnPropertyChanged(propertyName);
        afterSave?.Invoke();

        if (value)
        {
            SetStatus(onStatusKey, statusArgs ?? Array.Empty<object?>());
        }
        else
        {
            SetStatus(offStatusKey);
        }
    }

    /// <summary>
    /// 下拉选项型设置的统一落盘路径：null 或同一个实例直接返回；否则赋值、落盘、发属性通知、跑附加动作、写状态栏。
    /// statusArgs 用委托延迟求值，避免调用方在 null 检查之前碰 value。
    /// </summary>
    private bool ApplyOption<T>(
        T? value,
        T? current,
        string statusKey,
        Action<T> assign,
        Action? afterSave = null,
        Func<T, object?[]>? statusArgs = null,
        [CallerMemberName] string? propertyName = null)
        where T : class
    {
        if (value is null || ReferenceEquals(value, current))
        {
            return false;
        }

        assign(value);
        SaveSettings();
        OnPropertyChanged(propertyName);
        afterSave?.Invoke();
        SetStatus(statusKey, statusArgs?.Invoke(value) ?? Array.Empty<object?>());
        return true;
    }

    /// <summary>
    /// 设置页各属性的落盘入口：直接写盘而不是登记 300ms 合并写。
    /// <para>
    /// 这里曾经走 <c>SaveSettingsDeferred</c>，好处是连续改动只写一次，但代价是
    /// 「改完设置立刻关窗口 / 进程被结束」时最后 300ms 的改动会丢——用户已经碰到过。
    /// 设置文件只有几百字节，逐次写盘的代价可以忽略，换来确定性。
    /// </para>
    /// 需要在输入框失焦 / 关闭串口一类的时机强制落盘时，仍然走 <see cref="PersistSettings"/>。
    /// </summary>
    private void SaveSettings()
    {
        var saved = AppServices.SaveSettings();
        if (!saved.IsSuccess)
        {
            AppServices.Logger.Warn("保存设置失败: " + saved.ErrorMessage);
        }
    }

    /// <summary>把「当前实际用了哪个代理」算出来显示；解析本身不联网。</summary>
    private void RefreshProxyStatus()
    {
        var resolution = ProxyResolver.Describe(AppServices.Settings);
        var sourceKey = resolution.Source switch
        {
            ProxySource.Manual => Keys.SettingsProxySourceManual,
            ProxySource.SystemSettings => Keys.SettingsProxySourceSystem,
            ProxySource.EnvironmentVariable => Keys.SettingsProxySourceEnvironment,
            _ => Keys.SettingsProxySourceDirect,
        };
        var source = T(sourceKey);

        if (resolution.Url is null)
        {
            ProxyStatusText = T(
                Keys.SettingsProxyStatusResolved,
                source,
                resolution.Warning ?? T(Keys.SettingsProxyNoProxy));
        }
        else if (resolution.Warning is null)
        {
            ProxyStatusText = T(Keys.SettingsProxyStatusResolved, source, resolution.Url);
        }
        else
        {
            ProxyStatusText = T(Keys.SettingsProxyStatusResolvedWithWarning, source, resolution.Url, resolution.Warning);
        }

        OnPropertyChanged(nameof(ProxyStatusText));

        RefreshDetectedProxy(resolution);
    }

    /// <summary>
    /// 探测「系统里有没有可用代理」，用于提示「明明有代理，程序却在直连」。
    /// <para>
    /// 只读探测：不写任何设置、不改当前生效的代理。已经生效的代理地址不算「检测到但没在用」，
    /// 不然会显示一句自相矛盾的话。
    /// </para>
    /// </summary>
    /// <param name="current">当前实际生效的解析结果。</param>
    private void RefreshDetectedProxy(ProxyResolution current)
    {
        string? detected = null;
        try
        {
            var probe = ProxyResolver.Describe(new AppSettings { ProxyMode = ProxyModes.System });
            if (probe.Url is not null
                && !string.Equals(probe.Url, current.Url, StringComparison.OrdinalIgnoreCase))
            {
                detected = probe.Url;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            AppServices.Logger.Warn("探测系统代理失败: " + ex.Message);
        }

        _detectedProxyUrl = detected;
        DetectedProxyText = detected ?? string.Empty;
        OnPropertyChanged(nameof(HasDetectedProxy));
    }

    /// <summary>按 <see cref="_probeReport"/> 重建结果行与摘要；没测过就清空。</summary>
    private void RebuildProbeRows()
    {
        _probeRows.Clear();

        var report = _probeReport;
        if (report is null)
        {
            ProbeSummaryText = string.Empty;
            OnPropertyChanged(nameof(HasProbeRows));
            OnPropertyChanged(nameof(HasDirectProbeColumn));
            return;
        }

        for (var index = 0; index < report.Current.Count; index++)
        {
            var current = report.Current[index];
            var target = NetworkProbe.SteamTargets.FirstOrDefault(
                item => string.Equals(item.Key, current.Key, StringComparison.Ordinal));
            var directText = report.Direct is { } direct && index < direct.Count
                ? FormatProbeResult(direct[index])
                : string.Empty;
            _probeRows.Add(new NetworkProbeRowViewModel(
                current.Key,
                target?.Uri.Host ?? current.Key,
                FormatProbeResult(current),
                directText,
                "probeRow" + current.Key));
        }

        // 摘要只看「当前模式」：直连失败但代理通是正常状态，不该被算成问题。
        var failed = report.Current.Count(item => !item.IsOk);
        ProbeSummaryText = failed == 0
            ? T(Keys.SettingsProxyProbeAllOk, report.Current.Count)
            : T(Keys.SettingsProxyProbeSomeFailed, failed);

        OnPropertyChanged(nameof(HasProbeRows));
        OnPropertyChanged(nameof(HasDirectProbeColumn));
    }

    /// <summary>把一次探测结果翻成一行文案（毫秒数取整，超时/状态码/拦截分别有专句）。</summary>
    private string FormatProbeResult(NetworkProbeResult result) => result.Status switch
    {
        NetworkProbeStatus.Ok => T(
            Keys.SettingsProxyProbeOk,
            (int)Math.Round(result.Elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero)),
        NetworkProbeStatus.Timeout => T(Keys.SettingsProxyProbeTimeout),
        NetworkProbeStatus.HttpStatus => T(Keys.SettingsProxyProbeHttp, result.HttpStatus ?? 0),
        NetworkProbeStatus.HostNotAllowed => T(Keys.SettingsProxyProbeBlocked),
        _ => T(Keys.SettingsProxyProbeFailed),
    };

    /// <summary>重建「标准可执行文件（默认）」这一项，让它的文案跟上当前语言；选中项按路径复位。</summary>
    private void RefreshAlternativeLabels()
    {
        if (Game is null)
        {
            return;
        }

        _isRelocalizing = true;
        try
        {
            RebuildAlternatives(Game);
        }
        finally
        {
            _isRelocalizing = false;
        }
    }

    private void RebuildAlternatives(GameInstallation game)
    {
        var selectedPath = SelectedAlternative?.ExePath;
        Alternatives.Clear();
        Alternatives.Add(new AlternativeExecutable(game.ExePath, game.ExeArgs, T(Keys.SettingsLaunchStandard)));
        foreach (var alternative in game.Alternatives)
        {
            Alternatives.Add(alternative);
        }

        SelectedAlternative = selectedPath is null
            ? Alternatives[0]
            : Alternatives.FirstOrDefault(item =>
                    string.Equals(item.ExePath, selectedPath, StringComparison.OrdinalIgnoreCase))
                ?? Alternatives[0];
    }

    /// <summary>
    /// 设置可能被其它页面改过（或 settings.json 被外部编辑）：把所有下拉的选项集合与选中项重新解析一遍。
    /// </summary>
    private void RefreshOptionSelections()
    {
        _selectedPlaySetDisplayMode =
            PlaySetPageDisplayModeCatalog.Resolve(AppServices.Settings.PlaySetPageDisplayMode);
        _selectedBackgroundDisplayMode =
            BackgroundDisplayModeCatalog.Resolve(AppServices.Settings.BackgroundDisplayMode);
        _selectedModThumbnailSize =
            ModThumbnailSizeCatalog.Resolve(AppServices.Settings.ModThumbnailSize);
        _selectedModThumbnailRetention =
            RetentionDaysCatalog.ModThumbnails.Resolve(AppServices.Settings.ModThumbnailCacheDays);
        _selectedDlcDescriptionRetention =
            RetentionDaysCatalog.DlcDescriptions.Resolve(AppServices.Settings.DlcDescriptionCacheDays);
        _selectedUiLanguage = UiLanguageCatalog.Resolve(AppServices.Settings.UiLanguage);
        _selectedTranslationEngine =
            TranslationEngineCatalog.Resolve(AppServices.Settings.TranslateEngineMode);
        _selectedTranslationLanguage =
            TranslationTargetLanguageCatalog.Resolve(AppServices.Settings.TranslateTargetLanguage);
        _translateBaseUrl = AppServices.Settings.TranslateBaseUrl ?? string.Empty;
        _translateApiKey = AppServices.Settings.TranslateApiKey ?? string.Empty;
        _translateModel = AppServices.Settings.TranslateModel ?? string.Empty;
        _translateMicrosoftKey = AppServices.Settings.TranslateMicrosoftKey ?? string.Empty;
        _translateMicrosoftRegion = AppServices.Settings.TranslateMicrosoftRegion ?? string.Empty;
        _translateGoogleKey = AppServices.Settings.TranslateGoogleKey ?? string.Empty;

        OnPropertyChanged(nameof(PlaySetDisplayModes));
        OnPropertyChanged(nameof(SelectedPlaySetDisplayMode));
        OnPropertyChanged(nameof(BackgroundDisplayModeOptions));
        OnPropertyChanged(nameof(SelectedBackgroundDisplayMode));
        OnPropertyChanged(nameof(ModThumbnailSizeOptions));
        OnPropertyChanged(nameof(SelectedModThumbnailSize));
        OnPropertyChanged(nameof(ModThumbnailRetentions));
        OnPropertyChanged(nameof(SelectedModThumbnailRetention));
        OnPropertyChanged(nameof(DlcDescriptionRetentions));
        OnPropertyChanged(nameof(SelectedDlcDescriptionRetention));
        OnPropertyChanged(nameof(LanguageOptions));
        OnPropertyChanged(nameof(SelectedUiLanguage));
        RaiseTranslationProps();
    }

    private void RaiseThemeProps()
    {
        OnPropertyChanged(nameof(ThemeSourceText));
        OnPropertyChanged(nameof(LastCheckText));
        OnPropertyChanged(nameof(NextCheckText));
        OnPropertyChanged(nameof(BackgroundFileText));
        OnPropertyChanged(nameof(LastFailureText));
        OnPropertyChanged(nameof(OnlineRefreshEnabled));
        OnPropertyChanged(nameof(DlcDescriptionOnlineEnabled));
        OnPropertyChanged(nameof(NewsFeedOnlineEnabled));
        OnPropertyChanged(nameof(ProxyUrl));
        OnPropertyChanged(nameof(SelectedProxyMode));
        OnPropertyChanged(nameof(ProxyModeOptions));
        OnPropertyChanged(nameof(IsManualProxy));
        OnPropertyChanged(nameof(ProxyStatusText));
        OnPropertyChanged(nameof(DataDirectoryPath));
        OnPropertyChanged(nameof(LegacyDataDirectoryPath));
        OnPropertyChanged(nameof(ModThumbnailOnlineEnabled));
        OnPropertyChanged(nameof(SelectedModThumbnailRetention));
        OnPropertyChanged(nameof(ModThumbnailCachePath));
        OnPropertyChanged(nameof(ModThumbnailCacheSummary));
        OnPropertyChanged(nameof(CacheTotalText));
    }

    private void RaiseGameProps()
    {
        OnPropertyChanged(nameof(ExePath));
        OnPropertyChanged(nameof(ExeArgsText));
        OnPropertyChanged(nameof(GameDataPath));
        OnPropertyChanged(nameof(DlcPath));
        OnPropertyChanged(nameof(ModDir));
        OnPropertyChanged(nameof(FormatVersionText));
        OnPropertyChanged(nameof(LoadFilePath));
        OnPropertyChanged(nameof(LoadFileStateText));
        OnPropertyChanged(nameof(WriteDlcSignature));
        OnPropertyChanged(nameof(CanRegenerateSignature));
    }
}
