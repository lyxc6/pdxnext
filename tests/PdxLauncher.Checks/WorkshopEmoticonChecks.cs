namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Text;

/// <summary>
/// 创意工坊评论里的 Steam 表情：真实评论 HTML → 图片 + 短码、失效地址只留短码、
/// &lt;br&gt; 仍换行、白名单只放行精确 fastly 主机 + 表情路径（akamai 别名只规范化不放大放行面）。
/// </summary>
public static class WorkshopEmoticonChecks
{
    private const string EmoticonUrl = "https://community.fastly.steamstatic.com/economy/emoticon/highrise";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckRealComment();
        CheckAkamaiCommentAlias();
        CheckPlainCommentUnchanged();
        CheckInvalidSourcesFallBackToShortCode();
        CheckLineBreak();
        CheckWhitelist();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>① 真实评论片段：评论 HTML 保留、AST 里有表情图片和短码。</summary>
    private static void CheckRealComment()
    {
        const string Html = """
            <div class="commentthread_comment " id="comment_1">
              <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/alice"><bdi>Alice</bdi></a>
              <div class="commentthread_comment_timestamp" data-timestamp="1700000000">时间</div>
              <div class="commentthread_comment_text">Nice <img src="https://community.fastly.steamstatic.com/economy/emoticon/highrise" alt=":highrise:" class="emoticon"> work</div>
            </div>
            """;

        var comments = SteamWorkshopItemPageParser.ParseComments(Html);
        ChecksContext.Equal("表情：评论解析出一条", 1, comments.Count);
        if (comments.Count != 1)
        {
            return;
        }

        var comment = comments[0];
        ChecksContext.Check("表情：评论 TextHtml 仍保留 img 标签", comment.TextHtml.Contains("<img", StringComparison.Ordinal), comment.TextHtml);

        var body = HtmlDescriptionParser.Parse(comment.TextHtml);
        ChecksContext.Equal("表情：评论 AST 收集到表情 URL", EmoticonUrl, body.ImageUrls.Count == 1 ? body.ImageUrls[0] : string.Join(",", body.ImageUrls));

        var image = AssertSingleImage(body);
        ChecksContext.Equal("表情：AST 保留 alt 短码", ":highrise:", image?.Alt);
        ChecksContext.Check(
            "表情：短码可直接用于纯文本",
            body.ToPlainText().Contains(":highrise:", StringComparison.Ordinal),
            body.ToPlainText());

        ChecksContext.Check("表情：URL 被判定为官方表情", SteamEmoticon.IsEmoticonImage(EmoticonUrl, ":highrise:"));
        ChecksContext.Check(
            "表情：TryCreate 给出规范化 URL 与短码",
            SteamEmoticon.TryCreate(EmoticonUrl, ":highrise:", out var normalized, out var shortCode)
            && normalized == EmoticonUrl
            && shortCode == ":highrise:",
            normalized + " / " + shortCode);
    }

    /// <summary>
    /// ①b 线上实际页面（3822538803）返回的是 akamai 别名；评论 HTML 仍要能出表情，
    /// 且下载 URL 必须规范化回白名单里的 fastly。
    /// </summary>
    private static void CheckAkamaiCommentAlias()
    {
        const string Html = """
            <div class="commentthread_comment " id="comment_3">
              <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/carol"><bdi>Carol</bdi></a>
              <div class="clickable commentthread_comment_timestamp" data-timestamp="1700000001">时间</div>
              <div class="commentthread_comment_text">Nice <img src="https://community.akamai.steamstatic.com/economy/emoticon/highrise" alt=":highrise:" class="emoticon"> work</div>
            </div>
            """;

        var comments = SteamWorkshopItemPageParser.ParseComments(Html);
        ChecksContext.Equal("表情别名：评论解析出一条", 1, comments.Count);
        if (comments.Count != 1)
        {
            return;
        }

        var body = HtmlDescriptionParser.Parse(comments[0].TextHtml);
        var image = AssertSingleImage(body);
        ChecksContext.Check("表情别名：AST 保留 akamai 原图", image?.Url.Contains("community.akamai.steamstatic.com", StringComparison.Ordinal) == true);
        ChecksContext.Check(
            "表情别名：akamai 原图不在下载白名单（必须规范化后才能下载）",
            image is not null && !OnlineHosts.IsAllowed(new Uri(image.Url)));
        var normalized = string.Empty;
        var isEmoticon = image is not null
            && SteamEmoticon.TryCreate(image.Url, image.Alt, out normalized, out var shortCode)
            && normalized == EmoticonUrl
            && shortCode == ":highrise:";
        ChecksContext.Check("表情别名：评论表情规范化回 fastly", isEmoticon, normalized);
    }

