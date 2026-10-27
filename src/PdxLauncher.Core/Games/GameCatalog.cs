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
        ParadoxLauncherLibraryLocator standaloneLibraryLocator)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _libraryLocator = libraryLocator ?? throw new ArgumentNullException(nameof(libraryLocator));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _themeReader = themeReader ?? throw new ArgumentNullException(nameof(themeReader));
        ArgumentNullException.ThrowIfNull(standaloneLibraryLocator);
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
        IReadOnlyList<string>? standaloneRoots = null)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _manifestReader = manifestReader ?? throw new ArgumentNullException(nameof(manifestReader));
        _settingsReader = settingsReader ?? throw new ArgumentNullException(nameof(settingsReader));
        _themeReader = themeReader ?? throw new ArgumentNullException(nameof(themeReader));
        ArgumentNullException.ThrowIfNull(libraries);
        _libraryLocator = new SteamLibraryLocator(fileSystem, logger);
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
        foreach (var installation in Discover(cancellationToken))
        {
            if (string.Equals(installation.GameId, gameId, StringComparison.OrdinalIgnoreCase))
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

            var settings = ReadSettings(installRoot);
            if (settings is null)
            {
                // 绝大多数非 PDX 游戏都在这里被自然跳过（包括残留的坏副本）
                continue;
            }

            var installDirName = _fileSystem.GetFileName(installRoot);
            manifestsByInstallDir.TryGetValue(installDirName, out var manifest);
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
        if (!seenGameIds.Add(settings.GameId))
        {
            _logger.Info("重复的 gameId 安装，保留先发现的: " + settings.GameId + " @ " + installRoot);
            return;
        }

        results.Add(BuildInstallation(settings, installRoot, manifest));
    }

    private Dictionary<string, AppManifest> BuildManifestIndex(string library)
    {
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
        var support = KnownGames.GetSupport(settings.GameId);
        var displayName = string.IsNullOrWhiteSpace(settings.DisplayName)
            ? KnownGames.GetDisplayName(settings.GameId) ?? settings.GameId
            : settings.DisplayName;

        var theme = _themeReader.Read(installRoot, settings.ThemeFile);

        return new GameInstallation(
            settings.GameId,
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
