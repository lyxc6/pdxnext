namespace PdxLauncher.Core.Text;

using PdxLauncher.Core.Online;

/// <summary>行内图片在界面上的排法。</summary>
public enum MarkdownImageLayout
{
    /// <summary>独占一行的正文配图（占位灰块 + 点击查看）。</summary>
    Block,

    /// <summary>贴文字行内的官方表情（20×20 小图，未下载时显示短码）。</summary>
    InlineEmoticon,

    /// <summary>失效表情：只留 <c>:name:</c> 短码文本。</summary>
    ShortCode,
}

/// <summary>一张行内图片的渲染方案。</summary>
/// <param name="Layout">排法。</param>
/// <param name="Url">实际要下载/显示的地址（表情已规范化到 fastly）。</param>
/// <param name="ShortCode">行内表情的短码（其它排法为空串）。</param>
public readonly record struct MarkdownImagePlan(MarkdownImageLayout Layout, string Url, string ShortCode)
{
    /// <summary>是否贴文字行内（表情或短码），即不单独成块。</summary>
    public bool IsInline => Layout != MarkdownImageLayout.Block;
}

/// <summary>
/// 行内流的渲染判定：一段行内里有没有图片、图片该贴行内还是单独成块、以及这段行内
/// 能不能塞进 <c>TextBlock</c>。
/// <para>
/// 这些判定必须和 <c>MarkdownView.AppendInlines</c> 的实际写法一致，否则界面会在运行时
/// 把 <c>InlineUIContainer</c> 塞进只认纯文本的 <c>TextBlock.Inlines</c>（WinUI 只允许
/// <c>RichTextBlock</c> 承载它），表现为整段评论不渲染甚至抛异常。判定放在 Core 里是为了
/// 能被自检钉住——它错了不会崩在编译期，只会在真机上看不见东西。
/// </para>
/// </summary>
public static class MarkdownInlineFlow
{
    /// <summary>行内表情的边长（px）。图片解码宽度、控件尺寸与行高都取这一个值。</summary>
    public const double EmoticonSize = 20;

    /// <summary>给一张行内图片定排法：官方表情贴行内、alt 是短码的失效图退短码、其它单独成块。</summary>
    /// <param name="url">图片地址。</param>
    /// <param name="alt">图片替代文本（Steam 给 <c>:name:</c>）。</param>
    public static MarkdownImagePlan PlanImage(string? url, string? alt) =>
        SteamEmoticon.TryCreate(url, alt, out var normalized, out var shortCode)
            ? new MarkdownImagePlan(MarkdownImageLayout.InlineEmoticon, normalized, shortCode)
            : SteamEmoticon.TryGetShortCode(alt, out var fallback)
                ? new MarkdownImagePlan(MarkdownImageLayout.ShortCode, url ?? string.Empty, fallback)
                : new MarkdownImagePlan(MarkdownImageLayout.Block, url ?? string.Empty, string.Empty);

    /// <summary>
    /// 这段行内流里有没有「必须用 <c>RichTextBlock</c> 才放得下」的东西：只要扁平化之后
    /// 存在官方表情就是。样式/链接**无条件**递归——渲染侧会把任意层样式展平，
    /// 所以 <c>&lt;b&gt;nice &lt;img class="emoticon"&gt;&lt;/b&gt;</c> 也算。
    /// </summary>
    /// <param name="inlines">行内流。</param>
    public static bool RequiresRichText(IReadOnlyList<MarkdownInline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        return Enumerate(inlines).Any(static inline =>
            inline is MarkdownImageInline image
            && PlanImage(image.Url, image.Alt).Layout == MarkdownImageLayout.InlineEmoticon);
    }

    /// <summary>
    /// 是否还有「需要单独成块」的图片：只认能整段归结为一张图的节点
    /// （图片本身，或被链接/样式整段包住的单张图），且那张图不是行内表情/短码。
    /// </summary>
    /// <param name="inlines">行内流。</param>
    public static bool HasBlockImage(IReadOnlyList<MarkdownInline> inlines)
    {
        ArgumentNullException.ThrowIfNull(inlines);
        return inlines.Any(static inline => TryGetBlockImage(inline, out _));
    }

    /// <summary>
    /// 取出一个行内节点里「唯一」的那张图：它本身就是图片，或整段被链接/样式包住
    /// （Steam 有 <c>&lt;a&gt;&lt;img&gt;&lt;/a&gt;</c>），并且这张图要单独成块。
    /// 包住的子节点不止一个时不算——那时它和文字混排，归行内流处理。
    /// </summary>
    /// <param name="inline">行内节点。</param>
    /// <param name="image">解析出的图片。</param>
    public static bool TryGetBlockImage(MarkdownInline? inline, out MarkdownImageInline? image)
    {
        switch (inline)
        {
            case MarkdownImageInline direct when !string.IsNullOrWhiteSpace(direct.Url):
                if (PlanImage(direct.Url, direct.Alt).Layout == MarkdownImageLayout.Block)
                {
                    image = direct;
                    return true;
                }

                break;

            case MarkdownLinkInline link when link.Inlines.Count == 1:
                return TryGetBlockImage(link.Inlines[0], out image);

            case MarkdownStyledInline styled when styled.Inlines.Count == 1:
                return TryGetBlockImage(styled.Inlines[0], out image);
        }

        image = null;
        return false;
    }

    /// <summary>按出现顺序扁平化行内流（样式与链接都展开）。</summary>
    private static IEnumerable<MarkdownInline> Enumerate(IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            switch (inline)
            {
                case MarkdownLinkInline link:
                    foreach (var child in Enumerate(link.Inlines))
                    {
                        yield return child;
                    }

                    break;

                case MarkdownStyledInline styled:
                    foreach (var child in Enumerate(styled.Inlines))
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }
}
