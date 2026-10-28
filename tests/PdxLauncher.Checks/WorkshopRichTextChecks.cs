namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Text;

/// <summary>
/// 详情页「富文本」三件套自检：Markdown 子集解析器、Steam 描述的 HTML 解析器、
/// 以及 Steam 物品页（作者/描述配图/评论）的抓取解析。
/// <para>
/// 这几个解析器全在 Core、纯字符串进出，所以直接在这里跑真实样例；
/// 它们的共同约定是「解析不出来就给空结果，绝不抛异常」——界面靠这个静默降级。
/// </para>
/// </summary>
public static class WorkshopRichTextChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckMarkdownBlocks();
        CheckMarkdownInlines();
        CheckMarkdownListsAndCode();
        CheckMarkdownPlainText();
        CheckMarkdownTruncation();
        CheckHtmlDescription();
        CheckItemPage();
        CheckItemPageScreenshots();
        CheckCommentPaging();
        return ChecksContext.Failures - before;
    }

    /// <summary>块级：标题、段落、引用、分隔线。</summary>
    private static void CheckMarkdownBlocks()
    {
        var document = MarkdownParser.Parse("# 一级\n\n正文一段\n\n> 引用\n\n---\n");

        ChecksContext.Equal("空输入给空文档", true, MarkdownParser.Parse(null).IsEmpty);
        ChecksContext.Equal("空白输入给空文档", true, MarkdownParser.Parse("   \n\n ").IsEmpty);
        ChecksContext.Equal("有 4 个块", 4, document.Blocks.Count);

        var heading = document.Blocks[0] as MarkdownHeading;
        ChecksContext.Equal("第一个块是标题", true, heading is not null);
        ChecksContext.Equal("标题级别 1", 1, heading?.Level ?? -1);
        ChecksContext.Equal("标题文字", "一级", heading?.Inlines is [MarkdownTextInline { Text: "一级" }] ? "一级" : "?");

        ChecksContext.Equal("第二个块是段落", true, document.Blocks[1] is MarkdownParagraph);
        ChecksContext.Equal("第三个块是引用", true, document.Blocks[2] is MarkdownQuote);
        ChecksContext.Equal("第四个块是分隔线", true, document.Blocks[3] is MarkdownRule);
    }

    /// <summary>行内：粗体、斜体、删除线、行内代码、链接、图片。</summary>
    private static void CheckMarkdownInlines()
    {
        var document = MarkdownParser.Parse(
            "**粗** *斜* ~~删~~ `code` [名字](https://example.com/a) ![图](https://example.com/i.png)");

        var paragraph = document.Blocks[0] as MarkdownParagraph;
        ChecksContext.Equal("一个段落", true, paragraph is not null);

        var inlines = paragraph?.Inlines ?? [];
        ChecksContext.Equal("粗体节点", true, inlines.OfType<MarkdownStyledInline>().Any(static item => item.Style == MarkdownInlineStyle.Bold));
        ChecksContext.Equal("斜体节点", true, inlines.OfType<MarkdownStyledInline>().Any(static item => item.Style == MarkdownInlineStyle.Italic));
        ChecksContext.Equal("删除线节点", true, inlines.OfType<MarkdownStyledInline>().Any(static item => item.Style == MarkdownInlineStyle.Strikethrough));
        ChecksContext.Equal("行内代码", "code", inlines.OfType<MarkdownCodeInline>().FirstOrDefault()?.Text);

        var link = inlines.OfType<MarkdownLinkInline>().FirstOrDefault();
        ChecksContext.Equal("链接地址", "https://example.com/a", link?.Url);
        ChecksContext.Equal("链接文字", "名字", link?.Inlines is [MarkdownTextInline { Text: "名字" }] ? "名字" : "?");

        ChecksContext.Equal(
            "图片进了 ImageUrls",
            "https://example.com/i.png",
            document.ImageUrls.Count == 1 ? document.ImageUrls[0] : string.Join(",", document.ImageUrls));
    }

    /// <summary>列表（含嵌套与有序）和围栏代码块。</summary>
    private static void CheckMarkdownListsAndCode()
    {
        var document = MarkdownParser.Parse(
            "- 一\n- 二\n    - 二点一\n\n1. 甲\n2. 乙\n\n```cs\nvar x = 1;\n```\n");

        var items = document.Blocks.OfType<MarkdownListItem>().ToList();
        ChecksContext.Equal("5 个列表项", 5, items.Count);
        ChecksContext.Equal("第三条缩进一级", 1, items.Count > 2 ? items[2].Level : -1);
        ChecksContext.Equal("第三条无序", false, items.Count > 2 && items[2].Ordered);
        ChecksContext.Equal("有序列表第一条序号 1", 1, items.Count > 3 ? items[3].Number : -1);
        ChecksContext.Equal("有序列表第二条序号 2", 2, items.Count > 4 ? items[4].Number : -1);

        var code = document.Blocks.OfType<MarkdownCodeBlock>().FirstOrDefault();
        ChecksContext.Equal("代码块内容", "var x = 1;", code?.Text?.Trim());
        ChecksContext.Equal("代码块语言", "cs", code?.Language);
    }

    /// <summary>纯文本视图：链接降级成「文字（地址）」。</summary>
    private static void CheckMarkdownPlainText()
    {
        var document = MarkdownParser.Parse("# 标题\n\n看 [这里](https://example.com/x) 和 **粗体**\n");
        var plain = document.ToPlainText();

        ChecksContext.Check("纯文本含标题", plain.Contains("标题", StringComparison.Ordinal), plain);
        ChecksContext.Check("纯文本含链接地址", plain.Contains("https://example.com/x", StringComparison.Ordinal), plain);
        ChecksContext.Check("纯文本含粗体文字", plain.Contains("粗体", StringComparison.Ordinal), plain);
        ChecksContext.Check("纯文本不含星号", !plain.Contains("**", StringComparison.Ordinal), plain);
    }

    /// <summary>超长描述必须截断并打标记，避免一屏渲染几万字。</summary>
    private static void CheckMarkdownTruncation()
    {
        var huge = new string('字', MarkdownParser.MaxInputLength + 500);
        var document = MarkdownParser.Parse(huge);

        ChecksContext.Equal("超长输入标记为截断", true, document.IsTruncated);
        ChecksContext.Equal("不截断时不标记", false, MarkdownParser.Parse("短文本").IsTruncated);
    }

    /// <summary>Steam 描述是 HTML（&lt;br&gt;、&lt;a&gt;、&lt;img&gt;）。</summary>
    private static void CheckHtmlDescription()
    {
        var document = HtmlDescriptionParser.Parse(
            "<h2>标题</h2><p>第一段<br>第二行</p><ul><li>条目</li></ul>"
            + "<p><b>加粗</b>与<a href=\"https://example.com\">链接</a></p>"
            + "<p><img src=\"https://images.steamusercontent.com/a.png\"></p>");

        var plain = document.ToPlainText();
        ChecksContext.Check("HTML 标题保留", plain.Contains("标题", StringComparison.Ordinal), plain);
        ChecksContext.Check("HTML 段落保留", plain.Contains("第一段", StringComparison.Ordinal), plain);
        ChecksContext.Check("HTML 列表保留", plain.Contains("条目", StringComparison.Ordinal), plain);
        ChecksContext.Check("HTML 标签被剥掉", !plain.Contains("<p>", StringComparison.Ordinal), plain);

        ChecksContext.Equal(
            "HTML 里的图片进 ImageUrls",
            "https://images.steamusercontent.com/a.png",
            document.ImageUrls.Count == 1 ? document.ImageUrls[0] : string.Join(",", document.ImageUrls));
        ChecksContext.Equal("空 HTML 给空文档", true, HtmlDescriptionParser.Parse(null).IsEmpty);

        // Steam 正文里的配图（<img><br><文字>）：图片必须是正文的一部分，不能只被收集：
        // 一个「只剩图片」的简介也不该被判成空文档，否则整段正文会被隐藏。
        var mixed = HtmlDescriptionParser.Parse(
            "<img src=\"https://i.imgur.com/NrstAKw.gif\" /><br><br><div class=\"bb_h1\">标题</div><br>Renovatio adds…");
        ChecksContext.Equal(
            "正文里的第一块就是图片",
            true,
            mixed.Blocks.Count > 0
            && mixed.Blocks[0] is MarkdownParagraph { Inlines.Count: > 0 } first
            && first.Inlines[0] is MarkdownImageInline);
        ChecksContext.Equal(
            "正文配图顺序正确",
            "https://i.imgur.com/NrstAKw.gif",
            mixed.ImageUrls.Count > 0 ? mixed.ImageUrls[0] : string.Join(",", mixed.ImageUrls));

        var imageOnly = HtmlDescriptionParser.Parse("<img src=\"https://i.imgur.com/only.png\" />");
        ChecksContext.Equal("只有图片的简介不算空", false, imageOnly.IsEmpty);
        ChecksContext.Equal("只有图片也进 ImageUrls", 1, imageOnly.ImageUrls.Count);
    }

    /// <summary>物品页解析：作者、描述配图、评论与总数。</summary>
    private static void CheckItemPage()
    {
        const string Html = """
            <div class="creatorsBlock">
              <a class="friendBlockLinkOverlay" href="https://steamcommunity.com/profiles/76561198000000000"></a>
              <div class="friendBlockContent">Agami</div>
            </div>
            <div class="workshopItemDescription">描述里的图
              <img src="https://images.steamusercontent.com/a.png">
              <img src="https://avatars.fastly.steamstatic.com/ignored.jpg">
            </div>
            <div id="_posts">
              <div class="commentthread_comment " id="comment_1">
                <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/alice"><bdi>Alice</bdi></a>
                <div class="commentthread_comment_timestamp" data-timestamp="1700000000">时间</div>
                <div class="commentthread_comment_text">第一条评论</div>
              </div>
              <div class="commentthread_comment " id="comment_2">
                <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/bob"><bdi>Bob</bdi></a>
                <div class="commentthread_comment_text">第二条评论</div>
              </div>
            </div>
            <script>{"total_count":136}</script>
            """;

        var page = SteamWorkshopItemPageParser.Parse(Html);
        ChecksContext.Equal("物品页解析成功", true, page is not null);
        ChecksContext.Equal("作者名", "Agami", page?.AuthorName);
        ChecksContext.Equal("作者主页", "https://steamcommunity.com/profiles/76561198000000000", page?.AuthorProfileUrl);
        ChecksContext.Equal("评论总数", 136, page?.TotalComments ?? -1);
        ChecksContext.Equal("抓到两条评论", 2, page?.Comments.Count ?? -1);
        ChecksContext.Equal("第一条评论作者", "Alice", page?.Comments[0].AuthorName);
        ChecksContext.Equal("第一条评论作者主页", "https://steamcommunity.com/id/alice", page?.Comments[0].AuthorProfileUrl);
        ChecksContext.Equal("第一条评论 id", "1", page?.Comments[0].Id);
        ChecksContext.Check(
            "第一条评论时间已解析",
            page is not null && page.Comments[0].PostedUtc > DateTimeOffset.UnixEpoch,
            page?.Comments[0].PostedUtc.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
        ChecksContext.Equal("第一条评论正文", "第一条评论", page?.Comments[0].TextPlain);
        ChecksContext.Equal("第二条评论正文", "第二条评论", page?.Comments[1].TextPlain);

        // 头像域名不进入配图；描述里的图只留一张。
        ChecksContext.Equal("描述配图只留非头像图", 1, page?.ImageUrls.Count ?? -1);

        ChecksContext.Equal("空 HTML 返回 null", null, SteamWorkshopItemPageParser.Parse(null));
        ChecksContext.Equal("无关 HTML 返回 null", null, SteamWorkshopItemPageParser.Parse("<html><body>hello</body></html>"));
    }
    private static void CheckItemPageScreenshots()
    {
        // 画廊缩略图顺序 ≠ 脚本数组顺序：真实页面里数组常被重排，必须按 id 顺序取。
        const string Html = """
            <div class="workshopItemPreviewArea">
              <div id="highlight_player_area">
                <div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_300">
                  <img src="https://community.steamstatic.com/public/images/trans.gif">
                </div>
                <div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_100">
                  <img src="https://community.steamstatic.com/public/images/trans.gif">
                </div>
                <div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_200">
                  <img src="https://community.steamstatic.com/public/images/trans.gif">
                </div>
                <div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_999">
                  <img src="https://community.steamstatic.com/public/images/trans.gif">
                </div>
              </div>
            </div>
            <div class="workshopItemDescription">正文图
              <img src="https://i.imgur.com/desc.png">
            </div>
            <div class="creatorsBlock">
              <a class="friendBlockLinkOverlay" href="https://steamcommunity.com/profiles/76561198000000000"></a>
              <div class="friendBlockContent">Agami</div>
            </div>
            <script>
              var rgScreenshotURLs = {
                '300': 'https://images.steamusercontent.com/ugc/300/?imw=637&imh=358',
                '100': 'https://images.steamusercontent.com/ugc/100/?imw=637&imh=358',
                '200': 'https://images.steamusercontent.com/ugc/200/?imw=637&imh=358'
              };
              var rgFullScreenshotURLs = [
                { 'previewid' : '200', 'url': 'https://images.steamusercontent.com/ugc/200/?imw=5000&imh=5000&letterbox=false' },
                { 'previewid' : '100', 'url': 'https://images.steamusercontent.com/ugc/100/?imw=5000&imh=5000&letterbox=false' },
                { 'previewid' : '300', 'url': 'https://images.steamusercontent.com/ugc/300/?imw=5000&imh=5000&letterbox=false' }
              ];
            </script>
            """;

        var page = SteamWorkshopItemPageParser.Parse(Html);
        ChecksContext.Equal("画廊截图数量（缺 url 的 id 跳过）", 3, page?.ScreenshotUrls.Count ?? -1);
        ChecksContext.Equal(
            "画廊顺序按页面 id",
            "https://images.steamusercontent.com/ugc/300/?imw=5000&imh=5000&letterbox=false",
            page?.ScreenshotUrls[0]);
        ChecksContext.Equal(
            "画廊第二张",
            "https://images.steamusercontent.com/ugc/100/?imw=5000&imh=5000&letterbox=false",
            page?.ScreenshotUrls[1]);
        ChecksContext.Equal(
            "画廊第三张",
            "https://images.steamusercontent.com/ugc/200/?imw=5000&imh=5000&letterbox=false",
            page?.ScreenshotUrls[2]);
        ChecksContext.Check(
            "用原图尺寸而不是 637 预览版",
            page is not null && page.ScreenshotUrls.All(url => url.Contains("imw=5000", StringComparison.Ordinal)),
            page?.ScreenshotUrls[0]);
        ChecksContext.Check(
            "描述里的图不进画廊",
            page is not null && page.ScreenshotUrls.All(url => !url.Contains("i.imgur.com", StringComparison.Ordinal)),
            null);
        ChecksContext.Check(
            "描述里的图仍在 ImageUrls",
            page is not null && page.ImageUrls.Any(url => url.Contains("i.imgur.com/desc.png", StringComparison.Ordinal)),
            page?.ImageUrls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // 只有 637 字典、没有 full 数组时兜底用字典。
        const string FallbackHtml = """
            <div id="highlight_player_area">
              <div class="highlight_player_item highlight_screenshot" id="highlight_screenshot_7"></div>
            </div>
            <script>
              var rgScreenshotURLs = { '7': 'https://images.steamusercontent.com/ugc/7/?imw=637&imh=358' };
            </script>
            <div class="friendBlockContent">Agami</div>
            """;
        var fallback = SteamWorkshopItemPageParser.Parse(FallbackHtml);
        ChecksContext.Equal("缺 full 数组时用字典兜底", 1, fallback?.ScreenshotUrls.Count ?? -1);

        // 没有画廊区的页面（只有视频/改版）→ 空列表，调用方退回封面。
        var noGallery = SteamWorkshopItemPageParser.Parse(
            "<div class=\"friendBlockContent\">Agami</div><div class=\"workshopItemDescription\">纯文字</div>");
        ChecksContext.Equal("没有画廊区时为空", 0, noGallery?.ScreenshotUrls.Count ?? -1);
        ChecksContext.Equal("空 HTML 时画廊为空", 0, SteamWorkshopItemPageParser.Parse(null)?.ScreenshotUrls.Count ?? 0);
    }

    private static void CheckCommentPaging()
    {
        // 翻页接口返回的是裸片段（没有物品页外壳），解析器必须能直接吃。
        const string Fragment = """
            <div class="commentthread_comment " id="comment_3">
              <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/carol"><bdi>Carol</bdi></a>
              <div class="commentthread_comment_timestamp" data-timestamp="1700003600">时间</div>
              <div class="commentthread_comment_text">第三页第一条</div>
            </div>
            <div class="commentthread_comment " id="comment_4">
              <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/dave"><bdi>Dave</bdi></a>
              <div class="commentthread_comment_text">第三页第二条</div>
            </div>
            """;

        var comments = SteamWorkshopItemPageParser.ParseComments(Fragment);
        ChecksContext.Equal("翻页片段: 解析两条", 2, comments.Count);
        ChecksContext.Equal("翻页片段: 第一条作者", "Carol", comments.Count > 0 ? comments[0].AuthorName : null);
        ChecksContext.Equal("翻页片段: 第一条正文", "第三页第一条", comments.Count > 0 ? comments[0].TextPlain : null);
        ChecksContext.Equal("翻页片段: 空输入返回空列表", 0, SteamWorkshopItemPageParser.ParseComments(null).Count);

        // 上限截断：翻页接口一次给太多也不该拖死界面。
        var capped = SteamWorkshopItemPageParser.ParseComments(Fragment, 1);
        ChecksContext.Equal("翻页片段: 按 maxComments 截断", 1, capped.Count);

        ChecksContext.Equal(
            "翻页片段: total_count 解析",
            136,
            SteamWorkshopItemPageParser.ParseCommentTotal("{\"total_count\":136,\"success\":true}"));
        ChecksContext.Equal(
            "翻页片段: 无 total_count 返回 0",
            0,
            SteamWorkshopItemPageParser.ParseCommentTotal("{\"success\":true}"));
        ChecksContext.Equal("翻页片段: total_count 空输入返回 0", 0, SteamWorkshopItemPageParser.ParseCommentTotal(null));
    }
}
