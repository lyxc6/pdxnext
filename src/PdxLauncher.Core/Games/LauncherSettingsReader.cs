namespace PdxLauncher.Core.Games;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 读取并解析游戏的 <c>launcher-settings.json</c>。探测顺序（大小写不敏感）：
/// <c>&lt;root&gt;\launcher-settings.json</c>、
/// <c>&lt;root&gt;\launcher\launcher-settings.json</c>、
/// <c>&lt;root&gt;\Launcher\launcher-settings.json</c>。
/// 所有相对路径都相对 launcher-settings.json 所在目录绝对化（这是 Paradox 启动器的实际语义：
/// CK3 的 <c>exePath="../binaries/ck3.exe"</c> 与 <c>dlcPath="../game"</c> 都是从 launcher 目录往上走）。
/// 解析失败返回 null 并记日志，绝不抛异常。
/// </summary>
public sealed class LauncherSettingsReader
{
    private const string SettingsFileName = "launcher-settings.json";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public LauncherSettingsReader(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 在安装根目录下按三种约定位置查找 launcher-settings.json；找不到返回 null。
    /// </summary>
    public string? FindSettingsFile(string installRoot)
    {
        ArgumentNullException.ThrowIfNull(installRoot);
        if (string.IsNullOrWhiteSpace(installRoot) || !_fileSystem.DirectoryExists(installRoot))
        {
            return null;
        }

        var atRoot = CaseInsensitivePath.FindFile(_fileSystem, installRoot, SettingsFileName);
        if (atRoot is not null)
        {
            return atRoot;
        }

        foreach (var folderName in new[] { "launcher", "Launcher" })
        {
            var folder = CaseInsensitivePath.FindDirectory(_fileSystem, installRoot, folderName);
            if (folder is null)
            {
                continue;
            }

            var found = CaseInsensitivePath.FindFile(_fileSystem, folder, SettingsFileName);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>读取某个安装根目录的设置；找不到或解析失败返回 null。</summary>
    public LauncherSettings? Read(string installRoot)
    {
        var settingsPath = FindSettingsFile(installRoot);
        if (settingsPath is null)
        {
            return null;
        }

        return ReadFile(settingsPath);
    }

    /// <summary>读取指定的 launcher-settings.json；解析失败返回 null。</summary>
    public LauncherSettings? ReadFile(string settingsPath)
    {
        ArgumentNullException.ThrowIfNull(settingsPath);
        if (!_fileSystem.FileExists(settingsPath))
        {
            _logger.Warn("launcher-settings.json 不存在: " + settingsPath);
            return null;
        }

        string text;
        try
        {
            text = _fileSystem.ReadAllText(settingsPath);
        }
        catch (IOException ex)
        {
            _logger.Warn("读取 launcher-settings.json 失败: " + settingsPath + " (" + ex.Message + ")");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取 launcher-settings.json: " + settingsPath + " (" + ex.Message + ")");
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                _logger.Warn("launcher-settings.json 根节点不是对象，跳过: " + settingsPath);
                return null;
            }

            var settingsDirectory = _fileSystem.GetDirectoryName(settingsPath) ?? string.Empty;

            // 安装根 = launcher-settings.json 所在的"游戏根"，而不是一律取上一层：
            //   CK3 / V3 / PA: <root>\launcher\launcher-settings.json → 上一层是 <root>
            //   Stellaris:     <root>\launcher-settings.json          → 本层就是 <root>
            // 一律取上一层会把 Stellaris 的 InstallRoot 算成 steamapps\common，
            // 进而让主题探测、dlcPath 比较和 SteamAppId 全部错位。
            var settingsFolderName = _fileSystem.GetFileName(settingsDirectory);
            var installRoot = string.Equals(settingsFolderName, "launcher", StringComparison.OrdinalIgnoreCase)
                ? _fileSystem.GetDirectoryName(settingsDirectory) ?? settingsDirectory
                : settingsDirectory;

            var gameId = GetString(root, "gameId");
            if (string.IsNullOrWhiteSpace(gameId))
            {
                _logger.Warn("launcher-settings.json 缺少 gameId，跳过: " + settingsPath);
                return null;
            }

            var formatVersion = GetDouble(root, "formatVersion") ?? 0.0;
            var gameDataDir = ResolveGameDataDir(root, installRoot);
            var dlcPath = ResolveRelative(
                _fileSystem,
                settingsDirectory,
                GetString(root, "dlcPath") ?? "./");
            var modDir = ResolveModDir(root, gameDataDir);

            var displayName = GetString(root, "displayName");
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = _fileSystem.GetFileName(installRoot);
            }

            var version = GetString(root, "version") ?? string.Empty;
            var rawVersion = GetString(root, "rawVersion");
            if (string.IsNullOrWhiteSpace(rawVersion))
            {
                rawVersion = version;
            }

            return new LauncherSettings(
                gameId,
                displayName,
                version,
                rawVersion,
                formatVersion,
                MapFormat(formatVersion),
                gameDataDir,
                dlcPath,
                modDir,
                ResolveRelative(_fileSystem, settingsDirectory, GetString(root, "exePath") ?? string.Empty),
                ReadStringArray(root, "exeArgs"),
                ReadAlternatives(root, settingsDirectory),
                NullIfEmpty(GetString(root, "browserDlcUrl")),
                NullIfEmpty(GetString(root, "browserModUrl")),
                ResolveOptional(_fileSystem, settingsDirectory, GetString(root, "themeFile")),
                settingsPath,
                installRoot,
                GetBool(root, "isLooseFilesModSupported"));
        }
        catch (JsonException ex)
        {
            _logger.Warn("launcher-settings.json 解析失败，跳过: " + settingsPath + " (" + ex.Message + ")");
            return null;
        }
    }

    /// <summary>formatVersion → 加载文件格式。0 → DlcLoadV0；1 → ContentLoadV1；1.1 → ContentLoadV1_1。</summary>
    public static LoadFileFormat MapFormat(double formatVersion) => formatVersion switch
    {
        >= 1.05 => LoadFileFormat.ContentLoadV1_1,
        >= 0.5 => LoadFileFormat.ContentLoadV1,
        _ => LoadFileFormat.DlcLoadV0,
    };

    /// <summary>展开 %USER_DOCUMENTS% / %USERPROFILE% / %LOCALAPPDATA% 之类的环境变量。</summary>
    public static string ExpandEnvironmentVariables(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        var result = path;
        foreach (var (token, replacement) in KnownTokens())
        {
            result = result.Replace(token, replacement, StringComparison.OrdinalIgnoreCase);
        }

        return result;
    }

    private static IEnumerable<(string Token, string Replacement)> KnownTokens()
    {
        var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        yield return ("%USER_DOCUMENTS%", documents);
        yield return ("%USERPROFILE%", userProfile);
        yield return ("%LOCALAPPDATA%", localAppData);
        yield return ("%APPDATA%", appData);
        yield return ("%DOCUMENTS%", documents);
        yield return ("\u0024{USER_DOCUMENTS}", documents);
        yield return ("\u0024{USERPROFILE}", userProfile);
    }

    private string ResolveGameDataDir(JsonElement root, string installRoot)
    {
        var raw = GetString(root, "gameDataPath");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TrimTrailingSeparators(installRoot);
        }

        var expanded = ExpandEnvironmentVariables(raw).Replace('/', '\\').TrimEnd('\\');
        if (Path.IsPathRooted(expanded))
        {
            return TrimTrailingSeparators(expanded);
        }

        return TrimTrailingSeparators(_fileSystem.GetFullPath(_fileSystem.Combine(installRoot, expanded)));
    }

