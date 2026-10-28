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
}
