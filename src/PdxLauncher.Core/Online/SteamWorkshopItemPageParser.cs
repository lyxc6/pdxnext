namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text.RegularExpressions;
using PdxLauncher.Core.Text;

/// <summary>
/// 解析 Steam 创意工坊「物品页」HTML（<c>steamcommunity.com/sharedfiles/filedetails/?id=…</c>）。
/// <para>
/// 为什么抓 HTML：<c>GetPublishedFileDetails</c> 不给截图、不给依赖、也不给评论；
/// 而物品页是服务端直出的，作者名/主页、描述里的图片、评论区首屏都在同一份 HTML 里。
/// </para>
/// <para>
/// 页面结构随时可能改版，所以这里一律「解析不出来就返回 null / 空列表」，绝不抛异常：
/// 调用方据此静默降级，详情页主体不受影响。
/// </para>
/// </summary>
public static class SteamWorkshopItemPageParser
{
    /// <summary>最多取多少条评论（首屏够用，翻页交给浏览器）。</summary>
    public const int MaxComments = 10;

    /// <summary>单条评论正文的长度上限。</summary>
    public const int MaxCommentLength = 2000;

    private static readonly Regex TotalCountRegex = new(
        "\"total_count\"\\s*:\\s*(\\d+)",
        RegexOptions.CultureInvariant);

    private static readonly Regex ImageRegex = new(
        "<img[^>]*?src=\"([^\"]+)\"",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex AvatarRegex = new(
        "avatars\\.fastly\\.steamstatic\\.com|community\\.fastly\\.steamstatic\\.com",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>解析物品页；连标题都拿不到时返回 null。</summary>
    /// <param name="html">页面 HTML。</param>
    public static SteamWorkshopItemPage? Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return null;
        }

        var authorName = ExtractAuthorName(html);
        var authorUrl = ExtractAuthorUrl(html);
        var descriptionHtml = ExtractDescription(html);
        var images = ExtractImages(descriptionHtml);
        var comments = ParseComments(html);
        var totalComments = ExtractTotalComments(html);

        if (authorName is null && descriptionHtml is null && comments.Count == 0)
        {
            return null;
        }

        return new SteamWorkshopItemPage(
            AuthorName: authorName,
            AuthorProfileUrl: authorUrl,
            DescriptionHtml: descriptionHtml,
            ImageUrls: images,
            TotalComments: totalComments,
            Comments: comments);
    }

    private static string? ExtractAuthorName(string html)
    {
        var block = IndexOf(html, "class=\"friendBlockContent\"");
        if (block < 0)
        {
            return null;
        }

        var start = html.IndexOf('>', block);
        if (start < 0)
        {
            return null;
        }

        var end = html.IndexOf('<', start + 1);
        if (end < 0)
        {
            return null;
        }

        var name = System.Net.WebUtility.HtmlDecode(html[(start + 1)..end]).Trim();
        return name.Length == 0 ? null : name;
    }

    private static string? ExtractAuthorUrl(string html)
    {
        var block = IndexOf(html, "class=\"creatorsBlock\"");
        if (block < 0)
        {
            return null;
        }

        var link = IndexOf(html, "class=\"friendBlockLinkOverlay\"", block);
        if (link < 0)
        {
            return null;
        }

        var href = ReadAttribute(html, link, "href");
        return string.IsNullOrWhiteSpace(href) ? null : href;
    }

    private static string? ExtractDescription(string html)
    {
        var marker = IndexOf(html, "class=\"workshopItemDescription\"");
        if (marker < 0)
        {
            return null;
        }

        var tagStart = html.LastIndexOf('<', marker);
        if (tagStart < 0)
        {
            return null;
        }

        var contentStart = html.IndexOf('>', marker);
        if (contentStart < 0)
        {
            return null;
        }

        var content = ExtractDivContent(html, contentStart + 1);
        return content is null || content.Length == 0 ? null : content;
    }

