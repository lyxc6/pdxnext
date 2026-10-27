namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// CS2 的播放集配置（<c>.cache\Mods\playset_config.json</c>）。
/// <para>
/// 真机实测结构：
/// <code>
/// {
///   "playsets": [{
///     "id": "c6b32019-...", "isOffdisk": false, "isImmutable": false, "isAutoSorted": false,
///     "createdAt": "...", "modifiedAt": "...", "modifiedWith": "Cities: Skylines II",
///     "presentation": { "name": "20250512", "thumbnailUrl": null },
///     "mods": [{ "source": "pdx_mods", "sourceId": "74238", "sourceVersion": "37", "isEnabled": true }]
///   }],
///   "configVersion": 1
/// }
/// </code>
/// </para>
/// <para>
/// 三个与 sqlite 播放集完全不同的地方，全部由这里的映射吸收：
/// </para>
/// <list type="number">
/// <item>播放集自带真 GUID（<c>id</c>），不需要像 EU5 那样从名字哈希。</item>
/// <item>mod 项只有 <c>source + sourceId</c>，没有位置字段 —— <b>数组下标就是加载顺序</b>。</item>
/// <item>激活的播放集写在顶层 <c>activePlaysetId</c>；官方在它为空时会整键删除。</item>
/// </list>
/// <para>
/// 解析与序列化都用 <see cref="JsonNode"/>，<b>只改已知节点、原样保留未知键</b>，
/// 这样游戏或官方启动器以后加字段时我们不会把它们抹掉。
/// </para>
/// </summary>
public sealed class PlaysetConfigDocument
{
    /// <summary>mod 的来源标记（CS2 只有这一种：PDX Mods 平台）。</summary>
    public const string ModSourcePdxMods = "pdx_mods";

    /// <summary>本地手工安装的 mod 来源标记。</summary>
    public const string ModSourceLocal = "local";

    /// <summary>当前已知的文档版本。</summary>
    public const int DefaultConfigVersion = 1;

    private const string KeyConfigVersion = "configVersion";
    private const string KeyPlaySets = "playsets";
    private const string KeyActivePlaySetId = "activePlaysetId";
    private const string KeyId = "id";
    private const string KeyIsOffdisk = "isOffdisk";
    private const string KeyIsImmutable = "isImmutable";
    private const string KeyIsAutoSorted = "isAutoSorted";
    private const string KeyCreatedAt = "createdAt";
    private const string KeyModifiedAt = "modifiedAt";
    private const string KeyModifiedWith = "modifiedWith";
    private const string KeyPresentation = "presentation";
    private const string KeyName = "name";
    private const string KeyThumbnailUrl = "thumbnailUrl";
    private const string KeyMods = "mods";
    private const string KeySource = "source";
    private const string KeySourceId = "sourceId";
    private const string KeySourceVersion = "sourceVersion";
    private const string KeyIsEnabled = "isEnabled";

    /// <summary>
    /// 写文件用的序列化选项：紧凑、不转义中文、不转义非 ASCII。
    /// <para>
    /// 与官方一致：C# 侧（PDX.SDK）写出来的就是紧凑 JSON，本机现存的 3599 字节文件确实没有换行；
    /// 官方 Electron 启动器用的是 2 空格缩进。两种格式彼此都能读，我们跟随游戏侧（紧凑）。
    /// </para>
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonObject _root;

    private PlaysetConfigDocument(JsonObject root)
    {
        _root = root;
    }

    /// <summary>全部播放集节点（顺序即文件里的顺序）。</summary>
    public IReadOnlyList<JsonObject> PlaySets
    {
        get
        {
            if (_root[KeyPlaySets] is not JsonArray array)
            {
                return Array.Empty<JsonObject>();
            }

            var result = new List<JsonObject>(array.Count);
            foreach (var node in array)
            {
                if (node is JsonObject playSet)
                {
                    result.Add(playSet);
                }
            }

            return result;
        }
    }

    /// <summary>顶层 <c>activePlaysetId</c>；没有该键（本机现状）或值非法时返回 null。</summary>
    public Guid? ActivePlaySetId =>
        GetString(_root, KeyActivePlaySetId) is { Length: > 0 } text && Guid.TryParse(text, out var id) ? id : null;

    /// <summary>新建一份空文档。</summary>
    public static PlaysetConfigDocument CreateNew() =>
        new(new JsonObject
        {
            [KeyConfigVersion] = DefaultConfigVersion,
            [KeyPlaySets] = new JsonArray(),
        });

    /// <summary>解析已有文本；根不是对象时抛 <see cref="JsonException"/>。</summary>
    public static PlaysetConfigDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (node is not JsonObject root)
        {
            throw new JsonException("playset_config.json 的根节点不是对象。");
        }

