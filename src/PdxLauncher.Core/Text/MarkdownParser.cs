namespace PdxLauncher.Core.Text;

using System.Globalization;
using System.Text;

/// <summary>
/// 手写的 Markdown 子集解析器。
/// <para>
/// 为什么不引第三方库：仓库一贯用手写解析器（<c>SteamWorkshopHtmlParser</c>、<c>LuaParser</c> 等），
/// 离线构建零外网依赖；而且作者写的简介只用得到很小的子集。
/// </para>
/// <para>支持：ATX 标题、段落、无序/有序列表（按缩进嵌套）、代码围栏、行内代码、粗体/斜体/删除线、
/// 链接、图片、行内 HTML（<c>&lt;br&gt;</c>、<c>&lt;img&gt;</c>）、引用块、分隔线。</para>
/// </summary>
public static class MarkdownParser
{
    /// <summary>输入上限（超长简介只取前这么多字符，避免病态输入拖死界面）。</summary>
    public const int MaxInputLength = 20000;

    /// <summary>引用块最多递归的层数。</summary>
    private const int MaxQuoteDepth = 4;

    /// <summary>把 Markdown 解析成 AST；null 或空白返回空文档。</summary>
    /// <param name="source">原始文本。</param>
    public static MarkdownDocument Parse(string? source)
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return MarkdownDocument.Empty;
        }

        var text = source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var truncated = text.Length > MaxInputLength;
        if (truncated)
        {
            text = text[..MaxInputLength];
        }

        // 作者常混写 HTML 实体（&amp;、&nbsp;），先整体解码再按行解析。
        text = System.Net.WebUtility.HtmlDecode(text);

        var lines = text.Split('\n');
        var blocks = new List<MarkdownBlock>();
        var index = 0;
        ParseBlocks(lines, ref index, blocks, 0);
        return new MarkdownDocument(blocks, truncated);
    }

    private static void ParseBlocks(string[] lines, ref int index, List<MarkdownBlock> blocks, int depth)
    {
        // 列表嵌套层级：原文的缩进宽度不可靠（有人用 4 个空格，有人用 tab 展开），
        // 所以按「比上一条最多深一层」归一化，避免一个缩进直接渲染出两级空白。
        var lastListLevel = -1;

        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (TryReadFence(line, out _))
            {
                blocks.Add(ReadCodeBlock(lines, ref index));
                lastListLevel = -1;
                continue;
            }

            if (IsRule(line))
            {
                blocks.Add(new MarkdownRule());
                lastListLevel = -1;
                index++;
                continue;
            }

            if (TryReadHeading(line, out var level, out var headingText))
            {
                blocks.Add(new MarkdownHeading(level, ParseInlines(headingText)));
                lastListLevel = -1;
                index++;
                continue;
            }

            if (line.TrimStart().StartsWith('>'))
            {
                if (depth < MaxQuoteDepth)
                {
                    blocks.Add(ReadQuote(lines, ref index, depth));
                }
                else
                {
                    index++;
                }

                lastListLevel = -1;
                continue;
            }

            if (TryReadListItem(line, out var listItem))
            {
                if (listItem is MarkdownListItem item)
                {
                    var normalized = Math.Min(item.Level, lastListLevel + 1);
                    lastListLevel = normalized;
                    blocks.Add(normalized == item.Level ? item : item with { Level = normalized });
                }
                else
                {
                    blocks.Add(listItem);
                }

                index++;
                continue;
            }

            blocks.Add(ReadParagraph(lines, ref index));
            lastListLevel = -1;
        }
    }

    private static MarkdownCodeBlock ReadCodeBlock(string[] lines, ref int index)
    {
        var open = lines[index];
        var fenceChar = open.TrimStart()[0];
        var fenceLength = 0;
        foreach (var ch in open.TrimStart())
        {
            if (ch != fenceChar)
            {
                break;
            }

            fenceLength++;
        }

        var language = open.TrimStart()[fenceLength..].Trim();
        index++;

        var code = new StringBuilder();
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (trimmed.Length >= fenceLength && trimmed.Length > 0 && trimmed[0] == fenceChar
                && trimmed.TrimEnd().All(c => c == fenceChar))
            {
                index++;
                break;
            }

            if (code.Length > 0)
            {
                code.Append('\n');
            }

            code.Append(line);
            index++;
        }

        return new MarkdownCodeBlock(code.ToString(), string.IsNullOrWhiteSpace(language) ? null : language);
    }

    private static MarkdownQuote ReadQuote(string[] lines, ref int index, int depth)
    {
        var inner = new List<string>();
        while (index < lines.Length)
        {
            var line = lines[index];
            var trimmed = line.TrimStart();
            if (!trimmed.StartsWith('>'))
            {
                break;
            }

            var content = trimmed[1..];
            if (content.StartsWith(' '))
            {
                content = content[1..];
            }

            inner.Add(content);
            index++;
        }

        var blocks = new List<MarkdownBlock>();
        var innerIndex = 0;
        ParseBlocks(inner.ToArray(), ref innerIndex, blocks, depth + 1);
        return new MarkdownQuote(blocks);
    }

    private static MarkdownParagraph ReadParagraph(string[] lines, ref int index)
    {
        var builder = new StringBuilder();
        while (index < lines.Length)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || IsBlockStart(line))
            {
                break;
            }

            if (builder.Length > 0)
            {
                // 行尾两个空格 = 硬换行，其余软换行按空格接续。
                if (builder.Length >= 2 && builder[^1] == ' ' && builder[^2] == ' ')
                {
                    builder.Length -= 2;
                    builder.Append('\u0001');
                }
                else
                {
                    builder.Append(' ');
                }
            }

            builder.Append(line.Trim());
            index++;
        }

        var text = builder.ToString();
        return new MarkdownParagraph(ParseInlines(text.Replace('\u0001', '\n')));
    }

    private static bool IsBlockStart(string line)
    {
        var trimmed = line.TrimStart();
        return TryReadFence(trimmed, out _)
               || IsRule(trimmed)
               || TryReadHeading(trimmed, out _, out _)
               || trimmed.StartsWith('>')
               || TryReadListItem(trimmed, out _);
    }

    private static bool TryReadFence(string line, out string language)
    {
        language = string.Empty;
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~'))
        {
            return false;
        }

        var fenceChar = trimmed[0];
        var count = 0;
        foreach (var ch in trimmed)
        {
            if (ch != fenceChar)
            {
                break;
            }

            count++;
        }

        if (count < 3)
        {
            return false;
        }

        language = trimmed[count..].Trim();
        return true;
    }

    private static bool IsRule(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length < 3)
        {
            return false;
        }

        var ch = trimmed[0];
        if (ch != '-' && ch != '*' && ch != '_')
        {
            return false;
        }

        foreach (var c in trimmed)
        {
            if (c != ch && c != ' ')
            {
                return false;
            }
        }

        return trimmed.Count(c => c == ch) >= 3;
    }

    private static bool TryReadHeading(string line, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('#'))
        {
            return false;
        }

        var count = 0;
        while (count < trimmed.Length && trimmed[count] == '#')
        {
            count++;
        }

        if (count is < 1 or > 6)
        {
            return false;
        }

        var rest = trimmed[count..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]))
        {
            return false;
        }

        level = count;
        text = rest.Trim().TrimEnd('#').Trim();
        return true;
    }

    private static bool TryReadListItem(string line, out MarkdownBlock block)
    {
        block = null!;
        var trimmed = line.TrimStart();
        if (trimmed.Length == 0)
        {
            return false;
        }

        var indent = line.Length - trimmed.Length;
        var level = Math.Min(indent / 2, 6);

        if (trimmed.Length > 2 && (trimmed[0] == '-' || trimmed[0] == '*' || trimmed[0] == '+')
            && char.IsWhiteSpace(trimmed[1]))
        {
            block = new MarkdownListItem(level, false, 0, ParseInlines(trimmed[2..].Trim()));
            return true;
        }

        var digits = 0;
        while (digits < trimmed.Length && char.IsAsciiDigit(trimmed[digits]))
        {
            digits++;
        }

        if (digits == 0 || digits >= trimmed.Length)
        {
            return false;
        }

        if (trimmed[digits] is not ('.' or ')') || digits + 1 >= trimmed.Length
            || !char.IsWhiteSpace(trimmed[digits + 1]))
        {
            return false;
        }

        var number = int.TryParse(trimmed[..digits], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 1;
        block = new MarkdownListItem(level, true, number, ParseInlines(trimmed[(digits + 2)..].Trim()));
        return true;
    }

    /// <summary>解析行内标记。</summary>
    /// <param name="text">单行（或含硬换行）文本。</param>
    internal static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var inlines = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text))
        {
            return inlines;
        }

        var buffer = new StringBuilder();
        var index = 0;

        void Flush()
        {
            if (buffer.Length > 0)
            {
                inlines.Add(new MarkdownTextInline(buffer.ToString()));
                buffer.Clear();
            }
        }

        while (index < text.Length)
        {
            var ch = text[index];

            if (ch == '\\' && index + 1 < text.Length)
            {
                buffer.Append(text[index + 1]);
                index += 2;
                continue;
            }

            if (ch == '\n')
            {
                Flush();
                inlines.Add(new MarkdownBreakInline());
                index++;
                continue;
            }

            if (ch == '`' && TryReadDelimited(text, ref index, "`", out var code))
            {
                Flush();
                inlines.Add(new MarkdownCodeInline(code));
                continue;
            }

            if (ch == '!' && index + 1 < text.Length && text[index + 1] == '['
                && TryReadLink(text, ref index, image: true, out var imageUrl, out var imageText))
            {
                Flush();
                inlines.Add(new MarkdownImageInline(imageUrl, imageText));
                continue;
            }

            if (ch == '[' && TryReadLink(text, ref index, image: false, out var linkUrl, out var linkText))
            {
                Flush();
                inlines.Add(new MarkdownLinkInline(ParseInlines(linkText), linkUrl));
                continue;
            }

            if (ch == '<' && TryReadAngleTag(text, ref index, out var tagName, out var attribute))
            {
                Flush();
                if (tagName.Equals("br", StringComparison.OrdinalIgnoreCase))
                {
                    inlines.Add(new MarkdownBreakInline());
                }
                else if (tagName.Equals("img", StringComparison.OrdinalIgnoreCase) && attribute.Length > 0)
                {
                    inlines.Add(new MarkdownImageInline(attribute, string.Empty));
                }

                // 其余行内标签（b/i/a…）直接丢弃标记，文字留给后续扫描。
                continue;
            }

            if (TryReadStyle(text, ref index, "**", out var boldText))
            {
                Flush();
                inlines.Add(new MarkdownStyledInline(MarkdownInlineStyle.Bold, ParseInlines(boldText)));
                continue;
            }

            if (TryReadStyle(text, ref index, "__", out var boldText2))
            {
                Flush();
                inlines.Add(new MarkdownStyledInline(MarkdownInlineStyle.Bold, ParseInlines(boldText2)));
                continue;
            }

            if (TryReadStyle(text, ref index, "~~", out var struckText))
            {
                Flush();
                inlines.Add(new MarkdownStyledInline(MarkdownInlineStyle.Strikethrough, ParseInlines(struckText)));
                continue;
            }

            if (ch == '*' && TryReadStyle(text, ref index, "*", out var italicText))
            {
                Flush();
                inlines.Add(new MarkdownStyledInline(MarkdownInlineStyle.Italic, ParseInlines(italicText)));
                continue;
            }

            if (ch == '_' && TryReadStyle(text, ref index, "_", out var italicText2))
            {
                Flush();
                inlines.Add(new MarkdownStyledInline(MarkdownInlineStyle.Italic, ParseInlines(italicText2)));
                continue;
            }

            buffer.Append(ch);
            index++;
        }

        Flush();
        return inlines;
    }

    private static bool TryReadDelimited(string text, ref int index, string delimiter, out string inner)
    {
        inner = string.Empty;
        if (!text.AsSpan(index).StartsWith(delimiter, StringComparison.Ordinal))
        {
            return false;
        }

        var start = index + delimiter.Length;
        if (start < text.Length && char.IsWhiteSpace(text[start]))
        {
            return false;
        }

        var end = text.IndexOf(delimiter, start, StringComparison.Ordinal);
        if (end <= start)
        {
            return false;
        }

        inner = text[start..end];
        index = end + delimiter.Length;
        return true;
    }

    private static bool TryReadStyle(string text, ref int index, string delimiter, out string inner)
    {
        inner = string.Empty;
        if (!TryReadDelimited(text, ref index, delimiter, out var candidate))
        {
            return false;
        }

        inner = candidate.Trim();
        return inner.Length > 0;
    }

    private static bool TryReadLink(string text, ref int index, bool image, out string url, out string label)
    {
        url = string.Empty;
        label = string.Empty;

        var cursor = index;
        if (image)
        {
            if (cursor + 1 >= text.Length || text[cursor] != '!' || text[cursor + 1] != '[')
            {
                return false;
            }

            cursor += 2;
        }
        else
        {
            if (text[cursor] != '[')
            {
                return false;
            }

            cursor++;
        }

        var labelEnd = text.IndexOf(']', cursor);
        if (labelEnd < 0 || labelEnd + 1 >= text.Length || text[labelEnd + 1] != '(')
        {
            return false;
        }

        var urlEnd = text.IndexOf(')', labelEnd + 2);
        if (urlEnd < 0)
        {
            return false;
        }

        label = text[cursor..labelEnd].Trim();
        url = text[(labelEnd + 2)..urlEnd].Trim();

        // 链接可能写成 [t](url "title")，标题部分丢掉。
        var space = url.IndexOf(' ');
        if (space > 0)
        {
            url = url[..space];
        }

        if (url.Length == 0)
        {
            return false;
        }

        index = urlEnd + 1;
        return true;
    }

    private static bool TryReadAngleTag(string text, ref int index, out string name, out string attribute)
    {
        name = string.Empty;
        attribute = string.Empty;

        var end = text.IndexOf('>', index + 1);
        if (end < 0)
        {
            return false;
        }

        var body = text[(index + 1)..end].Trim();
        if (body.Length == 0 || body[0] == '/')
        {
            // </b> 之类：吃掉即可。
            index = end + 1;
            return true;
        }

        if (!char.IsLetter(body[0]))
        {
            return false;
        }

        var nameEnd = 0;
        while (nameEnd < body.Length && (char.IsLetterOrDigit(body[nameEnd])))
        {
            nameEnd++;
        }

        name = body[..nameEnd];

        if (name.Equals("img", StringComparison.OrdinalIgnoreCase))
        {
            attribute = ExtractAttribute(body[nameEnd..], "src");
        }

        index = end + 1;
        return true;
    }

    private static string ExtractAttribute(string body, string attributeName)
    {
        var position = body.IndexOf(attributeName, StringComparison.OrdinalIgnoreCase);
        if (position < 0)
        {
            return string.Empty;
        }

        var quoteStart = body.IndexOf('"', position);
        if (quoteStart < 0)
        {
            return string.Empty;
        }

        var quoteEnd = body.IndexOf('"', quoteStart + 1);
        return quoteEnd < 0 ? string.Empty : body[(quoteStart + 1)..quoteEnd];
    }
}
