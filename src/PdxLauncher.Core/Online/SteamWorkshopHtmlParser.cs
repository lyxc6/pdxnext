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

            var payload = FindQueryPayload(queryData);
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

            return new SteamWorkshopPage(items, totalCount, page, pageSize, totalPages);
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