        return new PlaysetConfigDocument(root);
    }

    /// <summary>序列化为文本（紧凑、无 BOM）。</summary>
    public string Serialize() => _root.ToJsonString(WriteOptions);

    /// <summary>按 GUID 取播放集节点；找不到返回 null。</summary>
    public JsonObject? FindPlaySet(Guid playSetId)
    {
        foreach (var playSet in PlaySets)
        {
            if (GetPlaySetId(playSet) == playSetId)
            {
                return playSet;
            }
        }

        return null;
    }

    /// <summary>按显示名取播放集节点；找不到返回 null。</summary>
    public JsonObject? FindPlaySet(string name) =>
        PlaySets.FirstOrDefault(ps => string.Equals(GetName(ps), name, StringComparison.Ordinal));

    /// <summary>追加一个播放集节点并返回它。</summary>
    public JsonObject AddPlaySet(Guid id, string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var playSet = new JsonObject
        {
            [KeyId] = id.ToString("D"),
            [KeyIsOffdisk] = false,
            [KeyIsImmutable] = false,
            [KeyIsAutoSorted] = false,
            [KeyCreatedAt] = now.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            [KeyModifiedAt] = now.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture),
            [KeyModifiedWith] = "PdxNext",
            [KeyPresentation] = new JsonObject { [KeyName] = name },
            [KeyMods] = new JsonArray(),
        };

        if (_root[KeyPlaySets] is not JsonArray array)
        {
            array = new JsonArray();
            _root[KeyPlaySets] = array;
        }

        array.Add(playSet);
        return playSet;
    }

    /// <summary>删除播放集节点；返回是否真的删掉了。</summary>
    public bool RemovePlaySet(JsonObject playSet)
    {
        if (_root[KeyPlaySets] is not JsonArray array)
        {
            return false;
        }

        var index = array.IndexOf(playSet);
        if (index < 0)
        {
            return false;
        }

        array.RemoveAt(index);
        return true;
    }

    /// <summary>
    /// 设置激活播放集。<paramref name="playSetId"/> 为 null 时删除整键
    /// （官方在 <c>activePlaysetId === undefined</c> 时也是 delete，不是写 null）。
    /// </summary>
    public void SetActivePlaySetId(Guid? playSetId)
    {
        if (playSetId is { } id)
        {
            _root[KeyActivePlaySetId] = id.ToString("D");
        }
        else
        {
            _root.Remove(KeyActivePlaySetId);
        }
    }

    /// <summary>更新播放集的修改时间与修改者。</summary>
    public static void Touch(JsonObject playSet, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(playSet);
        playSet[KeyModifiedAt] = now.UtcDateTime.ToString("o", System.Globalization.CultureInfo.InvariantCulture);
        playSet[KeyModifiedWith] = "PdxNext";
    }

    /// <summary>播放集 id；缺失或非法时返回 <see cref="Guid.Empty"/>。</summary>
    public static Guid GetPlaySetId(JsonObject playSet) =>
        Guid.TryParse(GetString(playSet, KeyId), out var id) ? id : Guid.Empty;

    /// <summary>播放集 id 文本（写回时用），缺失返回空串。</summary>
    public static string GetPlaySetIdText(JsonObject playSet) => GetString(playSet, KeyId);

    /// <summary>播放集显示名：<c>presentation.name</c>。</summary>
    public static string GetName(JsonObject playSet) =>
        playSet[KeyPresentation] is JsonObject presentation ? GetString(presentation, KeyName) : string.Empty;

    /// <summary>设置播放集显示名（只改 <c>presentation.name</c>，其它 presentation 字段原样保留）。</summary>
    public static void SetName(JsonObject playSet, string name)
    {
        ArgumentNullException.ThrowIfNull(playSet);
        if (playSet[KeyPresentation] is not JsonObject presentation)
        {
            presentation = new JsonObject();
            playSet[KeyPresentation] = presentation;
        }

        presentation[KeyName] = name;
    }

    /// <summary>播放集的 mod 数组；必要时创建。</summary>
    public static JsonArray EnsureMods(JsonObject playSet) => EnsureArray(playSet, KeyMods);

    /// <summary>读取 mod 项的 source（缺失时按 pdx_mods）。</summary>
    public static string GetModSource(JsonObject mod)
    {
        var source = GetString(mod, KeySource);
        return source.Length == 0 ? ModSourcePdxMods : source;
    }

    /// <summary>读取 mod 项的 sourceId（PDX Mods 的数字 id 字符串）。</summary>
    public static string GetModSourceId(JsonObject mod) => GetString(mod, KeySourceId);

    /// <summary>读取 mod 项的 sourceVersion；缺失返回 null（官方在 keepLatest 时不写这个键）。</summary>
    public static string? GetModSourceVersion(JsonObject mod)
    {
        var version = GetString(mod, KeySourceVersion);
        return version.Length == 0 ? null : version;
    }

    /// <summary>读取 mod 项的启用状态（缺失按 true，与官方默认一致）。</summary>
    public static bool GetModEnabled(JsonObject mod) => GetBool(mod, KeyIsEnabled, fallback: true);

    /// <summary>读取字符串字段；缺失或类型不对返回空串。</summary>
    public static string GetString(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    /// <summary>读取布尔字段；缺失或类型不对时用 <paramref name="fallback"/>。</summary>
    public static bool GetBool(JsonObject node, string key, bool fallback)
    {
        if (node[key] is not JsonValue value)
        {
            return fallback;
        }

        if (value.TryGetValue<bool>(out var flag))
        {
            return flag;
        }

        // 对 "true"/"1" 也宽容一点，别因为一个字符串把整条播放集读废。
        if (value.TryGetValue<string>(out var text))
        {
            return string.Equals(text, "true", StringComparison.OrdinalIgnoreCase)
                || string.Equals(text, "1", StringComparison.Ordinal);
        }

        return fallback;
    }

    /// <summary>取（必要时创建）指定键的数组。</summary>
    public static JsonArray EnsureArray(JsonObject node, string key)
    {
        if (node[key] is JsonArray array)
        {
            return array;
        }

        array = new JsonArray();
        node[key] = array;
        return array;
    }
}
