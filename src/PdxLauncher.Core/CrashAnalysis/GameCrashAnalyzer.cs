namespace PdxLauncher.Core.CrashAnalysis;

using System.Globalization;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.LoadFiles;

/// <summary>
/// 崩溃分析编排器：只读扫描游戏自己的崩溃包与日志，解析 Mod 归属，最后交给 <see cref="CrashRules"/> 下结论。
/// <para>
/// 不启动游戏、不联网、不写游戏目录；单个文件读取失败只记 Warn 并跳过，
/// 保证「分析失败」不会变成「页面打不开」。
/// </para>
/// </summary>
public sealed class GameCrashAnalyzer
{
    private const int MaxCrashBundles = 10;
    private const int MaxEvidenceBytes = 2 * 1024 * 1024;
    private const int MaxMetadataBytes = 256 * 1024;

    private static readonly string[] LogFileNames =
    [
        "error.log",
        "game.log",
        "debug.log",
        "system.log",
        "graphics.log",
        "warning.log",
    ];

    private readonly IFileSystem _fileSystem;
    private readonly ILoadFileWriter _loadFiles;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    /// <summary>创建分析器。</summary>
    public GameCrashAnalyzer(
        IFileSystem fileSystem,
        ILoadFileWriter loadFiles,
        IAppLogger logger,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(loadFiles);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);

