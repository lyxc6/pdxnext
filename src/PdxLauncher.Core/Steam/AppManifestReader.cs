namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Parsing;

/// <summary>
/// 读取 Steam 的 <c>appmanifest_&lt;appid&gt;.acf</c>（Valve KeyValues 格式），
/// 取出 appid / name / installdir / StateFlags / SizeOnDisk / LastOwner / DisabledDLC /
/// buildid / InstalledDepots。坏文件跳过并记 Warn，不抛异常。
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
                ParseLong(appState.GetValue("SizeOnDisk")),
                ParseLastOwner(appState.GetValue("LastOwner")),
                ParseDisabledDlc(appState.GetValue("DisabledDLC")),
                ParseInstalledDepots(appState.FindChild("InstalledDepots")),
                ParseNullableText(appState.GetValue("buildid")));
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

    /// <summary>
    /// <c>LastOwner</c>：最后一次写入这份清单的 SteamID64。官方启动器拿它跟当前账号比对，
    /// 只有一致时才相信 <c>DisabledDLC</c>（防止跨账号残留的旧数据）。
    /// </summary>
    private static string? ParseLastOwner(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();
        foreach (var ch in value)
        {
            if (ch < '0' || ch > '9')
            {
                return null;
            }
        }

        return value.Length == 0 ? null : value;
    }

    /// <summary><c>DisabledDLC</c>：逗号分隔的 appid 列表；没有这一项时返回 null。</summary>
    private static IReadOnlyList<string>? ParseDisabledDlc(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var items = new List<string>();
        foreach (var part in text.Split(','))
        {
            var trimmed = part.Trim();
            if (trimmed.Length != 0)
            {
                items.Add(trimmed);
            }
        }

        return items.Count == 0 ? null : items;
    }

    /// <summary>
    /// <c>InstalledDepots</c>：每个子块是 <c>"&lt;depotid&gt;" { "manifest" "…" "size" "…" ["dlcappid" "…"] }</c>。
    /// 段不存在时返回 <c>null</c>（＝「不知道」，不能据此断言任何 DLC 已装）；
    /// 段存在但为空返回空集合（＝「确实一个都没装」）。
    /// </summary>
    private static IReadOnlyList<AppDepot>? ParseInstalledDepots(ValveKeyValuesNode? block)
    {
        if (block is null || !block.IsBlock)
        {
            return null;
        }

        var results = new List<AppDepot>();
        foreach (var child in block.Children)
        {
            if (string.IsNullOrWhiteSpace(child.Key)
                || !int.TryParse(child.Key.Trim(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var depotId)
                || depotId <= 0)
            {
                continue;
            }

            int? dlcAppId = null;
            if (int.TryParse(
                    child.GetValue("dlcappid"),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsedDlcAppId) && parsedDlcAppId > 0)
            {
                dlcAppId = parsedDlcAppId;
            }

            results.Add(new AppDepot(depotId, dlcAppId, ParseLong(child.GetValue("size"))));
        }

        return results;
    }

    /// <summary>去掉首尾空白，空串返回 null（用于 <c>buildid</c> 这类可缺字段）。</summary>
    private static string? ParseNullableText(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    private static int ParseInt(string? text) =>
        int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0;

    private static long ParseLong(string? text) =>
        long.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : 0L;
}
