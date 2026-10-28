namespace PdxLauncher.Checks;

using System.Globalization;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情文本清洗自检：<see cref="WorkshopText"/> 的 BBCode/HTML/Markdown 剥离、
/// 片段拼接（丢空段/去重）与「未知时间显示空串」。
/// <para>
/// 起因：详情正文从弹窗搬进子页后，长描述会整段渲染在页面上，作者写的
/// <c>[b]</c>/<c>[h1]</c>/<c>&lt;br&gt;</c>/<c>[文字](地址)</c> 一旦漏剥就会直接糊在界面上。
/// 这里把口径钉死：标记去干净、行首井号只吃本行、连续空行收敛、空时间不显示 1970。
/// </para>
/// <para>纯内存用例，不读写磁盘，不依赖网络。</para>
/// </summary>
public static class WorkshopTextChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckPlainTextBasics();
        CheckHtmlAndBbCode();
        CheckMarkdown();
        CheckJoin();
        CheckFormatDate();
        return ChecksContext.Failures - before;
    }

    private static void CheckPlainTextBasics()
    {
        ChecksContext.Equal("null → 空串", string.Empty, WorkshopText.ToPlainText(null));
        ChecksContext.Equal("空白 → 空串", string.Empty, WorkshopText.ToPlainText("  \r\n\t "));
        ChecksContext.Equal("纯文本原样保留", "一段描述", WorkshopText.ToPlainText("  一段描述  "));
    }

    private static void CheckHtmlAndBbCode()
    {
        ChecksContext.Equal(
            "BBCode 粗体/标题被剥离",
            "粗体标题",
            WorkshopText.ToPlainText("[b]粗体[/b][h1]标题[/h1]"));

        ChecksContext.Equal(
            "BBCode 链接标签剥离保留文字",
            "看这里",
            WorkshopText.ToPlainText("[url=https://example.com]看这里[/url]"));

        ChecksContext.Equal(
            "BBCode 列表条目剥离",
            "第一项",
            WorkshopText.ToPlainText("[list][*]第一项[/list]"));

        ChecksContext.Equal(
            "HTML br 变真换行",
            "甲\n乙",
            WorkshopText.ToPlainText("甲<br>乙"));

        ChecksContext.Equal(
            "HTML 段落变空行",
            "甲\n\n乙",
            WorkshopText.ToPlainText("<p>甲</p><p>乙</p>"));

        ChecksContext.Equal(
            "HTML 标签剥离保留文字",
            "字",
            WorkshopText.ToPlainText("<span class=\"x\">字</span>"));

        ChecksContext.Equal(
            "HTML 实体被解码",
            "A & B",
            WorkshopText.ToPlainText("A &amp; B"));
    }

    private static void CheckMarkdown()
    {
        ChecksContext.Equal(
            "Markdown 链接变「文字（地址）」",
            "名字（https://example.com/a）",
            WorkshopText.ToPlainText("[名字](https://example.com/a)"));

        ChecksContext.Equal(
            "行首井号只吃本行",
            "一\n二",
            WorkshopText.ToPlainText("# 一\n## 二"));

        ChecksContext.Equal(
            "粗体与行内代码标记剥离",
            "粗 码",
            WorkshopText.ToPlainText("**粗** `码`"));

        ChecksContext.Equal(
            "连续空行收敛成一段",
            "甲\n\n乙",
            WorkshopText.ToPlainText("甲\n\n\n\n乙"));
    }

    private static void CheckJoin()
    {
        ChecksContext.Equal("null 片段序列 → 空串", string.Empty, WorkshopText.Join(null, " · "));
        ChecksContext.Equal(
            "空段被丢弃且不留孤立分隔符",
            "评分 4.5 · 12 订阅",
            WorkshopText.Join(["评分 4.5", null, "   ", "12 订阅"], " · "));
        ChecksContext.Equal(
            "逐段 Trim 且重复段只留第一个",
            "甲 · 乙",
            WorkshopText.Join(["甲", " 甲 ", "乙"], " · "));
        ChecksContext.Equal("全空 → 空串", string.Empty, WorkshopText.Join([null, " ", ""], " · "));
    }

    private static void CheckFormatDate()
    {
        ChecksContext.Equal("null 时间 → 空串", string.Empty, WorkshopText.FormatDate(null));
        ChecksContext.Equal("Unix 纪元 → 空串", string.Empty, WorkshopText.FormatDate(DateTimeOffset.UnixEpoch));
        ChecksContext.Equal(
            "早于纪元 → 空串（不显示 1969）",
            string.Empty,
            WorkshopText.FormatDate(DateTimeOffset.UnixEpoch.AddDays(-1)));

        var stamp = new DateTimeOffset(2024, 5, 6, 7, 8, 0, TimeSpan.FromHours(8));
        var expected = stamp.ToLocalTime().ToString("g", CultureInfo.CurrentCulture);
        ChecksContext.Equal("有效时间按当前区域格式化", expected, WorkshopText.FormatDate(stamp));
    }
}
