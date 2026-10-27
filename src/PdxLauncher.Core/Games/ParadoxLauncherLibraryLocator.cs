namespace PdxLauncher.Core.Games;

using System.Text.Json;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 定位官方 Paradox 启动器的"独立游戏库"根目录（不经 Steam 安装的游戏也放在这里）。
/// 数据来源是官方启动器的用户配置
/// <c>%APPDATA%\Paradox Interactive\launcher-v2\userSettings.json</c> 中的
/// <c>gameLibraryPaths</c> 数组：
/// <list type="bullet">
/// <item><description>字符串条目 = 库根目录；相对路径按官方启动器目录
/// （<c>%LOCALAPPDATA%\Programs\Paradox Interactive\launcher</c>）解析，官方默认值是 <c>../games</c>。</description></item>
/// <item><description>对象条目 = 取其中的 <c>installationPath</c>（可能直接指向某个游戏的安装根）。</description></item>
/// </list>
/// 除配置外还会尝试官方默认库目录 <c>&lt;启动器目录&gt;\..\games</c>。
/// 只返回真实存在的目录，按 <see cref="StringComparer.OrdinalIgnoreCase"/> 去重；
/// 本类只读，不修改官方配置，任何解析失败都降级为空列表而不抛异常。
/// </summary>
public sealed class ParadoxLauncherLibraryLocator
{
    private const string LauncherFolderName = "launcher";
    private const string DefaultGamesFolderName = "games";
    private const string LibraryPathsProperty = "gameLibraryPaths";
    private const string InstallationPathProperty = "installationPath";

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly string _launcherRoot;
    private readonly string _userSettingsPath;

    /// <summary>用本机默认位置（%APPDATA% / %LOCALAPPDATA%）构造。</summary>
    public ParadoxLauncherLibraryLocator(IFileSystem fileSystem, IAppLogger logger)
        : this(fileSystem, logger, userSettingsPath: null, launcherRoot: null)
    {
    }

    /// <summary>构造；可注入配置与启动器路径（自检用）。</summary>
    public ParadoxLauncherLibraryLocator(
        IFileSystem fileSystem,
        IAppLogger logger,
        string? userSettingsPath,
        string? launcherRoot)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        _launcherRoot = Normalize(string.IsNullOrWhiteSpace(launcherRoot)
            ? _fileSystem.Combine(localAppData, "Programs", "Paradox Interactive", LauncherFolderName)
            : launcherRoot);

        _userSettingsPath = Normalize(string.IsNullOrWhiteSpace(userSettingsPath)
            ? _fileSystem.Combine(appData, "Paradox Interactive", "launcher-v2", "userSettings.json")
            : userSettingsPath);
    }

    /// <summary>官方启动器的默认游戏库根：<c>%LOCALAPPDATA%\Programs\Paradox Interactive\games</c>。</summary>
    public string DefaultGamesRoot => Normalize(_fileSystem.Combine(_launcherRoot, "..", DefaultGamesFolderName));

    /// <summary>
    /// 返回全部可用的独立游戏库根目录（已去重、只含存在的目录）；配置缺失或损坏时返回空列表。
    /// </summary>
    public IReadOnlyList<string> FindLibraries()
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in ReadConfiguredPaths())
        {
            AddIfDirectoryExists(results, seen, Resolve(configured));
        }

        AddIfDirectoryExists(results, seen, DefaultGamesRoot);
        return results;
    }

    /// <summary>读出 <c>gameLibraryPaths</c> 里的原始条目（未解析）；失败返回空列表。</summary>
    private IReadOnlyList<string> ReadConfiguredPaths()
    {
        var results = new List<string>();

        if (!_fileSystem.FileExists(_userSettingsPath))
        {
            _logger.Info("官方启动器配置不存在，跳过独立游戏库: " + _userSettingsPath);
            return results;
        }

        string text;
        try
        {
            text = _fileSystem.ReadAllText(_userSettingsPath);
        }
        catch (IOException ex)
        {
            _logger.Warn("读取官方启动器配置失败: " + _userSettingsPath + " (" + ex.Message + ")");
            return results;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取官方启动器配置: " + _userSettingsPath + " (" + ex.Message + ")");
            return results;
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
                _logger.Warn("官方启动器配置根节点不是对象，跳过独立游戏库: " + _userSettingsPath);
                return results;
            }

            if (!root.TryGetProperty(LibraryPathsProperty, out var paths) || paths.ValueKind != JsonValueKind.Array)
            {
                _logger.Info("官方启动器配置里没有 gameLibraryPaths，跳过独立游戏库: " + _userSettingsPath);
                return results;
            }

            foreach (var entry in paths.EnumerateArray())
            {
                switch (entry.ValueKind)
                {
                    case JsonValueKind.String:
                        {
                            var value = entry.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                results.Add(value);
                            }

                            break;
                        }

                    case JsonValueKind.Object:
                        {
                            if (entry.TryGetProperty(InstallationPathProperty, out var pathElement)
                                && pathElement.ValueKind == JsonValueKind.String)
                            {
                                var value = pathElement.GetString();
                                if (!string.IsNullOrWhiteSpace(value))
                                {
                                    results.Add(value);
                                }
                            }

                            break;
                        }

                    default:
                        _logger.Info("忽略无法识别的 gameLibraryPaths 条目: " + entry.ValueKind);
                        break;
                }
            }
        }
        catch (JsonException ex)
        {
            _logger.Warn("官方启动器配置解析失败，跳过独立游戏库: " + _userSettingsPath + " (" + ex.Message + ")");
            return new List<string>();
        }

        return results;
    }

    /// <summary>把库路径条目解析成绝对路径：展开环境变量，相对路径按官方启动器目录解析。</summary>
    private string Resolve(string path)
    {
        var expanded = LauncherSettingsReader.ExpandEnvironmentVariables(path.Trim());
        if (!Path.IsPathRooted(expanded))
        {
            expanded = _fileSystem.Combine(_launcherRoot, expanded);
        }

        return Normalize(expanded);
    }

    /// <summary>只要目录存在且未出现过就收录。</summary>
    private void AddIfDirectoryExists(List<string> results, HashSet<string> seen, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        if (!_fileSystem.DirectoryExists(path))
        {
            _logger.Info("独立游戏库路径不存在，跳过: " + path);
            return;
        }

        if (seen.Add(path))
        {
            results.Add(path);
        }
    }

    private string Normalize(string path)
    {
        try
        {
            return _fileSystem.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            _logger.Warn("无效的路径: " + path + " (" + ex.Message + ")");
            return path;
        }
    }
}