    private static IReadOnlyList<string> ExtractImages(string? descriptionHtml)
    {
        if (string.IsNullOrWhiteSpace(descriptionHtml))
        {
            return [];
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var urls = new List<string>();
        foreach (Match match in ImageRegex.Matches(descriptionHtml))
        {
            var src = System.Net.WebUtility.HtmlDecode(match.Groups[1].Value).Trim();
            if (src.Length == 0 || AvatarRegex.IsMatch(src))
            {
                continue;
            }

            if (!src.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (seen.Add(src))
            {
                urls.Add(src);
            }
        }

        return urls;
    }

    /// <summary>
    /// 解析一段包含评论块的 HTML，取前 <paramref name="maxComments"/> 条。
    /// <para>
    /// 物品页内嵌的评论与翻页接口（<c>/comment/PublishedFile_Public/render/…</c>）返回的
    /// <c>comments_html</c> 结构完全同构，所以两边共用这一份解析。
    /// </para>
    /// </summary>
    /// <param name="html">含评论块的 HTML（也可以是整页 HTML）。</param>
    /// <param name="maxComments">最多取多少条。</param>
    public static IReadOnlyList<SteamWorkshopComment> ParseComments(string? html, int maxComments = MaxComments)
    {
        if (string.IsNullOrWhiteSpace(html) || maxComments <= 0)
        {
            return [];
        }

        var comments = new List<SteamWorkshopComment>();
        var marker = "class=\"commentthread_comment ";
        var searchFrom = 0;

        while (comments.Count < maxComments)
        {
            var start = IndexOf(html, marker, searchFrom);
            if (start < 0)
            {
                break;
            }

            var blockEnd = IndexOf(html, marker, start + marker.Length);
            var limit = blockEnd < 0 ? Math.Min(html.Length, start + 20000) : blockEnd;

            comments.AddRange(ParseComment(html, start, limit));
            searchFrom = limit;
            if (blockEnd < 0)
            {
                break;
            }
        }

        if (comments.Count > maxComments)
        {
            comments.RemoveRange(maxComments, comments.Count - maxComments);
        }

        return comments;
    }

    /// <summary>从评论接口返回的 JSON 里取出评论总数（<c>"total_count":N</c>）。</summary>
    /// <param name="json">评论接口 JSON。</param>
    public static int ParseCommentTotal(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return 0;
        }

        return ExtractTotalComments(json);
    }

    private static IEnumerable<SteamWorkshopComment> ParseComment(string html, int start, int end)
    {
        var segment = html[start..end];

        var authorName = string.Empty;
        var bdiStart = IndexOf(segment, "<bdi>");
        if (bdiStart >= 0)
        {
            var bdiEnd = IndexOf(segment, "</bdi>", bdiStart);
            if (bdiEnd > bdiStart)
            {
                authorName = System.Net.WebUtility.HtmlDecode(segment[(bdiStart + 5)..bdiEnd]).Trim();
            }
        }

        if (authorName.Length == 0)
        {
            yield break;
        }

        var authorUrl = string.Empty;
        var link = IndexOf(segment, "class=\"hoverunderline commentthread_author_link\"");
        if (link >= 0)
        {
            authorUrl = ReadAttribute(segment, link, "href");
        }

        var posted = DateTimeOffset.UnixEpoch;
        var stamp = IndexOf(segment, "data-timestamp=\"");
        if (stamp >= 0)
        {
            var valueStart = stamp + "data-timestamp=\"".Length;
            var valueEnd = segment.IndexOf('"', valueStart);
            if (valueEnd > valueStart
                && long.TryParse(segment[valueStart..valueEnd], NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds)
                && seconds > 0)
            {
                posted = DateTimeOffset.FromUnixTimeSeconds(seconds);
            }
        }

        var textStart = IndexOf(segment, "class=\"commentthread_comment_text\"");
        if (textStart < 0)
        {
            yield break;
        }

        var open = segment.IndexOf('>', textStart);
        if (open < 0)
        {
            yield break;
        }

        var content = ExtractDivContent(segment, open + 1);
        if (string.IsNullOrWhiteSpace(content))
        {
            yield break;
        }

        if (content.Length > MaxCommentLength)
        {
            content = content[..MaxCommentLength];
        }

        var text = WorkshopHtml.ToPlainText(content);
        if (text.Length == 0)
        {
            yield break;
        }

        var idStart = segment.IndexOf("id=\"comment_", StringComparison.Ordinal);
        var id = string.Empty;
        if (idStart >= 0)
        {
            var idValueStart = idStart + "id=\"comment_".Length;
            var idValueEnd = segment.IndexOf('"', idValueStart);
            if (idValueEnd > idValueStart)
            {
                id = segment[idValueStart..idValueEnd];
            }
        }

        yield return new SteamWorkshopComment(
            Id: id,
            AuthorName: authorName,
            AuthorProfileUrl: authorUrl.Length == 0 ? null : authorUrl,
            PostedUtc: posted,
            TextHtml: content,
            TextPlain: text);
    }

    private static int ExtractTotalComments(string html)
    {
        var match = TotalCountRegex.Match(html);
        return match.Success && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : 0;
    }

    /// <summary>从 <paramref name="contentStart"/> 开始，取到与之配对的 <c>&lt;/div&gt;</c> 之前的内容。</summary>
    private static string? ExtractDivContent(string html, int contentStart)
    {
        var depth = 1;
        var index = contentStart;

        while (index < html.Length && depth > 0)
        {
            var open = html.IndexOf("<div", index, StringComparison.OrdinalIgnoreCase);
            var close = html.IndexOf("</div", index, StringComparison.OrdinalIgnoreCase);

            if (close < 0)
            {
                return html[contentStart..].Trim();
            }

            if (open >= 0 && open < close)
            {
                depth++;
                index = open + 4;
                continue;
            }

            depth--;
            if (depth == 0)
            {
                return html[contentStart..close];
            }

            index = close + 5;
        }

        return html.Length > contentStart ? html[contentStart..].Trim() : null;
    }

    private static int IndexOf(string text, string value, int start = 0) =>
        start >= text.Length ? -1 : text.IndexOf(value, start, StringComparison.Ordinal);

    private static string ReadAttribute(string text, int tagPosition, string attribute)
    {
        var name = attribute + "=\"";
        var position = text.IndexOf(name, Math.Max(0, tagPosition), StringComparison.Ordinal);
        if (position < 0)
        {
            return string.Empty;
        }

        var valueStart = position + name.Length;
        var valueEnd = text.IndexOf('"', valueStart);
        return valueEnd < 0 ? string.Empty : text[valueStart..valueEnd];
    }
}
