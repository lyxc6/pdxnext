namespace PdxLauncher.Core.Text;

using System.Text;

/// <summary>
/// 把一份 Markdown 文档翻译成另一种语言，结构保持不变：
/// 标题层级、列表层级与序号、链接地址、强调样式都原样保留，
/// 代码块、图片、分隔线不翻译，引用块递归处理。
/// <para>
/// 翻译失败（引擎返回 null 或空串）时保留原文，绝不把内容改空。
/// </para>
/// </summary>
public sealed class MarkdownTranslator
{
    /// <summary>
    /// 翻译一段文本；返回 null 或空串表示「译不出来」，调用方应保留原文。
    /// </summary>
    /// <param name="text">原文。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public delegate Task<string?> TextTranslator(string text, CancellationToken cancellationToken);

    private readonly TextTranslator _translate;

    /// <summary>创建翻译器。</summary>
    /// <param name="translate">文本翻译委托。</param>
    public MarkdownTranslator(TextTranslator translate)
    {
        ArgumentNullException.ThrowIfNull(translate);

        _translate = translate;
    }

    /// <summary>
    /// 翻译整份文档。
    /// </summary>
    /// <param name="document">原文文档；null 返回空文档。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<MarkdownDocument> TranslateAsync(MarkdownDocument? document, CancellationToken cancellationToken)
    {
        if (document is null)
        {
            return MarkdownDocument.Empty;
        }

        var blocks = new List<MarkdownBlock>(document.Blocks.Count);
        foreach (var block in document.Blocks)
        {
            blocks.Add(await TranslateBlockAsync(block, cancellationToken).ConfigureAwait(false));
        }

        return new MarkdownDocument(blocks, document.IsTruncated);
    }

    /// <summary>翻译一个块级节点。</summary>
    private async Task<MarkdownBlock> TranslateBlockAsync(MarkdownBlock block, CancellationToken cancellationToken)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                return heading with
                {
                    Inlines = await TranslateInlinesAsync(heading.Inlines, cancellationToken).ConfigureAwait(false),
                };
            case MarkdownParagraph paragraph:
                return paragraph with
                {
                    Inlines = await TranslateInlinesAsync(paragraph.Inlines, cancellationToken).ConfigureAwait(false),
                };
            case MarkdownListItem item:
                return item with
                {
                    Inlines = await TranslateInlinesAsync(item.Inlines, cancellationToken).ConfigureAwait(false),
                };
            case MarkdownQuote quote:
            {
                var inner = new List<MarkdownBlock>(quote.Blocks.Count);
                foreach (var child in quote.Blocks)
                {
                    inner.Add(await TranslateBlockAsync(child, cancellationToken).ConfigureAwait(false));
                }

                return quote with { Blocks = inner };
            }

            // 代码块、图片、分隔线不翻译。
            case MarkdownCodeBlock:
            case MarkdownRule:
            default:
                return block;
        }
    }

    /// <summary>翻译一组行内节点：连续纯文本合并成一次翻译请求。</summary>
    private async Task<IReadOnlyList<MarkdownInline>> TranslateInlinesAsync(
        IReadOnlyList<MarkdownInline> inlines,
        CancellationToken cancellationToken)
    {
        var result = new List<MarkdownInline>(inlines.Count);
        var buffer = new List<MarkdownTextInline>();

        foreach (var inline in inlines)
        {
            if (inline is MarkdownTextInline text)
            {
                buffer.Add(text);
                continue;
            }

            await FlushBufferAsync(buffer, result, cancellationToken).ConfigureAwait(false);

            switch (inline)
            {
                // 裸链接和 [url]自动文字[/url] 的可见文本就是地址：不翻译，免得模型把 URL 改写。
                case MarkdownLinkInline link when IsBareUrlLink(link):
                    result.Add(link);
                    break;
                case MarkdownLinkInline link:
                    result.Add(link with
                    {
                        Inlines = await TranslateInlinesAsync(link.Inlines, cancellationToken).ConfigureAwait(false),
                    });
                    break;
                case MarkdownStyledInline styled:
                    result.Add(styled with
                    {
                        Inlines = await TranslateInlinesAsync(styled.Inlines, cancellationToken).ConfigureAwait(false),
                    });
                    break;
                default:
                    result.Add(inline);
                    break;
            }
        }

        await FlushBufferAsync(buffer, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// 链接可见文本是否本身就是 URL（裸链接、<c>[url]https://…[/url]</c> 这类自动文字）。
    /// 这类文字交给模型只会被改写成「中文说明」，所以整段跳过、URL 与原样文字都保留。
    /// </summary>
    private static bool IsBareUrlLink(MarkdownLinkInline link)
    {
        var text = FlattenText(link.Inlines).Trim();
        if (text.Length == 0)
        {
            return false;
        }

        return string.Equals(text, link.Url, StringComparison.Ordinal)
            || Uri.IsWellFormedUriString(text, UriKind.Absolute);
    }

    /// <summary>把行内节点展平成可见文本，供 <see cref="IsBareUrlLink"/> 判定。</summary>
    private static string FlattenText(IReadOnlyList<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        AppendText(inlines, builder);
        return builder.ToString();
    }

    private static void AppendText(IReadOnlyList<MarkdownInline> inlines, StringBuilder builder)
    {
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
                    builder.Append('\n');
                    break;
                case MarkdownImageInline image:
                    builder.Append(string.IsNullOrEmpty(image.Alt) ? image.Url : image.Alt);
                    break;
                case MarkdownLinkInline link:
                    AppendText(link.Inlines, builder);
                    break;
                case MarkdownStyledInline styled:
                    AppendText(styled.Inlines, builder);
                    break;
            }
        }
    }

    /// <summary>把攒下的连续纯文本一次性翻译后写进结果。</summary>
    private async Task FlushBufferAsync(
        List<MarkdownTextInline> buffer,
        List<MarkdownInline> result,
        CancellationToken cancellationToken)
    {
        if (buffer.Count == 0)
        {
            return;
        }

        var joined = string.Concat(buffer.Select(item => item.Text));
        var original = buffer.ToList();
        var trimmed = joined.Trim();
        buffer.Clear();

        if (trimmed.Length == 0)
        {
            return;
        }

        var translated = await _translate(trimmed, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(translated))
        {
            // 译不出来就保留原文。
            result.AddRange(original);
            return;
        }

        result.Add(new MarkdownTextInline(translated.Trim()));
    }
}
