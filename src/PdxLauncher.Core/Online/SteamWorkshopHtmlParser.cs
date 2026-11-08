namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

/// <summary>
/// Steam 创意工坊列表页解析器（纯静态，可在自检里用内联 HTML 覆盖）。
/// <para>
/// 页面把数据塞在 <c>&lt;script type="application/json" id="valve-ssr-data"&gt;</c> 里：
/// </para>
/// <list type="number">
/// <item><c>props.pageProps.renderContext.queryData</c> —— 是<b>字符串</b>，需要二次 JSON 解析；</item>
/// <item>解析后 <c>queries[]</c>，每项 <c>state.data</c> 才是 <c>total_count</c>/<c>total_pages</c>/<c>results[]</c>。</item>
/// </list>
/// <para>
/// 解析失败一律返回 <c>null</c>（上层映射成「页面结构变了」），不抛异常 ——
/// Steam 前端改版是常态，不能让它把整个页面炸掉。
/// </para>
/// </summary>
public static class SteamWorkshopHtmlParser
{
    /// <summary>SSR 数据脚本的 id。</summary>
    public const string ScriptId = "valve-ssr-data";

    /// <summary>解析一页创意工坊列表。</summary>
    /// <param name="html">列表页 HTML。</param>
    /// <param name="page">页码（从 0 开始）。</param>
    /// <param name="pageSize">每页条数。</param>
    /// <returns>解析结果；页面结构不符时返回 null。</returns>
    public static SteamWorkshopPage? Parse(string? html, int page, int pageSize)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var json = ExtractScriptJson(html);
        if (json is null)
        {
            return null;
        }

