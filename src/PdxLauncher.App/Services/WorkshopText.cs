namespace PdxLauncher.App.Services;

using System.Globalization;

/// <summary>
/// 创意工坊详情的文本处理：把作者写的 Markdown / HTML / BBCode 清成可读纯文本，
/// 以及把多个可选片段拼成一行（空段自动省略、不留孤立分隔符）。
/// <para>
/// 这里通篇只依赖 <c>System.*</c>，所以检查工程能直接链接源码做自检
/// （见 <c>tests/PdxLauncher.Checks/WorkshopTextChecks.cs</c>）。
/// </para>
/// </summary>
public static class WorkshopText
{
    /// <summary>
    /// 把作者写的 Markdown / HTML / BBCode 粗略清成可读纯文本。
    /// 只做「去掉标记、保留文字」，不追求完整渲染。
    /// </summary>
    /// <param name="source">原始描述；null 或空白返回空串。</param>
    public static string ToPlainText(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var text = source;

        // HTML 换行先转成真换行，再统一剥离标签。
        text = text
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        // Steam 描述是 BBCode：[b]x[/b]、[h1]x[/h1]、[url=…]x[/url]、[list][*]x[/list]。
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?(b|i|u|h[1-6]|list|\*)\]", string.Empty);
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\[/?(url|img)[^\]]*\]", string.Empty);

        var stripped = new System.Text.StringBuilder(text.Length);
        var inTag = false;
        foreach (var ch in text)
        {
            if (ch == '<')
            {
                inTag = true;
            }
            else if (ch == '>')
            {
                inTag = false;
            }
            else if (!inTag)
            {
                stripped.Append(ch);
            }
        }

        text = System.Net.WebUtility.HtmlDecode(stripped.ToString());

        // [文字](地址) → 文字（地址）；裸地址保持原样。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"\[([^\]]+)\]\((https?://[^)\s]+)\)",
            "$1（$2）");

        // 行首井号：### 标题 → 标题。只吃行内空格，绝不能跨行。
        text = System.Text.RegularExpressions.Regex.Replace(
            text,
            @"(?m)^[ \t]{0,3}#{1,6}[ \t]*",
            string.Empty);

        text = text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        // 收掉三行以上的连续空行。
        text = System.Text.RegularExpressions.Regex.Replace(text, @"(\r?\n){3,}", "\n\n");

        return text.Trim();
    }

    /// <summary>把多个片段拼成一行：空白段丢弃、逐段 Trim、重复段只留第一个。</summary>
    /// <param name="parts">片段序列；null 视为空。</param>
    /// <param name="separator">连接符（如 <c>" · "</c>）。</param>
    public static string Join(IEnumerable<string?>? parts, string separator)
    {
        if (parts is null)
        {
            return string.Empty;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var part in parts)
        {
            if (string.IsNullOrWhiteSpace(part))
            {
                continue;
            }

            var trimmed = part.Trim();
            if (seen.Add(trimmed))
            {
                kept.Add(trimmed);
            }
        }

        return string.Join(separator, kept);
    }

    /// <summary>时间显示成当前区域的可读日期；未知时间（null / Unix 纪元）返回空串。</summary>
    /// <param name="value">时间戳。</param>
    public static string FormatDate(DateTimeOffset? value)
    {
        if (value is not { } stamp || stamp <= DateTimeOffset.UnixEpoch)
        {
            return string.Empty;
        }

        return stamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
    }

    /// <summary>
    /// 从 Steam 主页地址里取出 SteamID64（评论翻页接口要的是纯数字 owner id）。
    /// <para>
    /// 只认 <c>steamcommunity.com/profiles/&lt;数字&gt;</c>；自定义地址 <c>/id/&lt;名字&gt;</c> 拿不到数字 id，
    /// 直接返回 false —— 调用方据此隐藏分页条，而不是留一个点了没反应的按钮。
    /// </para>
    /// </summary>
    /// <param name="profileUrl">作者主页地址，或直接是一串 SteamID64。</param>
    /// <param name="steamId">解析出的 SteamID64。</param>
    public static bool TryParseSteamId(string? profileUrl, out string steamId)
    {
        steamId = string.Empty;
        if (string.IsNullOrWhiteSpace(profileUrl))
        {
            return false;
        }

        var text = profileUrl.Trim();

        // 也接受直接传进来的 17 位数字（缓存里 Author 字段就是这种形态）。
        if (IsSteamId(text))
        {
            steamId = text;
            return true;
        }

        const string Marker = "/profiles/";
        var index = text.IndexOf(Marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return false;
        }

        var start = index + Marker.Length;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        if (end == start)
        {
            return false;
        }

        var candidate = text[start..end];
        if (!IsSteamId(candidate))
        {
            return false;
        }

        steamId = candidate;
        return true;
    }

    /// <summary>SteamID64 是 17 位数字（账号本体），这里只按形态判断。</summary>
    private static bool IsSteamId(string value)
    {
        if (value.Length is < 16 or > 20)
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (!char.IsAsciiDigit(ch))
            {
                return false;
            }
        }

        return true;
    }
}
