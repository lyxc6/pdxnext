namespace PdxLauncher.Core.Text;

using System.Text;
using PdxLauncher.Core.Online;

/// <summary>行内强调样式。</summary>
public enum MarkdownInlineStyle
{
    /// <summary>粗体（<c>**x**</c>）。</summary>
    Bold,

    /// <summary>斜体（<c>*x*</c>）。</summary>
    Italic,

    /// <summary>删除线（<c>~~x~~</c>）。</summary>
    Strikethrough,
}

/// <summary>
/// 行内节点。Markdown 与 HTML 两种来源都落到这一套 AST 上，
/// 界面只需实现一次渲染（见 <c>PdxLauncher.App.Controls.MarkdownPresenter</c>）。
/// </summary>
public abstract record MarkdownInline;

/// <summary>纯文本。</summary>
/// <param name="Text">已解码实体的文本。</param>
public sealed record MarkdownTextInline(string Text) : MarkdownInline;

/// <summary>行内代码（等宽显示，不参与强调解析）。</summary>
/// <param name="Text">代码文本。</param>
public sealed record MarkdownCodeInline(string Text) : MarkdownInline;

/// <summary>硬换行（HTML 的 <c>&lt;br&gt;</c>、Markdown 的「行尾两空格」）。</summary>
public sealed record MarkdownBreakInline : MarkdownInline;

/// <summary>行内图片。</summary>
/// <param name="Url">图片地址（http/https 或相对路径）。</param>
/// <param name="Alt">替代文本。</param>
public sealed record MarkdownImageInline(string Url, string Alt) : MarkdownInline;

/// <summary>链接。</summary>
/// <param name="Inlines">链接文字。</param>
/// <param name="Url">目标地址。</param>
public sealed record MarkdownLinkInline(IReadOnlyList<MarkdownInline> Inlines, string Url) : MarkdownInline;

/// <summary>带样式的行内片段。</summary>
/// <param name="Style">样式。</param>
/// <param name="Inlines">被包裹的片段。</param>
public sealed record MarkdownStyledInline(MarkdownInlineStyle Style, IReadOnlyList<MarkdownInline> Inlines)
    : MarkdownInline;

/// <summary>块级节点。</summary>
public abstract record MarkdownBlock;