        try
        {
            var root = JsonNode.Parse(json);

            // 实测两种形态都出现过：老页面在 props.pageProps.renderContext，
            // 2026-10 的页面把 renderContext 直接放在顶层。
            var renderContext = root?["props"]?["pageProps"]?["renderContext"] ?? root?["renderContext"];
            var node = renderContext?["queryData"];
            if (node is JsonValue value && value.TryGetValue<string>(out var text))
            {
                node = JsonNode.Parse(text);
            }

            if (node is not JsonObject queryData)
            {
                return null;
            }

            // 2026-11 起 queryData 里有多条 React Query 记录（workshop_browse / declared_tags_v6 /
            // workshop_required_dlc_v5 / StoreItem…），列表只是其中一条。先按 queryKey 定位，
            // 老页面没有 queryKey 时才退回原来的「找第一个带 results 的 payload」。
            var queries = CollectQueries(queryData);
            var payload = FindBrowsePayload(queries) ?? FindQueryPayload(queryData);
            if (payload is null)
            {
                return null;
            }

            var totalCount = GetInt(payload, "total_count");
            var totalPages = GetInt(payload, "total_pages");
            var items = new List<SteamWorkshopItem>();
            if (payload["results"] is JsonArray results)
            {
                foreach (var entry in results)
                {
                    if (entry is JsonObject item)
                    {
                        items.Add(ParseItem(item));
                    }
                }
            }

            if (totalPages <= 0 && pageSize > 0)
            {
                totalPages = totalCount <= 0 ? 0 : (int)Math.Ceiling(totalCount / (double)pageSize);
            }

            return new SteamWorkshopPage(
                items,
                totalCount,
                page,
                pageSize,
                totalPages,
                ParseFilterGroups(queries));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// 取出 <c>id="valve-ssr-data"</c> 那个 script 标签的正文。
    /// 属性顺序不保证，所以先找 id 出现的位置，再向前找 <c>&lt;script</c>、向后找 <c>&gt;</c>。
    /// </summary>
    private static string? ExtractScriptJson(string html)
    {
        var idIndex = html.IndexOf("id=\"" + ScriptId + "\"", StringComparison.OrdinalIgnoreCase);
        if (idIndex < 0)
        {
            idIndex = html.IndexOf(ScriptId, StringComparison.OrdinalIgnoreCase);
            if (idIndex < 0)
            {
                return null;
            }
        }

        var tagStart = html.LastIndexOf("<script", idIndex, StringComparison.OrdinalIgnoreCase);
        if (tagStart < 0)
        {
            return null;
        }

        var contentStart = html.IndexOf('>', tagStart);
        if (contentStart < 0)
        {
            return null;
        }

        contentStart++;
        var contentEnd = html.IndexOf("</script", contentStart, StringComparison.OrdinalIgnoreCase);
        if (contentEnd < 0)
        {
            return null;
        }

        var slice = html.AsSpan(contentStart, contentEnd - contentStart).Trim();
        return slice.IsEmpty ? null : slice.ToString();
    }

    /// <summary>在 queryData 里找出真正带 <c>results</c> 的那份 data。</summary>
    private static JsonObject? FindQueryPayload(JsonObject queryData)
    {
        if (HasResults(queryData))
        {
            return queryData;
        }

        var queries = queryData["queries"];
        switch (queries)
        {
            case JsonArray array:
                foreach (var entry in array)
                {
                    if (entry is JsonObject query && PickFromQuery(query) is { } hit)
                    {
                        return hit;
                    }
                }

                break;
            case JsonObject map:
                foreach (var pair in map)
                {
                    if (pair.Value is JsonObject query && PickFromQuery(query) is { } hit)
                    {
                        return hit;
                    }
                }

                break;
        }

        return null;
    }

    /// <summary>queryData 里的一条 React Query 记录：queryKey 与 data（data 可能是对象或数组）。</summary>
    private sealed record QueryEntry(IReadOnlyList<string> Key, JsonNode? Data);

    /// <summary>把 queryData.queries（数组或字典两种形态）摊平成统一列表。</summary>
    private static List<QueryEntry> CollectQueries(JsonObject queryData)
    {
        var list = new List<QueryEntry>();
        switch (queryData["queries"])
        {
            case JsonArray array:
                foreach (var entry in array)
                {
                    Add(entry);
                }

                break;
            case JsonObject map:
                foreach (var pair in map)
                {
                    Add(pair.Value);
                }

                break;
        }

        return list;

        void Add(JsonNode? node)
        {
            if (node is not JsonObject query)
            {
                return;
            }

            var key = ReadQueryKey(query["queryKey"]);
            var data = query["state"]?["data"] ?? query["data"];
            list.Add(new QueryEntry(key, data));
        }
    }

    /// <summary>queryKey 可能是 JSON 数组，也可能是被字符串化的 JSON 数组。</summary>
    private static IReadOnlyList<string> ReadQueryKey(JsonNode? node)
    {
        var array = node as JsonArray;
        if (array is null && node is JsonValue value && value.TryGetValue<string>(out var text))
        {
            try
            {
                array = JsonNode.Parse(text) as JsonArray;
            }
            catch (JsonException)
            {
                array = null;
            }
        }

        if (array is null)
        {
            return [];
        }

        return array
            .Select(static entry => entry is JsonValue keyValue && keyValue.TryGetValue<string>(out var text)
                ? text
                : entry?.ToJsonString().Trim('"') ?? string.Empty)
            .ToList();
    }

    /// <summary>找 <c>workshop_browse</c> 那条查询；找不到时退回任意带 results 的查询。</summary>
    private static JsonObject? FindBrowsePayload(List<QueryEntry> queries)
    {
        foreach (var query in queries)
        {
            if (query.Key.Count > 0
                && string.Equals(query.Key[0], "workshop_browse", StringComparison.OrdinalIgnoreCase)
                && query.Data is JsonObject data
                && HasResults(data))
            {
                return data;
            }
        }

        foreach (var query in queries)
        {
            if (query.Data is JsonObject data && data.ContainsKey("results"))
            {
                return data;
            }
        }

        return null;
    }

    /// <summary>
    /// 解析左侧筛选栏。数据来源（2026-11 实测）：
    /// <list type="bullet">
    /// <item><c>declared_tags_v6.readytouse_tags</c>：两组标签（类别、更新兼容性），值是标签名；</item>
    /// <item><c>workshop_required_dlc_v5</c>：<c>{appid,count}</c> 列表，名称在 <c>StoreItem/app_&lt;appid&gt;</c> 查询里；</item>
    /// <item>「显示不兼容的项目」不是筛选组，参数固定为 <c>requiredflags[]=incompatible</c>。</item>
    /// </list>
    /// 任何一步结构不符都只跳过该部分，绝不抛异常。
    /// </summary>
    private static IReadOnlyList<SteamWorkshopFilterGroup> ParseFilterGroups(List<QueryEntry> queries)
    {
        var groups = new List<SteamWorkshopFilterGroup>();

        var tagQuery = queries.FirstOrDefault(static query =>
            query.Key.Count > 0
            && string.Equals(query.Key[0], "declared_tags_v6", StringComparison.OrdinalIgnoreCase));
        if (tagQuery?.Data is JsonObject tagData && tagData["readytouse_tags"] is JsonArray tagGroups)
        {
            foreach (var node in tagGroups)
            {
                if (node is not JsonObject group || group["tags"] is not JsonArray tagArray)
                {
                    continue;
                }

                var title = GetString(group, "name")?.Trim() ?? string.Empty;
                var tags = new List<SteamWorkshopTag>();
                foreach (var tagNode in tagArray)
                {
                    if (tagNode is not JsonObject tag)
                    {
                        continue;
                    }

                    var value = GetString(tag, "name") ?? GetString(tag, "display_name");
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        continue;
                    }

                    var display = GetString(tag, "display_name");
                    tags.Add(new SteamWorkshopTag(
                        value!,
                        string.IsNullOrWhiteSpace(display) ? value! : display));
                }

                if (tags.Count == 0)
                {
                    continue;
                }

                var key = title.Length switch
                {
                    0 => "category",

                    // Steam 各游戏叫法不同：Update Compatibility / Compatible Version / Compatible …
                    // 只要标题里带 compatib 就归到这个组，界面才能显示本地化标题。
                    _ when title.Contains("compatib", StringComparison.OrdinalIgnoreCase) => "compatibility",
                    _ => "other:" + title,
                };
                groups.Add(new SteamWorkshopFilterGroup(UniquifyKey(groups, key), title, tags));
            }
        }

        if (TryParseDlcGroup(queries) is { } dlc)
        {
            groups.Add(dlc);
        }

        return groups;
    }

