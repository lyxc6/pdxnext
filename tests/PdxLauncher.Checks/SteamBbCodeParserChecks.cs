namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Text;

/// <summary>
/// Steam 描述 BBCode 解析器自检：<see cref="SteamBbCodeParser"/> 把作者写在创意工坊描述里的
/// 标记翻成详情页 / 上传预览共用的 <see cref="MarkdownDocument"/>。
/// <para>
/// 口径：认识的标签转成对应的块或行内节点；**不认识的方括号原样留在正文里**
/// （作者常写 <c>[1.13]</c> 这种版本号，被当成标签吃掉就成了「无中生有」的 bug）。
/// </para>
/// <para>纯内存用例，不读写磁盘，不依赖网络。</para>
/// </summary>
public static class SteamBbCodeParserChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckBasicParagraphs();
        CheckHeadings();
        CheckRules();
        CheckLists();
        CheckInlineStyles();
        CheckLinks();
        CheckImages();
        CheckUnknownTagsStay();
        CheckNoparse();
        CheckTruncation();
        return ChecksContext.Failures - before;
    }

    private static void CheckBasicParagraphs()
    {
        ChecksContext.Check("null 解析成空文档", SteamBbCodeParser.Parse(null).IsEmpty);
        ChecksContext.Check("纯空白解析成空文档", SteamBbCodeParser.Parse("  \r\n\t ").IsEmpty);

        var document = SteamBbCodeParser.Parse("一段没有标记的描述");
        ChecksContext.Equal("纯文本只有一段", 1, document.Blocks.Count);
        ChecksContext.Check(
            "纯文本是一段落",
            document.Blocks[0] is MarkdownParagraph);
        ChecksContext.Equal("纯文本原样保留", "一段没有标记的描述", document.ToPlainText());
    }

    private static void CheckHeadings()
    {
        var document = SteamBbCodeParser.Parse("[h2]安装步骤[/h2]");
        ChecksContext.Equal("h2 只产出一个标题块", 1, document.Blocks.Count);

        var heading = document.Blocks[0] as MarkdownHeading;
        ChecksContext.Check("h2 解析成标题块", heading is not null);
        ChecksContext.Equal("h2 级别是 2", 2, heading?.Level ?? 0);
        ChecksContext.Equal("标题文字", "安装步骤", document.ToPlainText());

        var closed = SteamBbCodeParser.Parse("[h1]只有开头");
        var bare = closed.Blocks[0] as MarkdownHeading;
        ChecksContext.Check("缺结束标签也能解析标题", bare is not null);
        ChecksContext.Equal("缺结束标签时取第一行", "只有开头", closed.ToPlainText());
    }

    private static void CheckRules()
    {
        var document = SteamBbCodeParser.Parse("上文\r\n[hr]\r\n下文");
        ChecksContext.Check(
            "分隔线解析成水平线块",
            document.Blocks.Any(block => block is MarkdownRule));
        ChecksContext.Check(
            "分隔线两侧的正文都在",
            document.ToPlainText().Contains("上文", StringComparison.Ordinal)
            && document.ToPlainText().Contains("下文", StringComparison.Ordinal));

        ChecksContext.Check(
            "自闭合的 hr 也认",
            SteamBbCodeParser.Parse("[hr/]").Blocks.Any(block => block is MarkdownRule));
    }

    private static void CheckLists()
    {
        var document = SteamBbCodeParser.Parse("[list][*]甲[*]乙[/list]");
        var items = document.Blocks.Where(block => block is MarkdownListItem).ToArray();
        ChecksContext.Equal("无序列表两条", 2, items.Length);
        ChecksContext.Check(
            "无序列表条目标记为无序",
            items.All(item => item is MarkdownListItem { Ordered: false }));
        ChecksContext.Check(
            "列表条目文字保留",
            document.ToPlainText().Contains("甲", StringComparison.Ordinal)
            && document.ToPlainText().Contains("乙", StringComparison.Ordinal));

        var ordered = SteamBbCodeParser.Parse("[olist][*]第一步[*]第二步[/olist]");
        var orderedItems = ordered.Blocks.Where(block => block is MarkdownListItem).ToArray();
        ChecksContext.Equal("有序列表两条", 2, orderedItems.Length);
        ChecksContext.Check(
            "有序列表条目标记为有序",
            orderedItems.All(item => item is MarkdownListItem { Ordered: true }));
    }

    private static void CheckInlineStyles()
    {
        var document = SteamBbCodeParser.Parse("[b]重点[/b]普通");
        var styled = document.Blocks
            .OfType<MarkdownParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<MarkdownStyledInline>()
            .ToArray();

        ChecksContext.Check("粗体解析成强调节点", styled.Length == 1);
        ChecksContext.Check("粗体样式是 Bold", styled.FirstOrDefault()?.Style == MarkdownInlineStyle.Bold);
        ChecksContext.Equal("粗体文字与后文拼接", "重点普通", document.ToPlainText());

        var strikethrough = SteamBbCodeParser.Parse("[strike]删掉[/strike]");
        ChecksContext.Check(
            "删除线解析成强调节点",
            strikethrough.Blocks
                .OfType<MarkdownParagraph>()
                .SelectMany(paragraph => paragraph.Inlines)
                .OfType<MarkdownStyledInline>()
                .Any(inline => inline.Style == MarkdownInlineStyle.Strikethrough));
    }

    private static void CheckLinks()
    {
        var document = SteamBbCodeParser.Parse("看[url=https://example.com/a]这里[/url]");
        var link = document.Blocks
            .OfType<MarkdownParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<MarkdownLinkInline>()
            .FirstOrDefault();

        ChecksContext.Check("url 标签解析成链接节点", link is not null);
        ChecksContext.Equal("链接地址", "https://example.com/a", link?.Url ?? string.Empty);
        ChecksContext.Equal("链接文字先于标签文字", "看", document.ToPlainText()[..1]);
    }

    private static void CheckImages()
    {
        var document = SteamBbCodeParser.Parse("[img]https://images.example.com/a.png[/img]");
        var image = document.Blocks
            .OfType<MarkdownParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<MarkdownImageInline>()
            .FirstOrDefault();

        ChecksContext.Check("img 标签解析成图片节点", image is not null);
        ChecksContext.Equal(
            "图片地址",
            "https://images.example.com/a.png",
            image?.Url ?? string.Empty);

        var video = SteamBbCodeParser.Parse("[previewyoutube=dQw4w9WgXcQ][/previewyoutube]");
        var thumbnail = video.Blocks
            .OfType<MarkdownParagraph>()
            .SelectMany(paragraph => paragraph.Inlines)
            .OfType<MarkdownImageInline>()
            .FirstOrDefault();
        ChecksContext.Check(
            "视频标签落成缩略图",
            thumbnail?.Url.Contains("dQw4w9WgXcQ", StringComparison.Ordinal) == true);
    }

    private static void CheckUnknownTagsStay()
    {
        var document = SteamBbCodeParser.Parse("[1.13] 修了崩溃");
        ChecksContext.Check(
            "版本号方括号原样保留",
            document.ToPlainText().Contains("[1.13]", StringComparison.Ordinal));

        var table = SteamBbCodeParser.Parse("[table][tr][td]甲[/td][/tr][/table]");
        ChecksContext.Check(
            "不认识的块标签不被吞掉",
            table.ToPlainText().Contains("[table]", StringComparison.Ordinal));
    }

    private static void CheckNoparse()
    {
        var document = SteamBbCodeParser.Parse("[noparse][b]原样[/b][/noparse]");
        ChecksContext.Check(
            "noparse 里的标签当文字",
            document.ToPlainText().Contains("[b]原样[/b]", StringComparison.Ordinal));
    }

    private static void CheckTruncation()
    {
        var longText = new string('甲', SteamBbCodeParser.MaxInputLength + 100);
        var document = SteamBbCodeParser.Parse(longText);
        ChecksContext.Check("超长输入标记为截断", document.IsTruncated);

        var normal = SteamBbCodeParser.Parse("短文本");
        ChecksContext.Check("正常输入不算截断", !normal.IsTruncated);
    }
}