/// <summary>标题（1–6 级）。</summary>
/// <param name="Level">级别。</param>
/// <param name="Inlines">标题文字。</param>
public sealed record MarkdownHeading(int Level, IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>段落。</summary>
/// <param name="Inlines">段落内容。</param>
public sealed record MarkdownParagraph(IReadOnlyList<MarkdownInline> Inlines) : MarkdownBlock;

/// <summary>列表项（扁平存放，缩进由 <paramref name="Level"/> 表达）。</summary>
/// <param name="Level">嵌套层级，0 为顶层。</param>
/// <param name="Ordered">是否有序列表。</param>
/// <param name="Number">有序列表序号（无序为 0）。</param>
/// <param name="Inlines">条目文字。</param>
public sealed record MarkdownListItem(int Level, bool Ordered, int Number, IReadOnlyList<MarkdownInline> Inlines)
    : MarkdownBlock;

/// <summary>代码块。</summary>
/// <param name="Text">原始代码。</param>
/// <param name="Language">围栏后声明的语言（可为空）。</param>
public sealed record MarkdownCodeBlock(string Text, string? Language) : MarkdownBlock;

/// <summary>引用块。</summary>
/// <param name="Blocks">被引用的块。</param>
public sealed record MarkdownQuote(IReadOnlyList<MarkdownBlock> Blocks) : MarkdownBlock;

/// <summary>分隔线。</summary>
public sealed record MarkdownRule : MarkdownBlock;

/// <summary>
/// 一份解析完成的富文本：块列表 + 是否被截断 + 纯文本兜底。
/// <para>纯逻辑、只依赖 <c>System.*</c>，检查工程可直接链接源码自检。</para>
/// </summary>
public sealed class MarkdownDocument
{
    /// <summary>空文档。</summary>
    public static readonly MarkdownDocument Empty = new([], false);

    /// <summary>创建文档。</summary>
    /// <param name="blocks">块列表。</param>
    /// <param name="isTruncated">输入是否因过长被截断。</param>
    public MarkdownDocument(IReadOnlyList<MarkdownBlock> blocks, bool isTruncated)
    {
        Blocks = blocks;
        IsTruncated = isTruncated;
    }

    /// <summary>块列表。</summary>
    public IReadOnlyList<MarkdownBlock> Blocks { get; }

    /// <summary>输入是否因过长被截断（界面据此显示提示）。</summary>
    public bool IsTruncated { get; }

    /// <summary>没有任何可见内容。</summary>
    public bool IsEmpty => Blocks.Count == 0;

    /// <summary>文档里出现的图片地址（按出现顺序去重），供图片查看器使用。</summary>
    public IReadOnlyList<string> ImageUrls
    {
        get
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var urls = new List<string>();
            foreach (var block in Blocks)
            {
                CollectImages(block, seen, urls);
            }

            return urls;
        }
    }

    /// <summary>把整份文档压成纯文本（无障碍名称、复制、兜底显示都用它）。</summary>
    public string ToPlainText()
    {
        var builder = new StringBuilder();
        foreach (var block in Blocks)
        {
            AppendBlock(block, builder);
            if (builder.Length > 0 && builder[^1] != '\n')
            {
                builder.Append('\n');
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>
    /// 把单个块压成纯文本。界面按块把原文与译文配对时，用它判断这一段是否真的翻出了不同内容
    /// （代码块、分隔线、翻译失败保留原文的段落都相等，不该再重复显示一遍）。
    /// </summary>
    public static string ToPlainText(MarkdownBlock block)
    {
        var builder = new StringBuilder();
        AppendBlock(block, builder);
        return builder.ToString().Trim();
    }

    private static void AppendBlock(MarkdownBlock block, StringBuilder builder)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                AppendInlines(heading.Inlines, builder);
                break;
            case MarkdownParagraph paragraph:
                AppendInlines(paragraph.Inlines, builder);
                break;
            case MarkdownListItem item:
                builder.Append(' ', item.Level * 2).Append(item.Ordered ? "- " : "- ");
                AppendInlines(item.Inlines, builder);
                break;
            case MarkdownCodeBlock code:
                builder.Append(code.Text);
                break;
            case MarkdownQuote quote:
                foreach (var inner in quote.Blocks)
                {
                    AppendBlock(inner, builder);
                    builder.Append('\n');
                }

                break;
            case MarkdownRule:
                builder.Append("---");
                break;
        }
    }

    private static void AppendInlines(IReadOnlyList<MarkdownInline> inlines, StringBuilder builder)
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
                    AppendInlines(link.Inlines, builder);
                    if (!string.IsNullOrEmpty(link.Url)
                        && !string.Equals(link.Url, Flatten(link.Inlines), StringComparison.Ordinal))
                    {
                        builder.Append("（").Append(link.Url).Append('）');
                    }

                    break;
                case MarkdownStyledInline styled:
                    AppendInlines(styled.Inlines, builder);
                    break;
            }
        }
    }

    private static string Flatten(IReadOnlyList<MarkdownInline> inlines)
    {
        var builder = new StringBuilder();
        AppendInlines(inlines, builder);
        return builder.ToString();
    }

    private static void CollectImages(MarkdownBlock block, HashSet<string> seen, List<string> urls)
    {
        switch (block)
        {
            case MarkdownHeading heading:
                CollectImages(heading.Inlines, seen, urls);
                break;
            case MarkdownParagraph paragraph:
                CollectImages(paragraph.Inlines, seen, urls);
                break;
            case MarkdownListItem item:
                CollectImages(item.Inlines, seen, urls);
                break;
            case MarkdownQuote quote:
                foreach (var inner in quote.Blocks)
                {
                    CollectImages(inner, seen, urls);
                }

                break;
        }
    }

    private static void CollectImages(IReadOnlyList<MarkdownInline> inlines, HashSet<string> seen, List<string> urls)
    {
        foreach (var inline in inlines)
        {
            switch (inline)
            {
                case MarkdownImageInline image:
                {
                    // 表情可能是 akamai 轮换地址：归一到 fastly 再去重，否则同一张图会被
                    // 当成两个地址下载（其中一个还过不了白名单），预览里也查不到缓存。
                    var url = SteamEmoticon.NormalizeUrl(image.Url);
                    if (!string.IsNullOrWhiteSpace(url) && seen.Add(url))
                    {
                        urls.Add(url);
                    }

                    break;
                }
                case MarkdownLinkInline link:
                    CollectImages(link.Inlines, seen, urls);
                    break;
                case MarkdownStyledInline styled:
                    CollectImages(styled.Inlines, seen, urls);
                    break;
            }
        }
    }
}
