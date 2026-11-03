namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// Steam 发布字段长度自检：<see cref="ModPublishLimits"/> 按 UTF-8 字节卡标题 / 描述 / 更新说明。
/// <para>
/// 为什么按字节而不是字符：SDK 里是定长 <c>char[128 + 1]</c> 与 <c>char[8000]</c>，
/// 而 Steamworks 按 UTF-8 写过去，一个汉字占 3 字节。按字符数算会让中文用户
/// 填写 128 个汉字、实际 384 字节，提交上去直接被拒或者被静默截断。
/// </para>
/// <para>纯内存用例，不读写磁盘，不依赖网络。</para>
/// </summary>
public static class ModPublishLimitsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckUtf8Length();
        CheckTitle();
        CheckDescription();
        CheckChangeNote();
        return ChecksContext.Failures - before;
    }

    private static void CheckUtf8Length()
    {
        ChecksContext.Equal("null 记 0 字节", 0, ModPublishLimits.Utf8Length(null));
        ChecksContext.Equal("空串记 0 字节", 0, ModPublishLimits.Utf8Length(string.Empty));
        ChecksContext.Equal("ASCII 一字一字节", 3, ModPublishLimits.Utf8Length("abc"));
        ChecksContext.Equal("汉字三字节", 3, ModPublishLimits.Utf8Length("汉"));
        ChecksContext.Equal("混排按实际字节", 4, ModPublishLimits.Utf8Length("a汉"));
        ChecksContext.Equal("表情符号四字节", 4, ModPublishLimits.Utf8Length("😀"));
    }

    private static void CheckTitle()
    {
        var asciiLimit = new string('a', ModPublishLimits.MaxTitleBytes);
        ChecksContext.Check("标题恰好 128 个 ASCII 通过", ModPublishLimits.TryValidateTitle(asciiLimit, out _));

        var overAscii = new string('a', ModPublishLimits.MaxTitleBytes + 1);
        ChecksContext.Check("标题 129 个 ASCII 被挡", !ModPublishLimits.TryValidateTitle(overAscii, out var asciiError));
        ChecksContext.Check(
            "超限提示带上限字节数",
            asciiError?.Contains("128", StringComparison.Ordinal) == true);

        // 42 个汉字 = 126 字节（通过）；43 个 = 129 字节（被挡）。
        var chineseOk = new string('虚', 42);
        ChecksContext.Check("标题 42 个汉字通过", ModPublishLimits.TryValidateTitle(chineseOk, out _));

        var chineseOver = new string('虚', 43);
        ChecksContext.Check("标题 43 个汉字被挡", !ModPublishLimits.TryValidateTitle(chineseOver, out _));

        ChecksContext.Check("空标题不算超长", ModPublishLimits.TryValidateTitle(null, out _));
        ChecksContext.Check("通过时没有错误文案", GetTitleError() is null);
    }

    private static string? GetTitleError()
    {
        ModPublishLimits.TryValidateTitle("正常标题", out var error);
        return error;
    }

    private static void CheckDescription()
    {
        var limit = new string('a', ModPublishLimits.MaxDescriptionBytes);
        ChecksContext.Check("描述恰好 8000 字节通过", ModPublishLimits.TryValidateDescription(limit, out _));

        var over = new string('a', ModPublishLimits.MaxDescriptionBytes + 1);
        ChecksContext.Check("描述 8001 字节被挡", !ModPublishLimits.TryValidateDescription(over, out var error));
        ChecksContext.Check(
            "描述超限提示带上限字节数",
            error?.Contains("8000", StringComparison.Ordinal) == true);

        ChecksContext.Check("空描述合法", ModPublishLimits.TryValidateDescription(null, out _));

        // BBCode 标记本身也算字节，这一点必须和 Steam 一致。
        ChecksContext.Check(
            "带 BBCode 的描述按原文算字节",
            ModPublishLimits.Utf8Length("[b]甲[/b]") == 10);
    }

    private static void CheckChangeNote()
    {
        var over = new string('a', ModPublishLimits.MaxChangeNoteBytes + 1);
        ChecksContext.Check("更新说明超 8000 字节被挡", !ModPublishLimits.TryValidateChangeNote(over, out var error));
        ChecksContext.Check(
            "更新说明超限提示带标签",
            error?.Contains("更新说明", StringComparison.Ordinal) == true);
        ChecksContext.Check("空更新说明合法", ModPublishLimits.TryValidateChangeNote(null, out _));
    }
}
