namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// CS2 的已安装 mod 元数据缓存（<c>.cache\Mods\pdx_mods_cache.json</c>）。
/// <para>
/// 真机结构：<c>{ "items": [ {...} ], "configVersion": 1 }</c>，每个 item 的关键字段：
/// <c>source / id / version / userModVersion / latestVersion / displayName / author / requiredGameVersion /
/// shortDescription / repositoryPath / rating / subscriptionsTotal / size / thumbnailPath / tags / dependencies</c>。
/// </para>
/// <para>
/// 这个文件是官方启动器与游戏自己维护的<b>缓存</b>，只读不改 —— 我们需要的显示名/版本/封面全在里面，
/// 但权威的「哪些 mod 在哪个播放集里」仍以 <c>playset_config.json</c> 为准。
/// 缺失或损坏时返回空字典，调用方回落到目录名，绝不因此让整页报错。
/// </para>
/// </summary>
public sealed class PdxModsCache
{
    private const string KeyItems = "items";
    private const string KeySource = "source";
    private const string KeyId = "id";
    private const string KeyVersion = "version";
    private const string KeyLatestVersion = "latestVersion";
    private const string KeyUserModVersion = "userModVersion";
    private const string KeyDisplayName = "displayName";
    private const string KeyName = "name";
    private const string KeyAuthor = "author";
    private const string KeyRequiredGameVersion = "requiredGameVersion";
    private const string KeyShortDescription = "shortDescription";
    private const string KeySize = "size";
    private const string KeyThumbnailPath = "thumbnailPath";
    private const string KeySubscriptionsTotal = "subscriptionsTotal";
    private const string KeyRating = "rating";
    private const string KeyInstalledDate = "installedDate";
    private const string KeyState = "state";

    private readonly Dictionary<string, PdxModsCacheItem> _byId;

    private PdxModsCache(Dictionary<string, PdxModsCacheItem> byId)
    {
        _byId = byId;
    }

    /// <summary>空缓存（文件缺失/损坏时用）。</summary>
    public static PdxModsCache Empty { get; } = new(new Dictionary<string, PdxModsCacheItem>(StringComparer.Ordinal));

    /// <summary>全部条目，按 id 排序（稳定输出，便于自检）。</summary>
    public IReadOnlyCollection<PdxModsCacheItem> Items =>
        _byId.Values.OrderBy(static item => item.Id, StringComparer.Ordinal).ToArray();

    /// <summary>按 PDX Mods 的数字 id 取条目。</summary>
    public PdxModsCacheItem? Find(string? sourceId) =>
        sourceId is not null && _byId.TryGetValue(sourceId, out var item) ? item : null;

    /// <summary>从文件读；不存在或解析失败返回 <see cref="Empty"/> 并记 Warn。</summary>
    public static PdxModsCache Load(IFileSystem fileSystem, IAppLogger logger, string path)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        if (!fileSystem.FileExists(path))
        {
            return Empty;
        }

        string text;
        try
        {
            text = fileSystem.ReadAllText(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.Warn("读取 pdx_mods_cache.json 失败: " + path + " (" + ex.Message + ")");
            return Empty;
        }

        return Parse(text, logger, path);
    }