    /// <summary>② 纯文本评论保持旧行为：标签剥掉、可见文字还在。</summary>
    private static void CheckPlainCommentUnchanged()
    {
        const string Html = """
            <div class="commentthread_comment " id="comment_2">
              <a class="hoverunderline commentthread_author_link" href="https://steamcommunity.com/id/bob"><bdi>Bob</bdi></a>
              <div class="commentthread_comment_text">第一行<br>第二行</div>
            </div>
            """;

        var comments = SteamWorkshopItemPageParser.ParseComments(Html);
        ChecksContext.Equal("纯文本评论：解析出一条", 1, comments.Count);
        if (comments.Count != 1)
        {
            return;
        }

        var text = comments[0].TextPlain;
        ChecksContext.Check("纯文本评论：可见文字保留", text.Contains("第一行", StringComparison.Ordinal) && text.Contains("第二行", StringComparison.Ordinal), text);
        ChecksContext.Check("纯文本评论：标签不泄漏", !text.Contains('<', StringComparison.Ordinal), text);

        var body = HtmlDescriptionParser.Parse(comments[0].TextHtml);
        ChecksContext.Check(
            "纯文本评论：HTML 解析路径与旧正文一致",
            body.ToPlainText().Contains("第一行", StringComparison.Ordinal)
            && body.ToPlainText().Contains("第二行", StringComparison.Ordinal),
            body.ToPlainText());
    }

    /// <summary>③ http / 非白名单主机 / 畸形 src：不能成为表情，只保留文本短码。</summary>
    private static void CheckInvalidSourcesFallBackToShortCode()
    {
        var invalid = new[]
        {
            "http://community.fastly.steamstatic.com/economy/emoticon/highrise",
            "https://evil.example/economy/emoticon/highrise",
            "https://community.fastly.steamstatic.com/avatars/highrise",
            "https://community.fastly.steamstatic.com/economy/emoticon/",
            "https://community.fastly.steamstatic.com/economy/emoticon/../avatars/x",
            "/economy/emoticon/highrise",
            "javascript:alert(1)",
        };

        foreach (var url in invalid)
        {
            ChecksContext.Check("表情：无效地址不成为表情 " + url, !SteamEmoticon.IsEmoticonImage(url, ":highrise:"));
        }

        ChecksContext.Check("表情：无效地址仍能取出短码", SteamEmoticon.TryGetShortCode(":highrise:", out var code) && code == ":highrise:");

        var body = HtmlDescriptionParser.Parse(
            "<p>看 <img src=\"https://evil.example/economy/emoticon/highrise\" alt=\":highrise:\"> 这里</p>");
        var image = AssertSingleImage(body);
        ChecksContext.Check(
            "表情：非白名单图在 AST 里不是表情",
            image is not null && !SteamEmoticon.IsEmoticonImage(image.Url, image.Alt));
        ChecksContext.Equal("表情：非白名单图降级为 alt 短码", ":highrise:", image?.Alt);

        // 每一种失效地址都要走同一条降级路：AST 仍是普通图片，短码来自 alt。
        foreach (var url in invalid)
        {
            var fallback = HtmlDescriptionParser.Parse(
                "<p>看 <img src=\"" + url + "\" alt=\":highrise:\"> 这里</p>");
            var fallbackImage = AssertSingleImage(fallback);
            ChecksContext.Check(
                "表情：失效地址在 AST 里仍保留图片节点 " + url,
                fallbackImage is not null);
            ChecksContext.Check(
                "表情：失效地址在 AST 里不是表情 " + url,
                fallbackImage is not null
                && !SteamEmoticon.IsEmoticonImage(fallbackImage.Url, fallbackImage.Alt));
            ChecksContext.Equal(
                "表情：失效地址在 AST 里仍可读短码 " + url,
                ":highrise:",
                fallbackImage?.Alt);
        }
    }

