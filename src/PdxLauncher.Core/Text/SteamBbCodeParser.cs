namespace PdxLauncher.Core.Text;

using System.Text;

/// <summary>
/// 把 Steam 创意工坊的 BBCode 描述解析成 <see cref="MarkdownDocument"/>。
/// <para>
/// 为什么需要它：工坊页面能抓到 HTML 时排版是好的（<see cref="HtmlDescriptionParser"/>），
/// 但 Steam Web API 兜底路径只给 BBCode 原文；上传页的「预览」也要把作者刚写下的
/// BBCode 立刻渲染出来。两者共用这一份实现，预览与详情页才不会长得不一样。
/// </para>
/// <para>
/// 只认 Steam 社区真正支持的标签（<c>[h1]</c>–<c>[h6]</c>、<c>[b]</c>、<c>[i]</c>、<c>[u]</c>、
/// <c>[strike]</c>、<c>[spoiler]</c>、<c>[noparse]</c>、<c>[hr]</c>、<c>[url]</c>、<c>[list]</c>、
/// <c>[olist]</c>、<c>[quote]</c>、<c>[code]</c>、<c>[img]</c>、<c>[previewyoutube]</c>）。
/// 不认识的方括号一律原样保留 —— 作者常拿它写正文（例如版本号 <c>[1.13]</c>），
/// 用 <c>\[[^\]]*\]</c> 这类粗暴正则会把这些字一起吃掉。
/// </para>
/// </summary>
public static class SteamBbCodeParser
{
    /// <summary>输入上限：超长描述只取前这么多字符，避免病态输入拖死界面。</summary>
    public const int MaxInputLength = 20000;

    /// <summary>嵌套容器（引用 / 折叠块）最多下钻的层数。</summary>
    private const int MaxNestingDepth = 4;

    /// <summary>行内样式标签 → 对应的 AST 样式（<c>[u]</c> 没有下划线样式，用斜体近似）。</summary>
    private static readonly (string Tag, MarkdownInlineStyle Style)[] StyleTags =
    [
        ("b", MarkdownInlineStyle.Bold),
        ("i", MarkdownInlineStyle.Italic),
        ("u", MarkdownInlineStyle.Italic),
        ("strike", MarkdownInlineStyle.Strikethrough),
        ("s", MarkdownInlineStyle.Strikethrough),
    ];

    /// <summary>把 BBCode 解析成 AST；null 或空白返回空文档。</summary>
    /// <param name="source">BBCode 原文（通常是 <c>ISteamRemoteStorage</c> 返回的 description）。</param>
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

        // 与 MarkdownParser 一致：作者常混写 &amp; / &nbsp; 实体，先整体解码再按行解析。
        text = System.Net.WebUtility.HtmlDecode(text);