    /// <summary>从文本解析。</summary>
    public static PdxModsCache Parse(string json, IAppLogger logger, string origin = "pdx_mods_cache.json")
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });

            if (node is not JsonObject root || root[KeyItems] is not JsonArray items)
            {
                logger.Warn(origin + " 缺少 items 数组，按空缓存处理。");
                return Empty;
            }

            var byId = new Dictionary<string, PdxModsCacheItem>(StringComparer.Ordinal);
            foreach (var entry in items)
            {
                if (entry is not JsonObject item)
                {
                    continue;
                }

                var id = PlaysetConfigDocument.GetString(item, KeyId);
                if (id.Length == 0 || byId.ContainsKey(id))
                {
                    continue;
                }

                byId[id] = new PdxModsCacheItem(
                    Id: id,
                    Source: PlaysetConfigDocument.GetString(item, KeySource) is { Length: > 0 } source
                        ? source
                        : PlaysetConfigDocument.ModSourcePdxMods,
                    Version: PlaysetConfigDocument.GetString(item, KeyVersion),
                    LatestVersion: PlaysetConfigDocument.GetString(item, KeyLatestVersion),
                    UserModVersion: PlaysetConfigDocument.GetString(item, KeyUserModVersion),
                    DisplayName: FirstNonEmpty(
                        PlaysetConfigDocument.GetString(item, KeyDisplayName),
                        PlaysetConfigDocument.GetString(item, KeyName)),
                    Author: PlaysetConfigDocument.GetString(item, KeyAuthor),
                    RequiredGameVersion: PlaysetConfigDocument.GetString(item, KeyRequiredGameVersion),
                    ShortDescription: PlaysetConfigDocument.GetString(item, KeyShortDescription),
                    SizeBytes: GetLong(item, KeySize),
                    ThumbnailPath: NullIfEmpty(PlaysetConfigDocument.GetString(item, KeyThumbnailPath)),
                    Subscriptions: GetLong(item, KeySubscriptionsTotal),
                    Rating: GetDouble(item, KeyRating),
                    InstalledOn: GetDate(item, KeyInstalledDate),
                    State: PlaysetConfigDocument.GetString(item, KeyState));
            }

            return new PdxModsCache(byId);
        }
        catch (JsonException ex)
        {
            logger.Warn(origin + " 解析失败，按空缓存处理: " + ex.Message);
            return Empty;
        }
    }

    /// <summary>
    /// 从 mod 目录名（<c>&lt;modId&gt;_&lt;version&gt;</c>）解析出 id 与版本；不符合命名规则时返回 null。
    /// </summary>
    public static (string Id, string Version)? ParseFolderName(string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return null;
        }

        var index = folderName.LastIndexOf('_');
        if (index <= 0 || index >= folderName.Length - 1)
        {
            return null;
        }

        var id = folderName[..index];
        var version = folderName[(index + 1)..];
        return id.All(char.IsAsciiDigit) && version.All(char.IsAsciiDigit) && id.Length > 0 && version.Length > 0
            ? (id, version)
            : null;
    }

    /// <summary>版本号比较（纯数字串）；任一无法解析返回 0。</summary>
    public static int CompareVersions(string? left, string? right)
    {
        if (!long.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out var a)
            || !long.TryParse(right, NumberStyles.Integer, CultureInfo.InvariantCulture, out var b))
        {
            return 0;
        }

        return a.CompareTo(b);
    }

    /// <summary>
    /// CS2 的 mod 在 UI 里的稳定 Id：由 (gameId, sourceId) 派生的确定性 Guid。
    /// <para>
    /// 播放集里存的是数字 <c>sourceId</c>（如 "74238"），而 <see cref="PdxLauncher.Core.Contracts.PlaySetMod.ModId"/>
    /// 是 <see cref="Guid"/>。派生规则必须稳定，否则每次扫描出来的 mod 都无法与播放集条目对上。
    /// 写回时一律用字典反查回 sourceId，<b>绝不把这个 Guid 写进游戏文件</b>。
    /// </para>
    /// </summary>
    public static Guid GetModId(string gameId, string sourceId)
    {
        var key = "pdxnext:pdxmod:" + (gameId ?? string.Empty) + ":" + (sourceId ?? string.Empty);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    private static string FirstNonEmpty(string first, string second) => first.Length > 0 ? first : second;

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    private static long? GetLong(JsonObject node, string key)
    {
        if (node[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<long>(out var number))
        {
            return number;
        }

        return value.TryGetValue<double>(out var floating) ? (long)floating : null;
    }

    private static double? GetDouble(JsonObject node, string key)
    {
        if (node[key] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<double>(out var floating))
        {
            return floating;
        }

        return value.TryGetValue<long>(out var number) ? number : null;
    }

    private static DateTimeOffset? GetDate(JsonObject node, string key)
    {
        var text = PlaysetConfigDocument.GetString(node, key);
        return DateTimeOffset.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var date)
            ? date
            : null;
    }
}

/// <summary><c>pdx_mods_cache.json</c> 里的一条已安装 mod 元数据。</summary>
/// <param name="Id">PDX Mods 的数字 id（字符串形式）。</param>
/// <param name="Source">来源标记，CS2 上恒为 <c>pdx_mods</c>。</param>
/// <param name="Version">当前安装的版本。</param>
/// <param name="LatestVersion">平台上的最新版本。</param>
/// <param name="UserModVersion">作者自己写的语义版本（如 1.1.0.5）。</param>
/// <param name="DisplayName">显示名（回落到 <c>name</c>）。</param>
/// <param name="Author">作者。</param>
/// <param name="RequiredGameVersion">要求的游戏版本（如 1.6.*）。</param>
/// <param name="ShortDescription">简介。</param>
/// <param name="SizeBytes">包大小。</param>
/// <param name="ThumbnailPath">封面 URL（modscontent CDN）。</param>
/// <param name="Subscriptions">订阅数。</param>
/// <param name="Rating">评分。</param>
/// <param name="InstalledOn">安装时间。</param>
/// <param name="State">平台状态（真机全为 published）。</param>
public sealed record PdxModsCacheItem(
    string Id,
    string Source,
    string Version,
    string LatestVersion,
    string UserModVersion,
    string DisplayName,
    string Author,
    string RequiredGameVersion,
    string ShortDescription,
    long? SizeBytes,
    string? ThumbnailPath,
    long? Subscriptions,
    double? Rating,
    DateTimeOffset? InstalledOn,
    string State);