        _fileSystem = fileSystem;
        _loadFiles = loadFiles;
        _logger = logger;
        _clock = clock;
    }

    /// <summary>分析一款游戏的崩溃与启动失败证据。</summary>
    public async Task<CrashAnalysisReport> AnalyzeAsync(
        GameInstallation game,
        string? launcherLogPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.UtcNow.ToLocalTime();
        var dataDirectory = game.GameDataDir;
        bool gameDataExists = !string.IsNullOrWhiteSpace(dataDirectory)
            && _fileSystem.DirectoryExists(dataDirectory);

        var crashesDirectory = gameDataExists
            ? _fileSystem.Combine(dataDirectory, "crashes")
            : string.Empty;
        var logsDirectory = gameDataExists
            ? _fileSystem.Combine(dataDirectory, "logs")
            : string.Empty;
        var settingsPath = gameDataExists
            ? _fileSystem.Combine(dataDirectory, "pdx_settings.json")
            : string.Empty;

        var crashes = DiscoverCrashes(crashesDirectory, cancellationToken);
        var latestCrash = crashes.Count > 0 ? crashes[0] : null;

        bool usedCrashLogs = latestCrash?.HasLogs == true;
        var logRoot = usedCrashLogs
            ? _fileSystem.Combine(latestCrash!.Directory, "logs")
            : logsDirectory;

        var logInputs = ReadLogs(logRoot, usedCrashLogs, cancellationToken);
        if (logInputs.Count == 0 && usedCrashLogs && _fileSystem.DirectoryExists(logsDirectory))
        {
            // 崩溃包没带日志快照时，退回实时日志目录；此时时间锚点仍是崩溃时间。
            usedCrashLogs = false;
            logRoot = logsDirectory;
            logInputs = ReadLogs(logsDirectory, fromCrashBundle: false, cancellationToken);
        }

        var signals = CrashSignals.Collect(logInputs, latestCrash?.Timestamp ?? now);

        bool settingsCorrupt = IsSettingsCorrupt(settingsPath);
        var launcherAttempt = ParseLauncherAttempt(
            CrashLogReader.ReadTail(_fileSystem, launcherLogPath, 512 * 1024),
            game);

        var modResult = await ReadModsAsync(game, cancellationToken).ConfigureAwait(false);
        var sourceToMod = MapSourcesToMods(signals, modResult.Drafts);
        var mods = modResult.Drafts
            .Select(static draft => draft.ToInfo())
            .ToArray();

        TimeSpan? startupDuration = null;
        DateTimeOffset? gameStartTime = null;
        if (usedCrashLogs
            && latestCrash?.Timestamp is { } crashTime
            && signals.SystemLogStartTime is { } start
            && start <= crashTime)
        {
            gameStartTime = start;
            startupDuration = crashTime - start;
        }

        bool logsExist = logInputs.Count > 0;
        var findings = CrashRules.Evaluate(
            game,
            gameDataExists,
            logsExist,
            _fileSystem.FileExists(game.ExePath),
            crashes,
            signals,
            mods,
            modResult.ModListAvailable,
            launcherAttempt,
            settingsCorrupt,
            settingsPath,
            logsDirectory,
            crashesDirectory,
            startupDuration);

        var groups = signals.WithModNames(sourceToMod);
        var launchChain = new CrashLaunchChain(
            gameStartTime,
            latestCrash?.Timestamp,
            startupDuration,
            launcherAttempt,
            latestCrash?.Directory,
            string.IsNullOrWhiteSpace(logRoot) ? null : logRoot,
            usedCrashLogs);

        return new CrashAnalysisReport(
            game.GameId,
            game.DisplayName,
            game.Version,
            dataDirectory,
            now,
            crashes,
            findings,
            mods,
            signals.LogFiles,
            groups,
            launchChain,
            modResult.ModListAvailable,
            modResult.LoadFilePath);
    }

    // ------------------------------------------------------------ 崩溃包

    private IReadOnlyList<CrashBundleInfo> DiscoverCrashes(
        string crashesDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(crashesDirectory) || !_fileSystem.DirectoryExists(crashesDirectory))
        {
            return Array.Empty<CrashBundleInfo>();
        }

        var bundles = new List<CrashBundleInfo>();
        foreach (string directory in _fileSystem.EnumerateDirectories(crashesDirectory))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string exceptionPath = _fileSystem.Combine(directory, "exception.txt");
                string metaPath = _fileSystem.Combine(directory, "meta.yml");
                if (!_fileSystem.FileExists(exceptionPath) && !_fileSystem.FileExists(metaPath))
                {
                    continue;
                }

                var exception = CrashLogParser.ParseException(
                    CrashLogReader.ReadTail(_fileSystem, exceptionPath, MaxMetadataBytes));
                var meta = CrashLogParser.ParseMetaYaml(
                    CrashLogReader.ReadTail(_fileSystem, metaPath, MaxMetadataBytes));

                meta.TryGetValue("AppName", out var appName);
                meta.TryGetValue("AppVersion", out var appVersion);

                bundles.Add(new CrashBundleInfo(
                    directory,
                    exception.Timestamp,
                    exception.Code,
                    exception.Name,
                    exception.Address,
                    exception.Excerpt,
                    exception.AppName ?? appName,
                    exception.Version ?? appVersion,
                    meta,
                    _fileSystem.GetFileSize(_fileSystem.Combine(directory, "minidump.dmp")),
                    _fileSystem.DirectoryExists(_fileSystem.Combine(directory, "logs"))));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warn("读取崩溃包失败：" + directory + "：" + exception.Message);
            }
        }

        return bundles
            .OrderByDescending(static bundle => bundle.Timestamp ?? DateTimeOffset.MinValue)
            .Take(MaxCrashBundles)
            .ToArray();
    }

    private IReadOnlyList<CrashLogInput> ReadLogs(
        string logDirectory,
        bool fromCrashBundle,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(logDirectory) || !_fileSystem.DirectoryExists(logDirectory))
        {
            return Array.Empty<CrashLogInput>();
        }

        var inputs = new List<CrashLogInput>(LogFileNames.Length);
        foreach (string name in LogFileNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                string path = _fileSystem.Combine(logDirectory, name);
                if (!_fileSystem.FileExists(path))
                {
                    continue;
                }

                string text = CrashLogReader.ReadTail(_fileSystem, path, MaxEvidenceBytes);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                inputs.Add(new CrashLogInput(
                    name,
                    path,
                    text,
                    _fileSystem.GetFileSize(path),
                    fromCrashBundle));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warn("读取游戏日志失败：" + name + "：" + exception.Message);
            }
        }

        return inputs;
    }

    // ------------------------------------------------------------ Mod

    private async Task<ModReadResult> ReadModsAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        var drafts = new List<ModDraft>();
        string? loadFilePath = null;
        bool modListAvailable = true;

        if (game.WritesLoadFile)
        {
            try
            {
                loadFilePath = _fileSystem.Combine(
                    game.GameDataDir,
                    LoadFileWriter.GetFileName(game.Format));

                var content = await _loadFiles.ReadAsync(game, cancellationToken).ConfigureAwait(false);
                foreach (string path in content.EnabledModDirPaths.Concat(content.EnabledModArchivePaths))
                {
                    drafts.Add(ReadModDraft(path, enabledKnown: true));
                }
            }
            catch (InvalidOperationException)
            {
                // EU5 / CS2 这类没有加载文件的格式：下面退回 ModDir 枚举。
                modListAvailable = false;
                loadFilePath = null;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                modListAvailable = false;
                loadFilePath = null;
                _logger.Warn("读取加载文件失败：" + exception.Message);
            }
        }
        else
        {
            modListAvailable = false;
            if (!string.IsNullOrWhiteSpace(game.ModDir) && _fileSystem.DirectoryExists(game.ModDir))
            {
                foreach (string directory in _fileSystem.EnumerateDirectories(game.ModDir))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    drafts.Add(ReadModDraft(directory, enabledKnown: false));
                }
            }
        }

        return new ModReadResult(drafts, modListAvailable, loadFilePath);
    }

    private ModDraft ReadModDraft(string path, bool enabledKnown)
    {
        bool isArchive = path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        bool exists = isArchive ? _fileSystem.FileExists(path) : _fileSystem.DirectoryExists(path);
        bool nonAscii = path.Any(static character => character > 0x7F);
        var metadata = ModMetadata.Empty;

        if (exists && !isArchive)
        {
            try
            {
                string metadataPath = _fileSystem.Combine(path, ".metadata", "metadata.json");
                if (_fileSystem.FileExists(metadataPath))
                {
                    metadata = CrashLogParser.ParseModMetadata(
                        CrashLogReader.ReadTail(_fileSystem, metadataPath, MaxMetadataBytes));
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                _logger.Warn("读取 Mod 元数据失败：" + path + "：" + exception.Message);
            }
        }

        return new ModDraft(path, isArchive, exists, nonAscii, enabledKnown, metadata);
    }

    private Dictionary<string, string> MapSourcesToMods(
        CrashSignals signals,
        IReadOnlyList<ModDraft> drafts)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (signals.SourceCounts.Count == 0 || drafts.Count == 0)
        {
            return map;
        }

        foreach (string source in signals.SourceCounts.Keys.OrderBy(static key => key, StringComparer.Ordinal))
        {
            foreach (var mod in drafts)
            {
                if (!mod.Exists || mod.IsArchive)
                {
                    continue;
                }

                try
                {
                    string candidate = _fileSystem.Combine(mod.Path, source);
                    if (_fileSystem.FileExists(candidate))
                    {
                        mod.AddSourceHit(source);
                        map[source] = mod.DisplayName;
                        break;
                    }
                }
                catch (Exception exception) when (exception is ArgumentException or IOException)
                {
                    _logger.Warn("检查 Mod 文件失败：" + mod.Path + "：" + exception.Message);
                }
            }
        }

        return map;
    }

    // ------------------------------------------------------------ 启动器 / 设置

    private bool IsSettingsCorrupt(string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settingsPath) || !_fileSystem.FileExists(settingsPath))
        {
            return false;
        }

        string text = CrashLogReader.ReadTail(_fileSystem, settingsPath, MaxMetadataBytes);
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind != JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return true;
        }
        catch (ArgumentException)
        {
            return true;
        }
    }

    private static LauncherAttemptInfo ParseLauncherAttempt(string? text, GameInstallation game)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return LauncherAttemptInfo.None;
        }

        string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (int index = lines.Length - 1; index >= 0; index--)
        {
            string line = lines[index];
            if (!line.Contains(game.GameId, StringComparison.OrdinalIgnoreCase)
                && !line.Contains(game.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            for (int offset = 0; offset <= 4 && index + offset < lines.Length; offset++)
            {
                string candidate = lines[index + offset];
                bool failed = Contains(candidate, "启动失败")
                    || Contains(candidate, "启动进程失败")
                    || Contains(candidate, "找不到可执行文件")
                    || Contains(candidate, "Failed to start");
                bool succeeded = Contains(candidate, "已启动");

                if (failed || succeeded)
                {
                    return new LauncherAttemptInfo(
                        true,
                        succeeded,
                        TryParseLogTime(candidate),
                        Truncate(candidate.Trim(), 300));
                }
            }
        }

        return LauncherAttemptInfo.None;
    }

    private static DateTimeOffset? TryParseLogTime(string line)
    {
        if (line.Length < 19)
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            line[..19],
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeLocal,
            out var time)
            ? time
            : null;
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];

    private sealed record ModReadResult(
        IReadOnlyList<ModDraft> Drafts,
        bool ModListAvailable,
        string? LoadFilePath);

    private sealed class ModDraft(
        string path,
        bool isArchive,
        bool exists,
        bool nonAscii,
        bool enabledKnown,
        ModMetadata metadata)
    {
        private readonly List<string> _sourceHits = new();

        public string Path => path;

        public bool IsArchive => isArchive;

        public bool Exists => exists;

        public string DisplayName => !string.IsNullOrWhiteSpace(metadata.Name)
            ? metadata.Name!
            : System.IO.Path.GetFileName(path.TrimEnd(
                System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar));

        public void AddSourceHit(string source)
        {
            if (!_sourceHits.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                _sourceHits.Add(source);
            }
        }

        public CrashModInfo ToInfo() => new(
            path,
            metadata.Name,
            metadata.Version,
            metadata.SupportedGameVersion,
            exists,
            isArchive,
            enabledKnown,
            nonAscii,
            _sourceHits.ToArray());
    }
}