    /// <summary>④ &lt;br&gt; 仍然产出换行节点。</summary>
    private static void CheckLineBreak()
    {
        var body = HtmlDescriptionParser.Parse("<p>第一行<br>第二行</p>");
        ChecksContext.Check(
            "表情：评论里的 <br> 是换行节点",
            EnumerateInlines(body).Any(static inline => inline is MarkdownBreakInline));
        ChecksContext.Equal("表情：<br> 转成换行文本", "第一行\n第二行", body.ToPlainText());
    }

    /// <summary>⑤ 白名单：精确主机放行，子域/其它主机/非 https 拒绝。</summary>
    private static void CheckWhitelist()
    {
        var emoticon = new Uri(EmoticonUrl);
        ChecksContext.Check("表情白名单：精确主机可下载", OnlineHosts.IsAllowed(emoticon));
        ChecksContext.Check("表情白名单：精确主机 + 表情路径是表情", OnlineHosts.IsEmoticonUri(emoticon));
        ChecksContext.Check(
            "表情白名单：同主机非表情路径不算表情",
            !OnlineHosts.IsEmoticonUri(new Uri("https://community.fastly.steamstatic.com/avatars/x.jpg")));
        ChecksContext.Check(
            "表情白名单：子域不算表情",
            !OnlineHosts.IsEmoticonUri(new Uri("https://evil.community.fastly.steamstatic.com/economy/emoticon/highrise")));
        ChecksContext.Check(
            "表情白名单：http 不算表情",
            !OnlineHosts.IsEmoticonUri(new Uri("http://community.fastly.steamstatic.com/economy/emoticon/highrise")));
        ChecksContext.Check(
            "表情白名单：同主机任意路径仍受通用白名单约束（这里只验证表情判定收紧）",
            OnlineHosts.IsAllowed(new Uri("https://community.fastly.steamstatic.com/avatars/x.jpg"))
            && !OnlineHosts.IsEmoticonUri(new Uri("https://community.fastly.steamstatic.com/avatars/x.jpg")));

        // Steam 线上还会把同一份表情轮换到 akamai；它不进下载白名单，只作为解析别名
        // 规范化回 fastly，保证真实页面能显示且网络放行面不扩大。
        var akamai = new Uri("https://community.akamai.steamstatic.com/economy/emoticon/highrise?v=2");
        ChecksContext.Check("表情白名单：akamai 别名不在通用白名单", !OnlineHosts.IsAllowed(akamai));
        ChecksContext.Check("表情白名单：akamai 别名不通过严格表情判定", !OnlineHosts.IsEmoticonUri(akamai));
        ChecksContext.Check(
            "表情白名单：akamai 别名规范化为 fastly 且去掉 query",
            SteamEmoticon.TryCreate(akamai.AbsoluteUri, ":highrise:", out var normalizedAlias, out var aliasCode)
            && normalizedAlias == EmoticonUrl
            && aliasCode == ":highrise:",
            normalizedAlias + " / " + aliasCode);
    }

    private static MarkdownImageInline? AssertSingleImage(MarkdownDocument body)
    {
        var images = EnumerateInlines(body).OfType<MarkdownImageInline>().ToList();
        ChecksContext.Equal("表情：AST 里只有一张图片", 1, images.Count);
        return images.Count == 1 ? images[0] : null;
    }

    private static IEnumerable<MarkdownInline> EnumerateInlines(MarkdownDocument document)
    {
        foreach (var block in document.Blocks)
        {
            foreach (var inline in EnumerateInlines(block))
            {
                yield return inline;
            }
        }
    }

    private static IEnumerable<MarkdownInline> EnumerateInlines(MarkdownBlock block) => block switch
    {
        MarkdownHeading heading => Flatten(heading.Inlines),
        MarkdownParagraph paragraph => Flatten(paragraph.Inlines),
        MarkdownListItem item => Flatten(item.Inlines),
        MarkdownQuote quote => quote.Blocks.SelectMany(EnumerateInlines),
        _ => [],
    };

    private static IEnumerable<MarkdownInline> Flatten(IReadOnlyList<MarkdownInline> inlines)
    {
        foreach (var inline in inlines)
        {
            yield return inline;
            switch (inline)
            {
                case MarkdownLinkInline link:
                    foreach (var child in Flatten(link.Inlines))
                    {
                        yield return child;
                    }

                    break;
                case MarkdownStyledInline styled:
                    foreach (var child in Flatten(styled.Inlines))
                    {
                        yield return child;
                    }

                    break;
            }
        }
    }
}