    private string ResolveModDir(JsonElement root, string gameDataDir)
    {
        var raw = GetString(root, "modPath");
        if (string.IsNullOrWhiteSpace(raw))
        {
            return TrimTrailingSeparators(_fileSystem.Combine(gameDataDir, "mod"));
        }

        var expanded = ExpandEnvironmentVariables(raw);
        var combined = _fileSystem.Combine(gameDataDir, expanded);
        return TrimTrailingSeparators(_fileSystem.GetFullPath(combined));
    }

    private static string ResolveRelative(IFileSystem fileSystem, string baseDirectory, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        var normalized = ExpandEnvironmentVariables(path).Replace('/', '\\');
        if (Path.IsPathRooted(normalized))
        {
            return TrimTrailingSeparators(fileSystem.GetFullPath(normalized));
        }

        return TrimTrailingSeparators(fileSystem.GetFullPath(fileSystem.Combine(baseDirectory, normalized)));
    }

    /// <summary>
    /// 去掉路径末尾多余的分隔符。GetFullPath 会把 "D:\\a\\game" 原样保留，
    /// 而 "D:\\a\\game\\" 这类带尾分隔符的结果会让字符串比较、去重、路径拼接出现
    /// 双分隔符或误判。盘符根（"C:\\"）必须保留尾部反斜杠，否则会退化成当前盘目录。
    /// </summary>
    private static string TrimTrailingSeparators(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return path;
        }

        var trimmed = path.TrimEnd('\\', '/');
        if (trimmed.Length == 0)
        {
            return path;
        }

        if (trimmed.Length == 2 && trimmed[1] == ':')
        {
            return trimmed + '\\';
        }

        return trimmed;
    }

    private static string? ResolveOptional(IFileSystem fileSystem, string baseDirectory, string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return ResolveRelative(fileSystem, baseDirectory, path);
    }

    private IReadOnlyList<AlternativeExecutable> ReadAlternatives(JsonElement root, string settingsDirectory)
    {
        if (!root.TryGetProperty("alternativeExecutables", out var alternatives) || alternatives.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<AlternativeExecutable>();
        }

        var results = new List<AlternativeExecutable>();
        foreach (var item in alternatives.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var exePath = GetString(item, "exePath");
            if (string.IsNullOrWhiteSpace(exePath))
            {
                continue;
            }

            var label = PickLabel(item);
            results.Add(new AlternativeExecutable(
                ResolveRelative(_fileSystem, settingsDirectory, exePath),
                ReadStringArray(item, "exeArgs"),
                label));
        }

        return results;
    }

    /// <summary>label 选择顺序：zh-hans → en → 第一个可用值 → 空串。</summary>
    private static string PickLabel(JsonElement item)
    {
        if (!item.TryGetProperty("label", out var label))
        {
            return string.Empty;
        }

        if (label.ValueKind == JsonValueKind.String)
        {
            return label.GetString() ?? string.Empty;
        }

        if (label.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        foreach (var preferred in new[] { "zh-hans", "en" })
        {
            foreach (var property in label.EnumerateObject())
            {
                if (string.Equals(property.Name, preferred, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    var text = property.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return text;
                    }
                }
            }
        }

        foreach (var property in label.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String)
            {
                var text = property.Value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var array) || array.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var results = new List<string>();
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var value = item.GetString();
                if (value is not null)
                {
                    results.Add(value);
                }
            }
        }

        return results;
    }

    private static string? GetString(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return value.GetString();
    }

    private static double? GetDouble(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
        {
            return number;
        }

        if (value.ValueKind == JsonValueKind.String
            && double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static bool GetBool(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
