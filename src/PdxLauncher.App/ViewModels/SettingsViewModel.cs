namespace PdxLauncher.App.ViewModels;

using System.Collections.ObjectModel;
using System.Globalization;
using System.Runtime.CompilerServices;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Storage;

/// <summary>
/// 设置页的视图模型：界面语言、主题来源与联网检查、网络代理、界面档位、启动参数、
/// 数据目录、四类联网缓存与危险区（dlc_signature）。
/// 只读展示 + 少量可写设置，全部持久化到 AppServices.Settings 并立即 SaveSettings()。
/// 不含任何 Microsoft.UI.Xaml 类型；ContentDialog 确认与"打开目录"等 shell 动作由页面负责。
/// 文案统一走 <see cref="LocalizedObservableObject"/>：语言切换后基类重发属性通知，
/// 状态栏这类拼好的句子用 (键, 参数) 记住来源，在 <see cref="Relocalize"/> 里按新语言重建。
/// </summary>
public sealed class SettingsViewModel : LocalizedObservableObject
{
    /// <summary>主题缓存 TTL：5 天 = 432,000,000 ms（docs/DATA-FORMATS.md 冻结值）。</summary>
    public static readonly TimeSpan ThemeCacheTtl = TimeSpan.FromMilliseconds(432_000_000);

    private GameInstallation? _game;
    private ThemeSpec? _theme;
    private bool _isBusy;
    private bool _isLoaded;
    private bool _isRelocalizing;
    private string _statusMessage = string.Empty;
    private string? _statusKey;
    private object?[] _statusArgs = Array.Empty<object?>();
    private string _proxyUrl = string.Empty;
    private string _proxyStatusText = string.Empty;
    private long _cacheTotalBytes;
    private ProxyModeOption _selectedProxyMode =
        ProxyModeCatalog.Resolve(AppServices.Settings.ProxyMode, AppServices.Settings.ProxyUrl);
    private AlternativeExecutable? _selectedAlternative;
    private string _lastSignature = string.Empty;
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