        var lines = text.Split('\n');
        var blocks = new List<MarkdownBlock>();
        var index = 0;
        ParseBlocks(lines, ref index, blocks, 0);
        return new MarkdownDocument(blocks, truncated);
    }

    private static void ParseBlocks(string[] lines, ref int index, List<MarkdownBlock> blocks, int depth)
    {
        while (index < lines.Length)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0)
            {
                index++;
                continue;
            }

            if (TryReadHeading(trimmed, out var level, out var headingText))
            {
                blocks.Add(new MarkdownHeading(level, ParseInlines(headingText)));
                index++;
                continue;
            }

            if (IsRuleLine(trimmed))
            {
                blocks.Add(new MarkdownRule());
                index++;
                continue;
            }

            if (TryReadCodeBlock(lines, ref index, out var codeBlock))
            {
                blocks.Add(codeBlock);
                continue;
            }

            if (TryReadContainer(lines, ref index, "quote", depth, out var quoteBlocks))
            {
                blocks.Add(new MarkdownQuote(quoteBlocks));
                continue;
            }

            // [spoiler] 在 Steam 上是折叠块，我们没有折叠控件，按引用块呈现（缩进 + 左边线）。
            if (TryReadContainer(lines, ref index, "spoiler", depth, out var spoilerBlocks))
            {
                blocks.Add(new MarkdownQuote(spoilerBlocks));
                continue;
            }

            if (TryReadList(lines, ref index, depth, out var listBlocks))
            {
                blocks.AddRange(listBlocks);
                continue;
            }

            // 兜底的段落分支必须保证 index 前进，否则遇到「认得是块首但读不出内容」的行会死循环。
            var before = index;
            var paragraph = ReadParagraph(lines, ref index);
            if (index == before)
            {
                index++;
                continue;
            }

            blocks.Add(paragraph);
        }
    }

    private static MarkdownParagraph ReadParagraph(string[] lines, ref int index)
    {
        var builder = new StringBuilder();
        while (index < lines.Length)
        {
            var trimmed = lines[index].Trim();
            if (trimmed.Length == 0 || IsBlockStart(trimmed))
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(trimmed);
            index++;
        }

        return new MarkdownParagraph(ParseInlines(builder.ToString()));
    }

    private static bool IsBlockStart(string trimmed) =>
        TryReadHeading(trimmed, out _, out _)
        || IsRuleLine(trimmed)
        || StartsWithTag(trimmed, "code")
        || StartsWithTag(trimmed, "quote")
        || StartsWithTag(trimmed, "spoiler")
        || IsListOpenLine(trimmed);

    private static bool TryReadHeading(string trimmed, out int level, out string text)
    {
        level = 0;
        text = string.Empty;
        if (trimmed.Length < 4 || trimmed[0] != '[' || (trimmed[1] != 'h' && trimmed[1] != 'H'))
        {
            return false;
        }

        if (!char.IsAsciiDigit(trimmed[2]) || trimmed[3] != ']')
        {
            return false;
        }

        var parsed = trimmed[2] - '0';
        if (parsed is < 1 or > 6)
        {
            return false;
        }

        var rest = trimmed[4..];
        var close = "[/h" + parsed + "]";
        var closeIndex = rest.IndexOf(close, StringComparison.OrdinalIgnoreCase);
        if (closeIndex >= 0)
        {
            rest = rest[..closeIndex];
        }

        rest = rest.Trim();
        if (rest.Length == 0)
        {
            return false;
        }

        level = parsed;
        text = rest;
        return true;
    }

    private static bool IsRuleLine(string trimmed) =>
        trimmed.Equals("[hr]", StringComparison.OrdinalIgnoreCase)
        || trimmed.Equals("[hr/]", StringComparison.OrdinalIgnoreCase)
        || trimmed.Equals("[/hr]", StringComparison.OrdinalIgnoreCase)
        || trimmed.Equals("[hr][/hr]", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadCodeBlock(string[] lines, ref int index, out MarkdownCodeBlock block)
    {
        block = null!;
        var trimmed = lines[index].Trim();
        if (!StartsWithTag(trimmed, "code"))
        {
            return false;
        }

        var language = string.Empty;
        var openEnd = trimmed.IndexOf(']');
        if (openEnd < 0)
        {
            return false;
        }

        var tag = trimmed[1..openEnd];
        var equals = tag.IndexOf('=');
        if (equals >= 0)
        {
            language = tag[(equals + 1)..].Trim();
        }

        var remainder = trimmed[(openEnd + 1)..];
        var inlineClose = remainder.IndexOf("[/code]", StringComparison.OrdinalIgnoreCase);
        if (inlineClose >= 0)
        {
            // 单行写法：[code]x[/code]
            block = new MarkdownCodeBlock(remainder[..inlineClose], NullIfEmpty(language));
            index++;
            return true;
        }

        var code = new StringBuilder(remainder);
        index++;
        while (index < lines.Length)
        {
            var line = lines[index];
            var close = line.IndexOf("[/code]", StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
            {
                if (code.Length > 0)
                {
                    code.Append('\n');
                }

                code.Append(line[..close]);
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

        block = new MarkdownCodeBlock(code.ToString(), NullIfEmpty(language));
        return true;
    }

    private static bool TryReadContainer(
        string[] lines,
        ref int index,
        string tag,
        int depth,
        out List<MarkdownBlock> blocks)
    {
        blocks = [];
        var trimmed = lines[index].Trim();
        if (!StartsWithTag(trimmed, tag) || depth >= MaxNestingDepth)
        {
            return false;
        }

        var openEnd = trimmed.IndexOf(']');
        if (openEnd < 0)
        {
            return false;
        }

        var remainder = trimmed[(openEnd + 1)..];
        var closeTag = "[/" + tag + "]";
        if (remainder.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase) is var inlineClose && inlineClose >= 0)
        {
            // 单行写法：[quote]x[/quote]
            var single = new[] { remainder[..inlineClose] };
            var singleIndex = 0;
            ParseBlocks(single, ref singleIndex, blocks, depth + 1);
            index++;
            return true;
        }

        var body = new StringBuilder(remainder);
        index++;
        while (index < lines.Length)
        {
            var line = lines[index];
            var close = line.IndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
            if (close >= 0)
            {
                body.Append('\n').Append(line, 0, close);
                index++;
                break;
            }

            body.Append('\n').Append(line);
            index++;
        }

        var inner = body.ToString().Split('\n');
        var innerIndex = 0;
        ParseBlocks(inner, ref innerIndex, blocks, depth + 1);
        return true;
    }

    private static bool TryReadList(string[] lines, ref int index, int depth, out List<MarkdownBlock> blocks)
    {
        blocks = [];
        var trimmed = lines[index].Trim();
        if (!IsListOpenLine(trimmed))
        {
            return false;
        }

        var ordered = trimmed.StartsWith("[olist", StringComparison.OrdinalIgnoreCase);

        // 先把整个列表（含嵌套）收集成一段文本，再做一次线性拆分：
        // 逐行数开/闭标签比在行内找配对简单，而且列表里的换行没有语义。
        var raw = new StringBuilder();
        var open = 0;
        while (index < lines.Length)
        {
            var line = lines[index];
            raw.Append(line).Append('\n');
            open += CountTags(line, "[list", "[olist") - CountTags(line, "[/list]", "[/olist]");
            index++;
            if (open <= 0)
            {
                break;
            }
        }

        var text = raw.ToString();
        var start = text.IndexOf(']');
        if (start < 0)
        {
            return true;
        }

        ParseListContent(text, start + 1, ordered, Math.Min(depth, 6), blocks);
        return true;
    }

    /// <summary>按 <c>[*]</c> 切条目；遇到嵌套列表递归下钻，条目层级 +1。</summary>
    /// <returns>列表收尾标签之后的位置。</returns>
    private static int ParseListContent(string text, int start, bool ordered, int level, List<MarkdownBlock> blocks)
    {
        var item = new StringBuilder();
        var number = 1;
        var position = start;

        void Flush()
        {
            var value = item.ToString().Trim();
            item.Clear();
            if (value.Length == 0)
            {
                return;
            }

            blocks.Add(new MarkdownListItem(
                Math.Min(level, 6),
                ordered,
                ordered ? number++ : 0,
                ParseInlines(value)));
        }

        while (position < text.Length)
        {
            if (text[position] == '[')
            {
                var end = text.IndexOf(']', position);
                if (end > position)
                {
                    var tag = text[(position + 1)..end];
                    if (tag == "*")
                    {
                        Flush();
                        position = end + 1;
                        continue;
                    }

                    if (tag == "/*")
                    {
                        position = end + 1;
                        continue;
                    }

                    if (IsListOpenTagName(tag))
                    {
                        // Steam 的嵌套写法是「父条目内容在前，子列表在后」，所以先给当前条目收尾。
                        Flush();
                        var innerOrdered = tag.Equals("olist", StringComparison.OrdinalIgnoreCase);
                        position = ParseListContent(text, end + 1, innerOrdered, Math.Min(level + 1, 6), blocks);
                        continue;
                    }

                    if (IsListCloseTagName(tag))
                    {
                        Flush();
                        return end + 1;
                    }
                }
            }

            item.Append(text[position]);
            position++;
        }

        Flush();
        return position;
    }

    /// <summary>数一段文本里出现了几次给定标签（不区分大小写的逐次推进，不会重复计数）。</summary>
    private static int CountTags(string line, string first, string second)
    {
        var count = 0;
        var index = 0;
        while (index < line.Length)
        {
            var a = line.IndexOf(first, index, StringComparison.OrdinalIgnoreCase);
            var b = line.IndexOf(second, index, StringComparison.OrdinalIgnoreCase);
            var next = a < 0 ? b : b < 0 ? a : Math.Min(a, b);
            if (next < 0)
            {
                break;
            }

            count++;
            index = next + first.Length;
        }

        return count;
    }

    private static bool IsListOpenLine(string value)
    {
        var trimmed = value.TrimStart();
        return StartsWithTag(trimmed, "list") || StartsWithTag(trimmed, "olist");
    }

    private static bool IsListOpenTagName(string tag) =>
        tag.Equals("list", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("olist", StringComparison.OrdinalIgnoreCase);

    private static bool IsListCloseTagName(string tag) =>
        tag.Equals("/list", StringComparison.OrdinalIgnoreCase)
        || tag.Equals("/olist", StringComparison.OrdinalIgnoreCase);

    private static bool StartsWithTag(string value, string tag) =>
        value.StartsWith("[" + tag, StringComparison.OrdinalIgnoreCase);

    /// <summary>解析一行里的行内标记。</summary>
    /// <param name="text">不含块级标签的文本。</param>
    internal static IReadOnlyList<MarkdownInline> ParseInlines(string text)
    {
        var inlines = new List<MarkdownInline>();
        if (string.IsNullOrEmpty(text))
        {
            return inlines;
        }

        var buffer = new StringBuilder();
        var position = 0;

        void Flush()
        {
            if (buffer.Length > 0)
            {
                inlines.Add(new MarkdownTextInline(buffer.ToString()));
                buffer.Clear();
            }
        }

        while (position < text.Length)
        {
            var ch = text[position];

            if (ch == '[')
            {
                var end = text.IndexOf(']', position);
                if (end > position)
                {
                    var tag = text[(position + 1)..end];
                    var lower = tag.ToLowerInvariant();

                    if (lower == "noparse")
                    {
                        var close = IndexOfClose(text, end + 1, "noparse");
                        if (close >= 0)
                        {
                            Flush();
                            inlines.Add(new MarkdownTextInline(text[(end + 1)..close]));
                            position = close + "[/noparse]".Length;
                            continue;
                        }
                    }

                    if (lower == "code")
                    {
                        var close = IndexOfClose(text, end + 1, "code");
                        if (close >= 0)
                        {
                            Flush();
                            inlines.Add(new MarkdownCodeInline(text[(end + 1)..close]));
                            position = close + "[/code]".Length;
                            continue;
                        }
                    }

                    if (lower.StartsWith("img", StringComparison.Ordinal))
                    {
                        var close = IndexOfClose(text, end + 1, "img");
                        if (close >= 0)
                        {
                            var imageUrl = text[(end + 1)..close].Trim();
                            Flush();
                            if (imageUrl.Length > 0)
                            {
                                inlines.Add(new MarkdownImageInline(imageUrl, string.Empty));
                            }

                            position = close + "[/img]".Length;
                            continue;
                        }
                    }

                    if (lower.StartsWith("previewyoutube", StringComparison.Ordinal))
                    {
                        var id = ExtractEqualsValue(tag);
                        var close = IndexOfClose(text, end + 1, "previewyoutube");
                        Flush();
                        if (id.Length > 0)
                        {
                            inlines.Add(new MarkdownImageInline(
                                "https://img.youtube.com/vi/" + id + "/0.jpg",
                                "YouTube"));
                        }

                        position = close >= 0 ? close + "[/previewyoutube]".Length : end + 1;
                        continue;
                    }

                    if (lower.StartsWith("url", StringComparison.Ordinal))
                    {
                        var target = ExtractEqualsValue(tag);
                        var close = IndexOfClose(text, end + 1, "url");
                        if (close >= 0)
                        {
                            var label = text[(end + 1)..close];
                            Flush();
                            var linkUrl = target.Length > 0 ? target : label.Trim();
                            if (linkUrl.Length > 0)
                            {
                                inlines.Add(new MarkdownLinkInline(
                                    ParseInlines(label.Length > 0 ? label : linkUrl),
                                    linkUrl));
                            }

                            position = close + "[/url]".Length;
                            continue;
                        }
                    }

                    if (IsEmptyTag(lower))
                    {
                        // 单独出现的空标签没有可见内容；[/*] 是部分作者的列表收尾写法。
                        position = end + 1;
                        continue;
                    }

                    var style = FindStyle(lower);
                    if (style is not null)
                    {
                        var close = IndexOfClose(text, end + 1, lower);
                        if (close >= 0)
                        {
                            var inner = ParseInlines(text[(end + 1)..close]);
                            if (inner.Count > 0)
                            {
                                Flush();
                                inlines.Add(new MarkdownStyledInline(style.Value, inner));
                            }

                            position = close + lower.Length + 3;
                            continue;
                        }
                    }

                    // 不认识的标签：原样输出（正文里的 [1.13] 之类不能被吃掉）。
                    buffer.Append(text, position, end - position + 1);
                    position = end + 1;
                    continue;
                }
            }

            if ((ch is 'h' or 'H') && IsUrlStart(text, position)
                && TryReadBareUrl(text, position, out var url, out var consumed))
            {
                Flush();
                inlines.Add(new MarkdownLinkInline([new MarkdownTextInline(url)], url));
                position += consumed;
                continue;
            }

            buffer.Append(ch);
            position++;
        }

        Flush();
        return inlines;
    }

    /// <summary>裸链接只在词首才算，避免把 <c>xhttp://</c> 这种词中间的内容也变成链接。</summary>
    private static bool IsUrlStart(string text, int position)
    {
        if (position == 0)
        {
            return true;
        }

        var previous = text[position - 1];
        return char.IsWhiteSpace(previous) || previous is '(' or '[' or '"' or '\'' or '（';
    }

    private static bool IsEmptyTag(string tag) =>
        tag is "hr" or "hr/" or "/hr" or "/b" or "/i" or "/u" or "/strike" or "/s" or "/quote"
            or "/spoiler" or "/code" or "/list" or "/olist" or "*" or "/*";

    private static MarkdownInlineStyle? FindStyle(string tag)
    {
        foreach (var (name, style) in StyleTags)
        {
            if (tag.Equals(name, StringComparison.Ordinal))
            {
                return style;
            }
        }

        return null;
    }

    /// <summary>找 <c>[/tag]</c> 的起始下标；找不到返回 -1。</summary>
    private static int IndexOfClose(string text, int start, string tag) =>
        text.IndexOf("[/" + tag + "]", start, StringComparison.OrdinalIgnoreCase);

    /// <summary>从 <c>tag=value</c> 里取 value（去引号）；没有等号返回空串。</summary>
    private static string ExtractEqualsValue(string tag)
    {
        var equals = tag.IndexOf('=');
        if (equals < 0 || equals + 1 >= tag.Length)
        {
            return string.Empty;
        }

        return tag[(equals + 1)..].Trim().Trim('"', '\'');
    }

    private static bool TryReadBareUrl(string text, int start, out string url, out int consumed)
    {
        url = string.Empty;
        consumed = 0;

        var rest = text.AsSpan(start);
        if (!rest.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !rest.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var length = 0;
        while (length < rest.Length)
        {
            var ch = rest[length];
            if (char.IsWhiteSpace(ch) || ch is '[' or ']' or '<' or '>' or '"' or '（' or '）')
            {
                break;
            }

            length++;
        }

        // 句末标点不属于链接。
        while (length > 0 && rest[length - 1] is '.' or ',' or ';' or ':' or '!' or '?' or '、' or '。')
        {
            length--;
        }

        if (length <= "https://".Length)
        {
            return false;
        }

        url = rest[..length].ToString();
        consumed = length;
        return true;
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
