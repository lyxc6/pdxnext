namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Parsing;

/// <summary>
/// 读取 Steam 的 <c>appmanifest_&lt;appid&gt;.acf</c>（Valve KeyValues 格式），
/// 取出 appid / name / installdir / StateFlags / SizeOnDisk。坏文件跳过并记 Warn，不抛异常。
/// </summary>
public sealed class AppManifestReader
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    public AppManifestReader(IFileSystem fileSystem, IAppLogger logger)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 读取某个库里指定 appid 的清单；不存在或解析失败返回 null。
    /// </summary>
    public AppManifest? ReadForApp(string libraryRoot, int appId)
    {
        ArgumentNullException.ThrowIfNull(libraryRoot);
        var path = _fileSystem.Combine(
            libraryRoot,
            "steamapps",
            "appmanifest_" + appId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".acf");
        return Read(path);
    }

    /// <summary>
    /// 读取一个 .acf 文件；不存在或解析失败返回 null。
    /// </summary>
    public AppManifest? Read(string manifestPath)
    {
        ArgumentNullException.ThrowIfNull(manifestPath);
        if (!_fileSystem.FileExists(manifestPath))
        {
            return null;
        }

        try
        {
            var root = ValveKeyValuesParser.ParseFile(_fileSystem, manifestPath);
            var appState = root.FindChild("AppState") ?? root;
            var appIdText = appState.GetValue("appid");
            if (!int.TryParse(appIdText, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var appId))
            {
                _logger.Warn("appmanifest 缺少可用的 appid，跳过: " + manifestPath);
                return null;
            }

            var installDir = appState.GetValue("installdir") ?? string.Empty;
            if (installDir.Length == 0)
            {
                _logger.Warn("appmanifest 缺少 installdir，跳过: " + manifestPath);
                return null;
            }

            var libraryRoot = _fileSystem.GetDirectoryName(_fileSystem.GetDirectoryName(manifestPath) ?? string.Empty) ?? string.Empty;
            var installPath = libraryRoot.Length == 0
                ? installDir
                : _fileSystem.Combine(libraryRoot, "steamapps", "common", installDir);

            return new AppManifest(
                appId,
                appState.GetValue("name") ?? installDir,
                installDir,
                installPath,
                ParseInt(appState.GetValue("StateFlags")),
                ParseLong(appState.GetValue("SizeOnDisk")));
        }
        catch (IOException ex)
        {
            _logger.Warn("读取 appmanifest 失败，跳过: " + manifestPath + " (" + ex.Message + ")");
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("无权读取 appmanifest，跳过: " + manifestPath + " (" + ex.Message + ")");
            return null;
        }
    }

    /// <summary>
    /// 读取一个库里全部 <c>appmanifest_*.acf</c>。解析失败的条目被跳过。
    /// </summary>
    public IReadOnlyList<AppManifest> ReadAll(string libraryRoot)
    {
        ArgumentNullException.ThrowIfNull(libraryRoot);
        var steamApps = _fileSystem.Combine(libraryRoot, "steamapps");
        if (!_fileSystem.DirectoryExists(steamApps))
        {
            return Array.Empty<AppManifest>();
        }

        var results = new List<AppManifest>();
        foreach (var path in _fileSystem.EnumerateFiles(steamApps, "appmanifest_*.acf", recursive: false))
        {
            var manifest = Read(path);
            if (manifest is not null)
            {
                results.Add(manifest);
            }
        }

        return results;
    }

    private static int ParseInt(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static long ParseLong(string? text) =>
        long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0L;
}
