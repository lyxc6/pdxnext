namespace PdxLauncher.Core.Text;

using System.Net;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// 创意工坊文本的「去标记」工具：把作者写的 Markdown / HTML / BBCode 清成可读纯文本。
/// <para>
/// 只做「去掉标记、保留文字」，不追求完整排版；<see cref="MarkdownParser"/> /
/// <see cref="HtmlDescriptionParser"/> 负责真正的富文本渲染，这里只给列表副标题、
/// 通知、日志、复制这些纯文本场合用。
/// </para>
/// </summary>
public static class WorkshopHtml
{
    private static readonly Regex BbCodeBlockRegex = new(
        @"\[/?(b|i|u|h[1-6]|list|\*)\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex BbCodeMediaRegex = new(
        @"\[/?(url|img)[^\]]*\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Regex MarkdownLinkRegex = new(
        @"\[([^\]]+)\]\((https?://[^)\s]+)\)",
        RegexOptions.CultureInvariant);

    private static readonly Regex HeadingPrefixRegex = new(
        @"(?m)^[ \t]{0,3}#{1,6}[ \t]*",
        RegexOptions.CultureInvariant);

    private static readonly Regex BlankLinesRegex = new(
        @"(\r?\n){3,}",
        RegexOptions.CultureInvariant);

    /// <summary>把 Markdown / HTML / BBCode 粗略清成可读纯文本。</summary>
    /// <param name="source">原始文本；null 或空白返回空串。</param>
    public static string ToPlainText(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return string.Empty;
        }

        var text = source
            .Replace("<br />", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br/>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<br>", "\n", StringComparison.OrdinalIgnoreCase)
            .Replace("</p>", "\n\n", StringComparison.OrdinalIgnoreCase)
            .Replace("<p>", string.Empty, StringComparison.OrdinalIgnoreCase);

        text = BbCodeBlockRegex.Replace(text, string.Empty);
        text = BbCodeMediaRegex.Replace(text, string.Empty);

        var stripped = new StringBuilder(text.Length);
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

        text = WebUtility.HtmlDecode(stripped.ToString());
        text = MarkdownLinkRegex.Replace(text, "$1（$2）");
        text = HeadingPrefixRegex.Replace(text, string.Empty);
        text = text
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Replace("__", string.Empty, StringComparison.Ordinal)
            .Replace("`", string.Empty, StringComparison.Ordinal);

        text = BlankLinesRegex.Replace(text, "\n\n");
        return text.Trim();
    }
}
