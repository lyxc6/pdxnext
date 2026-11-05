namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Text;

/// <summary>
/// 行内流的排版判定：哪些行内要 <c>RichTextBlock</c>、哪些图单独成块、表情怎么规范化。
/// <para>
/// 界面侧据此在 <c>TextBlock</c> 与 <c>RichTextBlock</c> 之间二选一，选错的表现是评论
/// 整段不渲染或抛异常，而不是编译报错——所以判定必须被钉在这里。尤其是「样式/链接里
/// 裹着表情」：<c>MarkdownView.AppendInlines</c> 对样式无条件展平，判定也必须无条件递归，
/// 不能只看 <c>Inlines.Count == 1</c>。
/// </para>
/// </summary>
public static class MarkdownInlineFlowChecks
{
    private const string EmoticonUrl = "https://community.fastly.steamstatic.com/economy/emoticon/highrise";

    private const string AkamaiEmoticonUrl = "https://community.akamai.steamstatic.com/economy/emoticon/highrise";

    private const string ScreenshotUrl = "https://images.steamusercontent.com/ugc/1/2.jpg";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckBoldEmoticonNeedsRichText();
        CheckDeepStyledEmoticonNeedsRichText();
        CheckLinkEmoticonNeedsRichText();
        CheckPlainTextStaysTextBlock();
        CheckBlockImageStaysBlock();
        CheckImagePlans();
        CheckEmoticonSize();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>① <c>&lt;b&gt;nice &lt;img class="emoticon"&gt;&lt;/b&gt;</c>：样式有两个子节点，仍必须判 RichTextBlock。</summary>
    private static void CheckBoldEmoticonNeedsRichText()
    {
        var body = HtmlDescriptionParser.Parse(
            "<b>nice <img src=\"" + EmoticonUrl + "\" alt=\":highrise:\" class=\"emoticon\"></b>");

        var root = Inlines(body);
        var styled = root.Count == 1 ? root[0] as MarkdownStyledInline : null;
        ChecksContext.Equal("排版：粗体段落解析成了一个样式节点", true, styled is not null);
        ChecksContext.Equal("排版：粗体样式里有两个子节点（文字 + 表情）", 2, styled?.Inlines.Count ?? -1);
        ChecksContext.Equal("排版：粗体里的样式是 Bold", MarkdownInlineStyle.Bold, styled?.Style);

        // 这一条就是「粗体里带表情的段落必须走 RichTextBlock」的判定本身：
        // BuildTextBlock 为 true 时建 RichTextBlock + Paragraph，否则把 InlineUIContainer
        // 塞进 TextBlock.Inlines（WinUI 不允许）。
        ChecksContext.Equal("排版：粗体里带表情 → 需要 RichTextBlock", true, MarkdownInlineFlow.RequiresRichText(Inlines(body)));

        // 反过来：它不该再被当成「单独成块的图片」，否则会走 StackPanel 分段而不是文字流。
        ChecksContext.Equal("排版：粗体里带表情 → 不算正文配图块", false, MarkdownInlineFlow.HasBlockImage(Inlines(body)));
    }

    /// <summary>② 深一层 <c>&lt;b&gt;&lt;i&gt;…&lt;/i&gt;&lt;/b&gt;</c> 同样要判 RichTextBlock。</summary>
    private static void CheckDeepStyledEmoticonNeedsRichText()
    {
        var body = HtmlDescriptionParser.Parse(
            "<b><i>hi <img src=\"" + EmoticonUrl + "\" alt=\":highrise:\" class=\"emoticon\"></i></b>");
        ChecksContext.Equal("排版：双层样式里带表情 → 需要 RichTextBlock", true, MarkdownInlineFlow.RequiresRichText(Inlines(body)));
    }

    /// <summary>③ 链接里裹着表情：<c>Hyperlink</c> 承载不了图片，也要判 RichTextBlock。</summary>
    private static void CheckLinkEmoticonNeedsRichText()
    {
        var body = HtmlDescriptionParser.Parse(
            "<a href=\"https://example.com/\">look <img src=\"" + EmoticonUrl + "\" alt=\":highrise:\" class=\"emoticon\"></a>");
        ChecksContext.Equal("排版：链接里带表情 → 需要 RichTextBlock", true, MarkdownInlineFlow.RequiresRichText(Inlines(body)));
        ChecksContext.Equal("排版：链接里带表情 → 不算正文配图块", false, MarkdownInlineFlow.HasBlockImage(Inlines(body)));
    }

