namespace PdxLauncher.Core.Playsets.Json;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// EU5 的播放集文档：文档目录下的 <c>playsets.json</c>（<c>file_version: "1.0.0"</c>）。
/// <para>
/// 与 v2 系的关键差异：播放集没有 id（<c>name</c> 才是身份）、mod 用绝对目录路径引用、
/// DLC 挂在播放集上、游戏本体直接读这个文件（没有 dlc_load.json / content_load.json）。
/// </para>
/// <para>
/// 解析与序列化都用 <see cref="JsonNode"/>，**只改已知节点、原样保留未知键**，
/// 这样游戏或官方启动器以后加字段时我们不会把它们抹掉。
/// </para>
/// </summary>
public sealed class PlaysetsJsonDocument
{
    /// <summary>文件名（游戏固定读这个位置）。</summary>
    public const string FileName = "playsets.json";

    /// <summary>当前已知的文档版本。</summary>
    public const string DefaultFileVersion = "1.0.0";

    private const string KeyFileVersion = "file_version";
    private const string KeyPlaySets = "playsets";
    private const string KeyName = "name";
    private const string KeyIsActive = "isActive";
    private const string KeyIsAutomaticallySorted = "isAutomaticallySorted";
    private const string KeyOrderedListMods = "orderedListMods";
    private const string KeyDlc = "DLC";

    /// <summary>
    /// 序列化选项：2 空格缩进、不转义中文（官方文件本身是 UTF-8 原文）。
    /// 注意：官方写出来的是 JsonCpp 的制表符风格，我们写标准 JSON——游戏解析器不受影响。
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        IndentCharacter = ' ',
        IndentSize = 2,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly JsonObject _root;

    private PlaysetsJsonDocument(JsonObject root)
    {
        _root = root;
    }

    /// <summary>文档版本；缺失时返回 <see cref="DefaultFileVersion"/>。</summary>
    public string FileVersion =>
        _root[KeyFileVersion] is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text)
            ? text
            : DefaultFileVersion;

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

    /// <summary>新建一份空文档（只有 file_version 与空的 playsets）。</summary>
    public static PlaysetsJsonDocument CreateNew() =>
        new(new JsonObject
        {
            [KeyFileVersion] = DefaultFileVersion,
            [KeyPlaySets] = new JsonArray(),
        });

    /// <summary>解析已有文本；根不是对象时抛 <see cref="JsonException"/>。</summary>
    public static PlaysetsJsonDocument Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);

        var node = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
        });

        if (node is not JsonObject root)
        {
            throw new JsonException("playsets.json 的根节点不是对象。");
        }

        return new PlaysetsJsonDocument(root);
    }

    /// <summary>序列化为文本。</summary>
    public string Serialize() => _root.ToJsonString(WriteOptions);

    /// <summary>按名字取播放集节点；找不到返回 null。</summary>
    public JsonObject? FindPlaySet(string name) =>
        PlaySets.FirstOrDefault(ps => string.Equals(GetString(ps, KeyName), name, StringComparison.Ordinal));

    /// <summary>追加一个播放集节点并返回它。</summary>
    public JsonObject AddPlaySet(string name, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var playSet = new JsonObject
        {
            [KeyName] = name,
            [KeyIsActive] = isActive,
            [KeyIsAutomaticallySorted] = false,
            [KeyOrderedListMods] = new JsonArray(),
            [KeyDlc] = new JsonArray(),
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

        // 游戏与官方启动器都写 JSON 布尔；这里对 "true"/"1" 也宽容一点，别因为一个字符串把整条播放集读废。
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

    /// <summary>取（必要时创建）子对象。</summary>
    public static JsonObject EnsureObject(JsonObject node, string key)
    {
        if (node[key] is JsonObject child)
        {
            return child;
        }

        child = new JsonObject();
        node[key] = child;
        return child;
    }
}
