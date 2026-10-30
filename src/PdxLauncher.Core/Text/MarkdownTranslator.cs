namespace PdxLauncher.Core.Text;

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