    /// <summary>解析「所需的 DLC」组：appid 列表 + StoreItem 里的名称。</summary>
    private static SteamWorkshopFilterGroup? TryParseDlcGroup(List<QueryEntry> queries)
    {
        var dlcQuery = queries.FirstOrDefault(static query =>
            query.Key.Count > 0
            && string.Equals(query.Key[0], "workshop_required_dlc_v5", StringComparison.OrdinalIgnoreCase));
        if (dlcQuery?.Data is not JsonArray dlcArray)
        {
            return null;
        }

        var names = new Dictionary<int, string>();
        foreach (var query in queries)
        {
            if (query.Key.Count < 3
                || !string.Equals(query.Key[0], "StoreItem", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(query.Key[2], "default_info", StringComparison.OrdinalIgnoreCase)
                || query.Data is not JsonObject store)
            {
                continue;
            }

            var appKey = query.Key[1];
            if (!appKey.StartsWith("app_", StringComparison.Ordinal)
                || !int.TryParse(appKey.AsSpan(4), NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            var name = GetString(store, "name");
            if (!string.IsNullOrWhiteSpace(name))
            {
                names[id] = name!;
            }
        }

        var appName = FindAppName(queries);
        var tags = new List<SteamWorkshopTag>();
        foreach (var node in dlcArray)
        {
            if (node is not JsonObject entry)
            {
                continue;
            }

            var id = (int)GetLong(entry, "appid");
            if (id <= 0)
            {
                continue;
            }

            var name = names.TryGetValue(id, out var found)
                ? found
                : id.ToString(CultureInfo.InvariantCulture);
            tags.Add(new SteamWorkshopTag(
                id.ToString(CultureInfo.InvariantCulture),
                StripGamePrefix(name, appName)));
        }

        if (tags.Count == 0)
        {
            return null;
        }

        tags.Sort(static (left, right) =>
            string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase));
        return new SteamWorkshopFilterGroup("dlc", "Required DLC", tags, IsDlc: true);
    }

    /// <summary>取游戏名，用来从 DLC 名称里去掉「Victoria 3: 」这类前缀（Steam 侧栏也是这么显示的）。</summary>
    private static string? FindAppName(List<QueryEntry> queries)
    {
        foreach (var query in queries)
        {
            if (query.Key.Count > 0
                && string.Equals(query.Key[0], "apphub", StringComparison.OrdinalIgnoreCase)
                && query.Data is JsonObject hub)
            {
                var name = GetString(hub, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        foreach (var query in queries)
        {
            if (query.Key.Count > 0
                && string.Equals(query.Key[0], "workshop_config_v8", StringComparison.OrdinalIgnoreCase)
                && query.Data is JsonObject config)
            {
                var name = GetString(config, "workshop_title");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    return name;
                }
            }
        }

        return null;
    }

    private static string StripGamePrefix(string name, string? appName)
    {
        if (string.IsNullOrWhiteSpace(appName))
        {
            return name;
        }

        var prefix = appName + ":";
        return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? name[prefix.Length..].Trim()
            : name;
    }

    private static string UniquifyKey(List<SteamWorkshopFilterGroup> groups, string key)
    {
        if (groups.All(group => !string.Equals(group.Key, key, StringComparison.Ordinal)))
        {
            return key;
        }

        return key + ":" + groups.Count.ToString(CultureInfo.InvariantCulture);
    }

    private static JsonObject? PickFromQuery(JsonObject query)
    {
        if (query["state"]?["data"] is JsonObject stateData && HasResults(stateData))
        {
            return stateData;
        }

        if (query["data"] is JsonObject direct && HasResults(direct))
        {
            return direct;
        }

        return null;
    }

    private static bool HasResults(JsonObject node) =>
        node.ContainsKey("results") || node.ContainsKey("total_count") || node.ContainsKey("totalCount");

    private static SteamWorkshopItem ParseItem(JsonObject item)
    {
        var tags = new List<SteamWorkshopTag>();
        if (item["tags"] is JsonArray tagArray)
        {
            foreach (var entry in tagArray)
            {
                if (entry is JsonObject tag)
                {
                    tags.Add(new SteamWorkshopTag(
                        GetString(tag, "tag") ?? string.Empty,
                        GetString(tag, "display_name") ?? string.Empty));
                }
            }
        }

        return new SteamWorkshopItem(
            PublishedFileId: GetString(item, "publishedfileid") ?? string.Empty,
            Title: GetString(item, "title") ?? string.Empty,
            ShortDescription: GetString(item, "short_description") ?? string.Empty,
            PreviewUrl: GetString(item, "preview_url"),
            AuthorSteamId: GetString(item, "creator") ?? string.Empty,
            Subscriptions: GetLong(item, "subscriptions"),
            Favorites: GetLong(item, "favorited"),
            Views: GetLong(item, "views"),
            CreatedUtc: FromUnixSeconds(GetLong(item, "time_created")),
            UpdatedUtc: FromUnixSeconds(GetLong(item, "time_updated")),
            FileSizeBytes: GetLongText(item, "file_size"),
            Tags: tags,
            StarRating: GetDouble(item, "star_rating"),
            TotalVotes: (int)GetLong(item, "total_votes"),
            ConsumerAppId: (int)GetLong(item, "consumer_appid"));
    }

    private static DateTimeOffset FromUnixSeconds(long seconds)
    {
        if (seconds <= 0)
        {
            return DateTimeOffset.UnixEpoch;
        }

        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        }
        catch (ArgumentOutOfRangeException)
        {
            return DateTimeOffset.UnixEpoch;
        }
    }

    /// <summary>读数字或字符串形式的整数（<c>publishedfileid</c> 两种都出现过）。</summary>
    private static string? GetString(JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return null;
        }

        if (value.TryGetValue<string>(out var text))
        {
            return text;
        }

        return value.ToJsonString().Trim('"');
    }

    private static long GetLong(JsonObject node, string name)
    {
        var text = GetString(node, name);
        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>读「可能是数字也可能是字符串」的体积字段；空串/非法 → null。</summary>
    private static long? GetLongText(JsonObject node, string name)
    {
        var text = GetString(node, name);
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0
            ? value
            : null;
    }

    private static double GetDouble(JsonObject node, string name)
    {
        var text = GetString(node, name);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    private static int GetInt(JsonObject node, string name)
    {
        if (node[name] is not JsonValue value)
        {
            return 0;
        }

        if (value.TryGetValue<int>(out var direct))
        {
            return direct;
        }

        return value.TryGetValue<string>(out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0;
    }
}
