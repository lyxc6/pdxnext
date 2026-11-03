namespace PdxLauncher.Core.Text;

using System.Text;

/// <summary>
/// 把作者写的 HTML（Steam 创意工坊的描述就是 HTML）转成与 Markdown 共用的 AST。
/// <para>
/// 只认「描述里真的会出现」的标签：段落/换行/标题/列表/粗体/斜体/链接/图片/代码块；
/// 其余标签（<c>span</c>、<c>font</c>、<c>div</c> 的 class 等）一律当容器，标记丢弃、文字保留。
/// </para>
/// </summary>
public static class HtmlDescriptionParser
{
    /// <summary>输入上限，超过即截断（与 Markdown 一致）。</summary>
    public const int MaxInputLength = 20000;

    private static readonly HashSet<string> BlockContainers = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "section", "article", "center", "header", "footer",
        "table", "thead", "tbody", "tr", "td", "th", "blockquote", "form", "figure",
    };

    /// <summary>解析 HTML 片段；null 或空白返回空文档。</summary>
    /// <param name="html">HTML 片段。</param>
    public static MarkdownDocument Parse(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return MarkdownDocument.Empty;
        }

        var text = html;
        var truncated = text.Length > MaxInputLength;
        if (truncated)
        {
            text = text[..MaxInputLength];
        }

        var builder = new Builder();
        foreach (var token in Tokenize(text))
        {
            builder.Accept(token);
        }

        builder.Finish();
        return new MarkdownDocument(builder.Blocks, truncated);
    }

    private enum TokenKind
    {
        Text,
        StartTag,
        EndTag,
    }

    private readonly record struct Token(TokenKind Kind, string Name, string Text, string Attributes);

    private static List<Token> Tokenize(string html)
    {
        var tokens = new List<Token>();
        var index = 0;

        while (index < html.Length)
        {
            var open = html.IndexOf('<', index);
            if (open < 0)
            {
                AddText(tokens, html[index..]);
                break;
            }

            if (open > index)
            {
                AddText(tokens, html[index..open]);
            }

            if (open + 1 >= html.Length)
            {
                break;
            }

            if (html.AsSpan(open).StartsWith("<!--", StringComparison.Ordinal))
            {
                var commentEnd = html.IndexOf("-->", open + 4, StringComparison.Ordinal);
                index = commentEnd < 0 ? html.Length : commentEnd + 3;
                continue;
            }

            if (html[open + 1] is '!' or '?')
            {
                var skipEnd = html.IndexOf('>', open + 2);
                index = skipEnd < 0 ? html.Length : skipEnd + 1;
                continue;
            }

            var close = FindTagEnd(html, open + 1);
            if (close < 0)
            {
                // 没闭合的 '<'，当普通文字。
                AddText(tokens, html[open..]);
                break;
            }

            var body = html[(open + 1)..close];
            index = close + 1;

            var isEnd = body.StartsWith('/');
            if (isEnd)
            {
                body = body[1..];
            }

            body = body.TrimEnd('/');
            var nameEnd = 0;
            while (nameEnd < body.Length && char.IsLetterOrDigit(body[nameEnd]))
            {
                nameEnd++;
            }

            if (nameEnd == 0)
            {
                continue;
            }

            var name = body[..nameEnd].ToLowerInvariant();
            var attributes = body[nameEnd..];
            tokens.Add(new Token(isEnd ? TokenKind.EndTag : TokenKind.StartTag, name, string.Empty, attributes));
        }

        return tokens;
    }

    private static void AddText(List<Token> tokens, string raw)
    {
        var decoded = System.Net.WebUtility.HtmlDecode(raw);
        if (decoded.Length > 0)
        {
            tokens.Add(new Token(TokenKind.Text, string.Empty, decoded, string.Empty));
        }
    }

    private static int FindTagEnd(string html, int start)
    {
        var quote = '\0';
        for (var i = start; i < html.Length; i++)
        {
            var ch = html[i];
            if (quote != '\0')
            {
                if (ch == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (ch is '"' or '\'')
            {
                quote = ch;
                continue;
            }

            if (ch == '>')
            {
                return i;
            }
        }

        return -1;
    }

    private static string GetAttribute(string attributes, string name)
    {
        var position = 0;
        while (position < attributes.Length)
        {
            var found = attributes.IndexOf(name, position, StringComparison.OrdinalIgnoreCase);
            if (found < 0)
            {
                return string.Empty;
            }

            var after = found + name.Length;
            if (after < attributes.Length && char.IsLetterOrDigit(attributes[after]))
            {
                // 命中的是别的属性名（如 class 里的 "href"），继续往后找。
                position = after;
                continue;
            }

            var cursor = after;
            while (cursor < attributes.Length && char.IsWhiteSpace(attributes[cursor]))
            {
                cursor++;
            }

            if (cursor >= attributes.Length)
            {
                return string.Empty;
            }

            if (attributes[cursor] != '=')
            {
                position = after;
                continue;
            }

            cursor++;
            while (cursor < attributes.Length && char.IsWhiteSpace(attributes[cursor]))
            {
                cursor++;
            }

            if (cursor >= attributes.Length)
            {
                return string.Empty;
            }

            var quote = attributes[cursor];
            if (quote is '"' or '\'')
            {
                var end = attributes.IndexOf(quote, cursor + 1);
                return end < 0 ? string.Empty : attributes[(cursor + 1)..end];
            }

            var valueEnd = cursor;
            while (valueEnd < attributes.Length && !char.IsWhiteSpace(attributes[valueEnd]))
            {
                valueEnd++;
            }

            return attributes[cursor..valueEnd];
        }

        return string.Empty;
    }

    /// <summary>把 token 流折成块列表。</summary>
    private sealed class Builder
    {
        private readonly List<MarkdownInline> _inlines = [];
        private readonly List<(MarkdownInlineStyle Style, int Start)> _styles = [];
        private readonly Stack<bool> _lists = new();
        private readonly Stack<int> _counters = new();
        private readonly StringBuilder _pre = new();
        private int _linkStart = -1;
        private string _linkUrl = string.Empty;
        private int _codeStart = -1;
        private int _headingLevel;
        private int _listLevel;
        private int _listNumber;
        private bool _listOrdered;
        private bool _inListItem;
        private bool _inPre;
        private bool _skipUntilEnd;
        private string _skipTag = string.Empty;

        public List<MarkdownBlock> Blocks { get; } = [];

        public void Accept(Token token)
        {
            if (_skipUntilEnd)
            {
                if (token.Kind == TokenKind.EndTag && token.Name == _skipTag)
                {
                    _skipUntilEnd = false;
                }

                return;
            }

            switch (token.Kind)
            {
                case TokenKind.Text:
                    AddText(token.Text);
                    break;
                case TokenKind.StartTag:
                    StartTag(token);
                    break;
                case TokenKind.EndTag:
                    EndTag(token);
                    break;
            }
        }

        public void Finish()
        {
            Flush();
            while (_lists.Count > 0)
            {
                _lists.Pop();
                _counters.Pop();
            }
        }

        private void AddText(string text)
        {
            if (_inPre)
            {
                _pre.Append(text);
                return;
            }

            var collapsed = Collapse(text);
            if (collapsed.Length == 0)
            {
                return;
            }

            _inlines.Add(new MarkdownTextInline(collapsed));
        }

        private static string Collapse(string text)
        {
            var builder = new StringBuilder(text.Length);
            var lastSpace = true;
            foreach (var ch in text)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!lastSpace)
                    {
                        builder.Append(' ');
                        lastSpace = true;
                    }

                    continue;
                }

                builder.Append(ch);
                lastSpace = false;
            }

            return builder.ToString();
        }

        private void StartTag(Token token)
        {
            var name = token.Name;
            switch (name)
            {
                case "script":
                case "style":
                    _skipUntilEnd = true;
                    _skipTag = name;
                    return;
                case "br":
                    if (_inPre)
                    {
                        _pre.Append('\n');
                    }
                    else
                    {
                        _inlines.Add(new MarkdownBreakInline());
                    }

                    return;
                case "img":
                {
                    var src = GetAttribute(token.Attributes, "src");
                    if (src.Length == 0)
                    {
                        return;
                    }

                    if (_inPre)
                    {
                        _pre.Append(src);
                    }
                    else
                    {
                        _inlines.Add(new MarkdownImageInline(src, GetAttribute(token.Attributes, "alt")));
                    }

                    return;
                }

                case "hr":
                    // Steam 把 BBCode 的 [hr] 渲染成 <hr>；此前没有分支，分隔线被静默丢掉，
                    // 于是「同一段描述走 HTML 路径和走 BBCode 路径排版不一样」。
                    if (!_inPre)
                    {
                        Flush();
                        Blocks.Add(new MarkdownRule());
                    }

                    return;
                case "pre":
                    Flush();
                    _inPre = true;
                    _pre.Clear();
                    return;
                case "ul":
                case "ol":
                    Flush();
                    _lists.Push(name == "ol");
                    _counters.Push(0);
                    return;
                case "li":
                    Flush();
                    _inListItem = true;
                    _listLevel = Math.Max(0, _lists.Count - 1);
                    _listOrdered = _lists.Count > 0 && _lists.Peek();
                    if (_listOrdered)
                    {
                        var next = _counters.Count > 0 ? _counters.Pop() + 1 : 1;
                        _listNumber = next;
                        _counters.Push(next);
                    }
                    else
                    {
                        _listNumber = 0;
                    }

                    return;
                case "a":
                    _linkStart = _inlines.Count;
                    _linkUrl = GetAttribute(token.Attributes, "href");
                    return;
                case "code":
                    if (!_inPre)
                    {
                        _codeStart = _inlines.Count;
                    }

                    return;
                case "b":
                case "strong":
                    _styles.Add((MarkdownInlineStyle.Bold, _inlines.Count));
                    return;
                case "i":
                case "em":
                    _styles.Add((MarkdownInlineStyle.Italic, _inlines.Count));
                    return;
                case "s":
                case "strike":
                case "del":
                    _styles.Add((MarkdownInlineStyle.Strikethrough, _inlines.Count));
                    return;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    Flush();
                    _headingLevel = name[1] - '0';
                    return;
            }

            if (BlockContainers.Contains(name))
            {
                Flush();
            }
        }

        private void EndTag(Token token)
        {
            var name = token.Name;
            switch (name)
            {
                case "br":
                case "img":
                    return;
                case "pre":
                    _inPre = false;
                    var code = _pre.ToString().Trim('\n');
                    if (code.Length > 0)
                    {
                        Blocks.Add(new MarkdownCodeBlock(code, null));
                    }

                    _pre.Clear();
                    return;
                case "ul":
                case "ol":
                    Flush();
                    if (_lists.Count > 0)
                    {
                        _lists.Pop();
                        _counters.Pop();
                    }

                    return;
                case "li":
                    Flush();
                    _inListItem = false;
                    return;
                case "a":
                    WrapLink();
                    return;
                case "code":
                    WrapCode();
                    return;
                case "b":
                case "strong":
                case "i":
                case "em":
                case "s":
                case "strike":
                case "del":
                    WrapStyle();
                    return;
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                    Flush();
                    return;
            }

            if (BlockContainers.Contains(name))
            {
                Flush();
            }
        }

        private void WrapLink()
        {
            if (_linkStart < 0 || _linkStart > _inlines.Count)
            {
                _linkStart = -1;
                return;
            }

            var children = _inlines.GetRange(_linkStart, _inlines.Count - _linkStart);
            _inlines.RemoveRange(_linkStart, children.Count);
            if (children.Count == 0)
            {
                _linkStart = -1;
                return;
            }

            if (string.IsNullOrWhiteSpace(_linkUrl))
            {
                _inlines.AddRange(children);
            }
            else
            {
                _inlines.Add(new MarkdownLinkInline(children, _linkUrl));
            }

            _linkStart = -1;
            _linkUrl = string.Empty;
        }

        private void WrapCode()
        {
            if (_codeStart < 0 || _codeStart > _inlines.Count)
            {
                _codeStart = -1;
                return;
            }

            var children = _inlines.GetRange(_codeStart, _inlines.Count - _codeStart);
            _inlines.RemoveRange(_codeStart, children.Count);
            var text = Flatten(children);
            if (text.Length > 0)
            {
                _inlines.Add(new MarkdownCodeInline(text));
            }

            _codeStart = -1;
        }

        private void WrapStyle()
        {
            if (_styles.Count == 0)
            {
                return;
            }

            var (style, start) = _styles[^1];
            _styles.RemoveAt(_styles.Count - 1);
            if (start < 0 || start > _inlines.Count)
            {
                return;
            }

            var children = _inlines.GetRange(start, _inlines.Count - start);
            if (children.Count == 0)
            {
                return;
            }

            _inlines.RemoveRange(start, children.Count);
            _inlines.Add(new MarkdownStyledInline(style, children));
        }

        private static string Flatten(IReadOnlyList<MarkdownInline> inlines)
        {
            var builder = new StringBuilder();
            foreach (var inline in inlines)
            {
                switch (inline)
                {
                    case MarkdownTextInline text:
                        builder.Append(text.Text);
                        break;
                    case MarkdownCodeInline code:
                        builder.Append(code.Text);
                        break;
                    case MarkdownBreakInline:
                        builder.Append(' ');
                        break;
                    case MarkdownImageInline image:
                        builder.Append(image.Alt);
                        break;
                    case MarkdownLinkInline link:
                        builder.Append(Flatten(link.Inlines));
                        break;
                    case MarkdownStyledInline styled:
                        builder.Append(Flatten(styled.Inlines));
                        break;
                }
            }

            return builder.ToString();
        }

        private void Flush()
        {
            TrimEdges();
            if (_inlines.Count == 0)
            {
                _headingLevel = 0;
                return;
            }

            var block = BuildBlock();
            if (block is not null)
            {
                Blocks.Add(block);
            }

            _inlines.Clear();
            _headingLevel = 0;
        }

        private MarkdownBlock? BuildBlock()
        {
            if (_headingLevel > 0)
            {
                return new MarkdownHeading(_headingLevel, _inlines.ToArray());
            }

            if (_inListItem)
            {
                return new MarkdownListItem(_listLevel, _listOrdered, _listNumber, _inlines.ToArray());
            }

            return new MarkdownParagraph(_inlines.ToArray());
        }

        private void TrimEdges()
        {
            if (_inlines.Count == 0)
            {
                return;
            }

            if (_inlines[0] is MarkdownTextInline first)
            {
                var trimmed = first.Text.TrimStart();
                if (trimmed.Length == 0)
                {
                    _inlines.RemoveAt(0);
                }
                else
                {
                    _inlines[0] = new MarkdownTextInline(trimmed);
                }
            }

            if (_inlines.Count > 0 && _inlines[^1] is MarkdownTextInline last)
            {
                var trimmed = last.Text.TrimEnd();
                if (trimmed.Length == 0)
                {
                    _inlines.RemoveAt(_inlines.Count - 1);
                }
                else
                {
                    _inlines[^1] = new MarkdownTextInline(trimmed);
                }
            }
        }
    }
}
