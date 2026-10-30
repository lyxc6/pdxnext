namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Themes;

/// <summary>
/// 已安装游戏发现，两个来源：
/// <list type="number">
/// <item><description>每个 Steam 库的 <c>steamapps\common\*</c>：找到 <c>launcher-settings.json</c> 即收录；</description></item>
/// <item><description>官方 Paradox 启动器的独立游戏库根（见 <see cref="ParadoxLauncherLibraryLocator"/>）：
/// 库根本身与其一级子目录里找到 <c>launcher-settings.json</c> 即收录，不依赖 Steam。</description></item>
/// </list>
/// 收录不再需要 gameId 白名单：只要目录里有可解析的 <c>launcher-settings.json</c> 就算已安装，
/// 支持等级由 <see cref="KnownGames.GetSupport"/> 决定（完整适配 / 仅启动 / 未适配），
/// 内容管理相关 UI 会按等级禁用（见 <see cref="GameSupport"/>）。
/// 同一个 gameId 出现在多个来源时保留先发现的（Steam 先扫，优先于独立库）。
/// </summary>
public sealed class GameCatalog : IGameCatalog
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly SteamLibraryLocator _libraryLocator;
    private readonly LauncherSettingsReader _settingsReader;
    private readonly AppManifestReader _manifestReader;
    private readonly ThemeReader _themeReader;
    private readonly InstallSignatureDetector _signatureDetector;
    private readonly Func<IReadOnlyList<string>> _libraryProvider;
    private readonly Func<IReadOnlyList<string>> _standaloneRootProvider;

    /// <summary>用默认的 Steam 库定位器 + 官方启动器库定位器构造。</summary>
    public GameCatalog(IFileSystem fileSystem, IAppLogger logger)
        : this(
            fileSystem,
            logger,
            new SteamLibraryLocator(fileSystem, logger),
            new AppManifestReader(fileSystem, logger),
            new LauncherSettingsReader(fileSystem, logger),
            new ThemeReader(fileSystem, logger),
            new ParadoxLauncherLibraryLocator(fileSystem, logger))
    {
    }

    /// <summary>完整构造（自检里可注入固定的库列表与独立库根列表）。</summary>
    public GameCatalog(
        IFileSystem fileSystem,
        IAppLogger logger,
        SteamLibraryLocator libraryLocator,
        AppManifestReader manifestReader,
        LauncherSettingsReader settingsReader,
        ThemeReader themeReader,
        ParadoxLauncherLibraryLocator standaloneLibraryLocator,
        InstallSignatureDetector? signatureDetector = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _themeReader = themeReader ?? throw new ArgumentNullException(nameof(themeReader));
        ArgumentNullException.ThrowIfNull(standaloneLibraryLocator);
        _signatureDetector = signatureDetector ?? new InstallSignatureDetector(fileSystem);
        _libraryProvider = _libraryLocator.FindLibraries;
        _standaloneRootProvider = standaloneLibraryLocator.FindLibraries;
    }

    /// <summary>用固定的库列表构造（离线/自检用）；<paramref name="standaloneRoots"/> 为 null 时不扫独立库。</summary>
    public GameCatalog(
        IFileSystem fileSystem,
        IAppLogger logger,
        AppManifestReader manifestReader,
        LauncherSettingsReader settingsReader,
        ThemeReader themeReader,
        IReadOnlyList<string> libraries,
        IReadOnlyList<string>? standaloneRoots = null,
        InstallSignatureDetector? signatureDetector = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _themeReader = themeReader ?? throw new ArgumentNullException(nameof(themeReader));
        ArgumentNullException.ThrowIfNull(libraries);
        _libraryLocator = new SteamLibraryLocator(fileSystem, logger);
        _signatureDetector = signatureDetector ?? new InstallSignatureDetector(fileSystem);
        var frozenLibraries = libraries.ToArray();
        var frozenStandaloneRoots = standaloneRoots?.ToArray() ?? Array.Empty<string>();
        _libraryProvider = () => frozenLibraries;
        _standaloneRootProvider = () => frozenStandaloneRoots;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken)
    {
        var results = Discover(cancellationToken);
        return Task.FromResult(results);
    }

    /// <inheritdoc />
    public Task<GameInstallation?> DiscoverOneAsync(string gameId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(gameId);
        var wanted = KnownGames.CanonicalizeGameId(gameId);
        foreach (var installation in Discover(cancellationToken))
        {
            if (string.Equals(installation.GameId, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<GameInstallation?>(installation);
            }
        }

        return Task.FromResult<GameInstallation?>(null);
    }

    /// <summary>同步发现全部游戏（按 gameId 排序）。</summary>
    public IReadOnlyList<GameInstallation> Discover(CancellationToken cancellationToken = default)
    {
        var results = new List<GameInstallation>();
        var seenGameIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Steam 先扫：同一个 gameId 同时存在于 Steam 库与官方独立库时保留 Steam 那份（有 appmanifest，SteamAppId 可用）。
        foreach (var library in _libraryProvider())
        {
            ScanSteamLibrary(library, results, seenGameIds, cancellationToken);
        }

        foreach (var root in _standaloneRootProvider())
        {
            ScanStandaloneRoot(root, results, seenGameIds, cancellationToken);
        }

        var ordered = results.OrderBy(x => x.GameId, StringComparer.OrdinalIgnoreCase).ToList();
        _logger.Info("游戏发现完成，共 " + ordered.Count + " 个");
        return ordered;
    }

    private void ScanSteamLibrary(
        string library,
        List<GameInstallation> results,
        HashSet<string> seenGameIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var commonDir = _fileSystem.Combine(library, "steamapps", "common");
        if (!_fileSystem.DirectoryExists(commonDir))
        {
            return;
        }

        var manifestsByInstallDir = BuildManifestIndex(library);

        foreach (var installRoot in _fileSystem.EnumerateDirectories(commonDir))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var installDirName = _fileSystem.GetFileName(installRoot);
            manifestsByInstallDir.TryGetValue(installDirName, out var manifest);

            var settings = ReadSettings(installRoot);
            if (settings is null)
            {
                // EU5 这类官方安装（1.0 起）不带 launcher-settings.json，走特征识别兜底；
                // 其余非 PDX 游戏在这里被自然跳过（包括残留的坏副本）。
                TryAddDetectedInstallation(installRoot, manifest, results, seenGameIds);
                continue;
            }

            TryAddInstallation(settings, installRoot, manifest, results, seenGameIds);
        }
    }

    /// <summary>
    /// 扫描官方启动器独立库根：根自身可能就是一个游戏安装目录（配置的对象条目指向它），
    /// 一级子目录则是常规的 <c>&lt;库根&gt;\&lt;游戏目录&gt;\launcher-settings.json</c> 布局。
    /// </summary>
    private void ScanStandaloneRoot(
        string root,
        List<GameInstallation> results,
        HashSet<string> seenGameIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_fileSystem.DirectoryExists(root))
        {
            _logger.Info("独立游戏库路径不存在，跳过: " + root);
            return;
        }

        AddStandaloneCandidate(root, results, seenGameIds, cancellationToken);

        foreach (var installRoot in _fileSystem.EnumerateDirectories(root))
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddStandaloneCandidate(installRoot, results, seenGameIds, cancellationToken);
        }
    }

    private void AddStandaloneCandidate(
        string installRoot,
        List<GameInstallation> results,
        HashSet<string> seenGameIds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var settings = ReadSettings(installRoot);
        if (settings is null)
        {
            // 独立库同样可能是「无 launcher-settings.json」的新game（EU5 装成独立版时）。
            TryAddDetectedInstallation(installRoot, manifest: null, results, seenGameIds);
            return;
        }

        // 独立库没有 appmanifest，SteamAppId 保持 null。
        TryAddInstallation(settings, installRoot, manifest: null, results, seenGameIds);
    }

    private LauncherSettings? ReadSettings(string installRoot)
    {
        var settingsPath = _settingsReader.FindSettingsFile(installRoot);
        if (settingsPath is null)
        {
            return null;
        }

        return _settingsReader.ReadFile(settingsPath);
    }

    private void TryAddInstallation(
        LauncherSettings settings,
        string installRoot,
        AppManifest? manifest,
        List<GameInstallation> results,
        HashSet<string> seenGameIds)
    {
        // 去重键用正名：同一个游戏在 Steam 库与独立库里可能写不同的官方 id（CS1 写 cities_v2），
        // 不归一就会同时收进两份，后面的选择器与补丁状态都会对不上。
        var canonicalId = KnownGames.CanonicalizeGameId(settings.GameId);
        if (!seenGameIds.Add(canonicalId))
        {
            _logger.Info("重复的 gameId 安装，保留先发现的: " + canonicalId + " @ " + installRoot);
            return;
        }

        results.Add(BuildInstallation(settings, installRoot, manifest));
    }

    /// <summary>
    /// 目录里没有 launcher-settings.json 时，尝试按安装特征识别（见 <see cref="InstallSignatureDetector"/>）。
    /// 识别不到就安静跳过：<c>steamapps\common</c> 下绝大多数目录属于其它厂商。
    /// </summary>
    private void TryAddDetectedInstallation(
        string installRoot,
        AppManifest? manifest,
        List<GameInstallation> results,
        HashSet<string> seenGameIds)
    {
        var detected = _signatureDetector.Detect(installRoot);
        if (detected is null)
        {
            return;
        }

        var canonicalId = KnownGames.CanonicalizeGameId(detected.GameId);
        if (!seenGameIds.Add(canonicalId))
        {
            _logger.Info("重复的 gameId 安装，保留先发现的: " + canonicalId + " @ " + installRoot);
            return;
        }

        _logger.Info(
            "按安装特征识别（目录内无 launcher-settings.json）: " + canonicalId + " @ " + installRoot);
        results.Add(BuildDetectedInstallation(detected, installRoot, manifest));
    }

    /// <summary>
    /// 特征识别出来的安装：有内容布局时（EU5）补齐文档目录 / DLC 目录 / 内容格式，
    /// 没有时只提供启动必需的信息（版本留空、无 DLC/mod 路径、主题走安装目录兜底）。
    /// </summary>
    private GameInstallation BuildDetectedInstallation(
        DetectedInstall detected,
        string installRoot,
        AppManifest? manifest)
    {
        var support = detected.Support;
        var theme = _themeReader.Read(installRoot);
        var content = detected.Content;

        var gameDataDir = installRoot;
        var dlcPath = string.Empty;
        var modDir = string.Empty;
        var format = LoadFileFormat.DlcLoadV0;

        if (content is not null)
        {
            // 文档目录模板可能带 %USER_DOCUMENTS%：与 launcher-settings.json 的 gameDataPath 用同一套展开规则。
            var expanded = LauncherSettingsReader.ExpandEnvironmentVariables(content.GameDataDirTemplate);
            var trimmed = expanded.Replace('/', '\\').TrimEnd('\\');
            gameDataDir = Path.IsPathRooted(trimmed)
                ? trimmed
                : _fileSystem.GetFullPath(_fileSystem.Combine(installRoot, trimmed));

            dlcPath = _fileSystem.Combine(installRoot, content.DlcPathRelativeToInstallRoot.Replace('/', '\\'));
            modDir = _fileSystem.Combine(gameDataDir, "mod");
            format = content.Format;
        }

        return new GameInstallation(
            KnownGames.CanonicalizeGameId(detected.GameId),
            detected.DisplayName,
            Version: string.Empty,
            installRoot,
            LauncherSettingsPath: string.Empty,
            Format: format,
            GameDataDir: gameDataDir,
            DlcPath: dlcPath,
            ModDir: modDir,
            ExePath: detected.ExePath,
            ExeArgs: Array.Empty<string>(),
            Alternatives: Array.Empty<AlternativeExecutable>(),
            theme,
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            manifest?.AppId,
            support);
    }

    private Dictionary<string, AppManifest> BuildManifestIndex(string library)    {
        var index = new Dictionary<string, AppManifest>(StringComparer.OrdinalIgnoreCase);
        foreach (var manifest in _manifestReader.ReadAll(library))
        {
            if (!index.ContainsKey(manifest.InstallDir))
            {
                index[manifest.InstallDir] = manifest;
            }
        }

        return index;
    }

    private GameInstallation BuildInstallation(LauncherSettings settings, string installRoot, AppManifest? manifest)
    {
        // launcher-settings.json 里写的是官方启动器的 id（CS1 = cities_v2），本启动器一律用正名，
        // 否则支持等级、appId 映射、创意工坊来源、元数据主题都会落空。
        var gameId = KnownGames.CanonicalizeGameId(settings.GameId);
        var support = KnownGames.GetSupport(gameId);
        var displayName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? KnownGames.GetDisplayName(gameId) ?? gameId
            : settings.DisplayName;

        var theme = _themeReader.Read(installRoot, settings.ThemeFile);

        return new GameInstallation(
            gameId,
            displayName,
            settings.Version,
            installRoot,
            settings.LauncherSettingsPath,
            settings.Format,
            settings.GameDataDir,
            settings.DlcPath,
            settings.ModDir,
            settings.ExePath,
            settings.ExeArgs,
            settings.Alternatives,
            theme,
            settings.BrowserDlcUrl,
            settings.BrowserModUrl,
            manifest?.AppId,
            support);
    }
}