    /// <summary>④ 纯文字评论不能平白升级成 RichTextBlock（保持原来的 TextBlock 路径）。</summary>
    private static void CheckPlainTextStaysTextBlock()
    {
        var body = HtmlDescriptionParser.Parse("<b>just words</b> and <i>more</i>");
        ChecksContext.Equal("排版：纯文字 → 不需要 RichTextBlock", false, MarkdownInlineFlow.RequiresRichText(Inlines(body)));
        ChecksContext.Equal("排版：纯文字 → 没有正文配图块", false, MarkdownInlineFlow.HasBlockImage(Inlines(body)));
    }

    /// <summary>⑤ 普通配图仍单独成块，且不被当成表情。</summary>
    private static void CheckBlockImageStaysBlock()
    {
        var body = HtmlDescriptionParser.Parse("<img src=\"" + ScreenshotUrl + "\" alt=\"shot\">");
        ChecksContext.Equal("排版：普通配图 → 需要正文配图块", true, MarkdownInlineFlow.HasBlockImage(Inlines(body)));
        ChecksContext.Equal("排版：普通配图 → 不需要 RichTextBlock", false, MarkdownInlineFlow.RequiresRichText(Inlines(body)));
        ChecksContext.Equal("排版：普通配图 → 判定为 Block", MarkdownImageLayout.Block, MarkdownInlineFlow.PlanImage(ScreenshotUrl, "shot").Layout);

        // Steam 也有 <a><img></a> 的单图包装：仍算正文配图块。
        var wrapped = HtmlDescriptionParser.Parse("<a href=\"https://example.com/\"><img src=\"" + ScreenshotUrl + "\" alt=\"shot\"></a>");
        ChecksContext.Equal("排版：链接包住的单张配图 → 仍算正文配图块", true, MarkdownInlineFlow.HasBlockImage(Inlines(wrapped)));
    }

    /// <summary>⑥ 表情 / 失效表情 / 普通图的排法与地址规范化。</summary>
    private static void CheckImagePlans()
    {
        var emoticon = MarkdownInlineFlow.PlanImage(EmoticonUrl, ":highrise:");
        ChecksContext.Equal("排版：官方表情 → 行内表情", MarkdownImageLayout.InlineEmoticon, emoticon.Layout);
        ChecksContext.Equal("排版：官方表情 → 短码", ":highrise:", emoticon.ShortCode);
        ChecksContext.Equal("排版：官方表情 → 地址原样", EmoticonUrl, emoticon.Url);
        ChecksContext.Equal("排版：行内表情不算正文配图", true, emoticon.IsInline);

        var akamai = MarkdownInlineFlow.PlanImage(AkamaiEmoticonUrl, ":highrise:");
        ChecksContext.Equal("排版：akamai 表情 → 行内表情", MarkdownImageLayout.InlineEmoticon, akamai.Layout);
        ChecksContext.Equal("排版：akamai 表情 → 规范化到 fastly", EmoticonUrl, akamai.Url);

        var broken = MarkdownInlineFlow.PlanImage("https://example.com/broken.png", ":nope:");
        ChecksContext.Equal("排版：失效表情 → 退短码", MarkdownImageLayout.ShortCode, broken.Layout);
        ChecksContext.Equal("排版：失效表情 → 短码文本", ":nope:", broken.ShortCode);

        var plain = MarkdownInlineFlow.PlanImage(ScreenshotUrl, "shot");
        ChecksContext.Equal("排版：普通图 → 无短码", string.Empty, plain.ShortCode);
        ChecksContext.Equal("排版：普通图不算行内", false, plain.IsInline);
    }

    /// <summary>⑦ 表情尺寸只有一个来源：界面控件尺寸、解码宽度、行高都取它。</summary>
    private static void CheckEmoticonSize()
    {
        ChecksContext.Equal("排版：表情边长固定 20px", 20d, MarkdownInlineFlow.EmoticonSize);
    }

    /// <summary>取文档第一段的行内流（这些用例每段只有一段）。</summary>
    private static IReadOnlyList<MarkdownInline> Inlines(MarkdownDocument document) =>
        document.Blocks.OfType<MarkdownParagraph>().FirstOrDefault()?.Inlines ?? [];
}