    public SettingsViewModel()
    {
        CacheRows = new ObservableCollection<CacheRowViewModel>
        {
            new(
                CacheRowViewModel.ThemeKey,
                Keys.SettingsCacheRowThemeTitle,
                Keys.SettingsCacheRowThemeDesc,
                AppServices.ThemeCacheRoot,
                () => CacheStorage.Clear(AppServices.FileSystem, AppServices.ThemeCacheRoot)),
            new(
                CacheRowViewModel.NewsKey,
                Keys.SettingsCacheRowNewsTitle,
                Keys.SettingsCacheRowNewsDesc,
                AppServices.NewsCacheRoot,
                AppServices.NewsFeed.ClearCache),
            new(
                CacheRowViewModel.ModThumbnailsKey,
                Keys.SettingsCacheRowModThumbnailsTitle,
                Keys.SettingsCacheRowModThumbnailsDesc,
                AppServices.ModThumbnailCacheRoot,
                () => AppServices.ModThumbnails.Cache.Clear()),
            new(
                CacheRowViewModel.DlcDescriptionsKey,
                Keys.SettingsCacheRowDlcDescriptionsTitle,
                Keys.SettingsCacheRowDlcDescriptionsDesc,
                AppServices.DlcDescriptions.CachePath,
                AppServices.DlcDescriptions.ClearCache),
        };
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
        private set => SetProperty(ref _isBusy, value);
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

    /// <summary>当前主题来源：联网缓存 / 安装目录 / 内置占位。</summary>
    public string ThemeSourceText => T(ThemeSourceKey);

    /// <summary>当前主题来源对应的本地化键（展示文案与状态栏共用，避免两处 switch 漂移）。</summary>
    private string ThemeSourceKey
    {
        get
        {
            var source = _theme?.Source ?? Game?.Theme.Source ?? ThemeAssetSource.BuiltInPlaceholder;
            return source switch
            {
                ThemeAssetSource.OnlineCache => Keys.SettingsThemeSourceOnline,
                ThemeAssetSource.InstallDir => Keys.SettingsThemeSourceInstallDir,
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

    /// <summary>下次检查时间 = 上次检查 + 5 天；从未检查时显示"下次启动时"。</summary>
    public string NextCheckText
    {
        get
        {
            var last = GetLastCheckUtc();
            return last is null
                ? T(Keys.SettingsThemeNextOnStartup)
                : FormatLocal(last.Value + ThemeCacheTtl);
        }
    }

    /// <summary>联网刷新开关；写入 AppSettings.OnlineThemeRefreshEnabled 并立即落盘。</summary>
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

    /// <summary>DLC 简介联网开关；写入 AppSettings.OnlineDlcDescriptionEnabled 并立即落盘。</summary>
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

    /// <summary>官方公告联网开关；写入 AppSettings.NewsFeedOnlineEnabled 并立即落盘。</summary>
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

    /// <summary>把内存中的 AppSettings 落盘（页面失焦 / 卸载时调用）。</summary>
    public void PersistSettings() => SaveSettings();

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
    /// 整窗背景显示方式；改动立即落盘并通知主窗口重画（只是换 Stretch / 对齐，不重新解码大图）。
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
    /// Mod 缩略图尺寸；改动立即落盘。缩略图尺寸在行视图模型构造时定下，
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

    /// <summary>DLC 简介缓存天数；改动立即落盘，下一次查询按新值判断过期。</summary>
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
    public Task LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
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
            return Task.CompletedTask;
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
        IsLoaded = true;

        RaiseThemeProps();
        RaiseGameProps();

        // 主题素材已由 GameDiscovery 在发现后合并好（缓存层 + 联网刷新），
        // 这里只展示 game.Theme，不再重复解析、也不在页面加载路径上发网络请求。
        _theme = game.Theme;
        RaiseThemeProps();
        return Task.CompletedTask;
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
        try
        {
            // 走同一条刷新管线：下载完成后由 ThemeUpdateService 回写 AppServices.Games 并广播，
            // 主窗口背景随之更新；这里只负责把结果反映到设置页文案。
            await ThemeUpdateService
                .RefreshAsync(Game, cancellationToken)
                .ConfigureAwait(true);

            _theme = AppServices.Games
                .FirstOrDefault(g => string.Equals(g.GameId, Game.GameId, StringComparison.OrdinalIgnoreCase))
                ?.Theme ?? Game.Theme;
            OnPropertyChanged(nameof(ThemeSourceText));
            RaiseThemeProps();

            var sourceKey = ThemeSourceKey;
            SetStatus(Keys.SettingsThemeStatusDone, T(sourceKey), NextCheckText);
            AppServices.Logger.Info("主题检查完成（gameId=" + Game.GameId + "，source=" + T(sourceKey) + "）");
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

    /// <summary>四类联网缓存各一行（顺序即界面顺序）。</summary>
    public ObservableCollection<CacheRowViewModel> CacheRows { get; }

    /// <summary>合计占用文案（只统计字节，文件数在每行里）。</summary>
    public string CacheTotalText => T(Keys.SettingsCacheSummary, SizeFormatter.Format(_cacheTotalBytes));

    /// <summary>重新统计四类缓存的占用（只看文件属性，不联网）。</summary>
    public async Task RefreshCacheStatsAsync(CancellationToken cancellationToken = default)
    {
        var rows = CacheRows.ToArray();
        if (rows.Length == 0)
        {
            return;
        }

        var fileSystem = AppServices.FileSystem;
        var usages = await Task.Run(
            () =>
            {
                var result = new CacheUsage[rows.Length];
                for (var index = 0; index < rows.Length; index++)
                {
                    result[index] = CacheStorage.Measure(fileSystem, rows[index].Path);
                }

                return result;
            },
            cancellationToken).ConfigureAwait(true);

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

    /// <summary>清除全部四类缓存。</summary>
    public async Task ClearAllCachesAsync(CancellationToken cancellationToken)
    {
        var before = _cacheTotalBytes;
        try
        {
            await Task.Run(
                () =>
                {
                    foreach (var row in CacheRows.ToArray())
                    {
                        row.Clear();
                    }
                },
                cancellationToken).ConfigureAwait(true);
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
        try
        {
            AppServices.SaveSettings();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("保存代理设置失败", ex);
            SetStatus(Keys.SettingsStatusSaveFailed, ex.Message);
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
        RaiseThemeProps();
        return true;
    }

    // ------------------------------------------------------------ 数据目录 / 联网图片

    /// <summary>本启动器数据根目录（settings.json / 日志 / 各缓存都在这里）。</summary>
    public string DataDirectoryPath => AppServices.DataRoot;

    /// <summary>旧版本的 C 盘数据目录；迁移完成后可以手动清理。</summary>
    public string LegacyDataDirectoryPath => DataRootMigrator.LegacyRoot;

    /// <summary>联网补全 Mod 缩略图开关；写入 AppSettings.OnlineModThumbnailEnabled 并立即落盘。</summary>
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

    /// <summary>Mod 缩略图缓存保留天数；改动立即落盘，下次加载 Mod 库时按新值清理。</summary>
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

    // ------------------------------------------------------------ 内部

    /// <summary>语言切换后要把拼好的句子按新语言重建。</summary>
    protected override void Relocalize()
    {
        RefreshProxyStatus();
        OnPropertyChanged(nameof(CacheTotalText));
        RefreshAlternativeLabels();
        if (_statusKey is not null)
        {
            StatusMessage = T(_statusKey, _statusArgs);
        }
    }

    private DateTimeOffset? GetLastCheckUtc() =>
        Game is null ? null : AppServices.ThemeResolver.GetLastCheckedUtc(Game.GameId);

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

    private void SaveSettings()
    {
        try
        {
            AppServices.SaveSettings();
        }
        catch (Exception ex)
        {
            AppServices.Logger.Error("保存设置失败", ex);
            SetStatus(Keys.SettingsStatusSaveFailed, ex.Message);
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
    }

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
    }

    private void RaiseThemeProps()
    {
        OnPropertyChanged(nameof(ThemeSourceText));
        OnPropertyChanged(nameof(LastCheckText));
        OnPropertyChanged(nameof(NextCheckText));
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
