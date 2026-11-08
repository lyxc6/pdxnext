namespace PdxLauncher.Core.Playsets.Share;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// 官方启动器「分享播放集」导出的一个 mod 条目。
/// <para>
/// 字段与官方 <c>fromPlayset</c> 的序列化结果一一对应：Steam 系 mod 写 <c>steamId</c>，
/// PDX Mods 写 <c>pdxId</c>；只有 PDX Mods 被锁定版本时才多一个 <c>version</c>。
/// 官方导入端允许未知字段，所以这里只声明已知字段即可。
/// </para>
/// </summary>
/// <param name="DisplayName">mod 显示名（仅展示用，导入端不靠它匹配）。</param>
/// <param name="Enabled">导入后是否启用。</param>
/// <param name="Position">加载顺序；官方按它升序排，允许跳号。</param>
/// <param name="SteamId">Steam 创意工坊物品 id；与 <paramref name="PdxId"/> 二选一。</param>
/// <param name="PdxId">PDX Mods 平台的数字 id；与 <paramref name="SteamId"/> 二选一。</param>
/// <param name="Version">PDX Mods 锁定版本号；跟随最新版时为 null（官方不写这个键）。</param>
public sealed record PlaySetShareMod(
    string DisplayName,
    bool Enabled,
    int Position,
    string? SteamId = null,
    string? PdxId = null,
    string? Version = null);

/// <summary>
/// 官方启动器的播放集分享文件：<c>{"game":"ck3","name":"1.18","mods":[...]}</c>。
/// <para>
/// 本启动器导出的文件与官方 2026.13 版本的 <c>getPlaysetToExport</c> 输出逐字段一致：
/// 紧凑单行 JSON、顶层只有 <c>game</c> / <c>name</c> / <c>mods</c>，本地 mod 不导出，
/// 不含 DLC 启用状态、播放集 id、启动器版本等元数据。
/// </para>
/// </summary>
public sealed record PlaySetShareDocument(
    string Game,
    string Name,
    IReadOnlyList<PlaySetShareMod> Mods)
{
    /// <summary>官方保存对话框里的扩展名（带点）。</summary>
    public const string FileExtension = ".json";

    /// <summary>解析失败：不是合法 JSON。</summary>
    public const string ErrorInvalidJson = "share-invalid-json";

    /// <summary>解析失败：缺少 game / name / mods 之一。</summary>
    public const string ErrorMissingField = "share-missing-field";

    /// <summary>解析失败：mods 里有一条既没有 steamId 也没有 pdxId。</summary>
    public const string ErrorInvalidMod = "share-invalid-mod";

    private static readonly JsonSerializerOptions Options = new()
    {
        // 官方是 JSON.stringify 输出：紧凑、无缩进、中文不转义、null 字段整个省略。
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>按官方格式序列化（紧凑单行）。</summary>
    public string ToJson() => JsonSerializer.Serialize(this, Options);

    /// <summary>
    /// 解析官方分享文件。容错口径：
    /// <list type="bullet">
    /// <item>顶层必须是对象，且 <c>game</c> / <c>name</c> 为非空字符串、<c>mods</c> 为数组；</item>
    /// <item>mod 条目必须有 <c>steamId</c> 或 <c>pdxId</c>；<c>displayName</c> / <c>enabled</c> / <c>position</c>
    /// 缺失时分别退化为 id / true / 条目下标，不让手写文件因为少一个可选字段直接失败；</item>
    /// <item>未知字段一律忽略（官方 schema 也是 additionalProperties）。</item>
    /// </list>
    /// </summary>
    /// <param name="json">文件原文。</param>
    /// <param name="errorCode">失败时的稳定错误码（<see cref="ErrorInvalidJson"/> 等）。</param>
    /// <returns>解析结果；失败时为 null。</returns>
    public static PlaySetShareDocument? TryParse(string json, out string? errorCode)
    {
        errorCode = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            errorCode = ErrorInvalidJson;
            return null;
        }

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            errorCode = ErrorInvalidJson;
            return null;
        }

        if (root is not JsonObject rootObject)
        {
            errorCode = ErrorInvalidJson;
            return null;
        }

        var game = ReadString(rootObject, "game");
        var name = ReadString(rootObject, "name");
        if (game is null)
        {
            errorCode = ErrorMissingField + ":game";
            return null;
        }

        if (name is null)
        {
            errorCode = ErrorMissingField + ":name";
            return null;
        }

        if (rootObject["mods"] is not JsonArray modsArray)
        {
            errorCode = ErrorMissingField + ":mods";
            return null;
        }

        var mods = new List<PlaySetShareMod>(modsArray.Count);
        for (var index = 0; index < modsArray.Count; index++)
        {
            if (modsArray[index] is not JsonObject modObject)
            {
                errorCode = ErrorInvalidMod + ":" + index;
                return null;
            }

            var steamId = ReadString(modObject, "steamId");
            var pdxId = ReadString(modObject, "pdxId");
            if (steamId is null && pdxId is null)
            {
                errorCode = ErrorInvalidMod + ":" + index;
                return null;
            }

            var displayName = ReadString(modObject, "displayName") ?? steamId ?? pdxId!;
            var enabled = ReadBool(modObject, "enabled") ?? true;
            var position = ReadInt(modObject, "position") ?? index;
            var version = ReadString(modObject, "version");

            mods.Add(new PlaySetShareMod(displayName, enabled, position, steamId, pdxId, version));
        }

        return new PlaySetShareDocument(game, name, mods);
    }

    private static string? ReadString(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node is null)
        {
            return null;
        }

        if (node is JsonValue value)
        {
            if (value.TryGetValue<string>(out var text))
            {
                var trimmed = text.Trim();
                return trimmed.Length == 0 ? null : trimmed;
            }

            // 官方 schema 要求字符串；但手写文件里把 id 写成数字很常见，按文本接受。
            if (value.TryGetValue<long>(out var number))
            {
                return number.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    private static bool? ReadBool(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node is not JsonValue value)
        {
            return null;
        }

        return value.TryGetValue<bool>(out var flag) ? flag : null;
    }

    private static int? ReadInt(JsonObject obj, string key)
    {
        var node = obj[key];
        if (node is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<int>(out var number))
        {
            return number;
        }

        if (value.TryGetValue<double>(out var real) && real is >= int.MinValue and <= int.MaxValue)
        {
            return (int)real;
        }

        return null;
    }
}
