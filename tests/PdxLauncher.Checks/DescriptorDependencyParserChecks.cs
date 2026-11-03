namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// descriptor 依赖解析自检：<see cref="DescriptorDependencyParser.ParseSteamIds"/> 对
/// dependencies = { "workshop_item_id = 123" } 各种写法的提取、去重与噪声过滤。
/// <para>纯内存用例，不读写磁盘。</para>
/// </summary>
public static class DescriptorDependencyParserChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckQuotedBlock();
        CheckUnquotedAndMultiline();
        CheckMissingAndEmpty();
        CheckDedupeAndNoise();
        return ChecksContext.Failures - before;
    }

    private static void CheckQuotedBlock()
    {
        var text = "name=\"X\"\ndependencies={ \"workshop_item_id = 123\" \"workshop_item_id = 456\" }\n";
        ChecksContext.Equal("引号写法逐条提取", "123,456", Join(DescriptorDependencyParser.ParseSteamIds(text)));
    }

    private static void CheckUnquotedAndMultiline()
    {
        var text = "dependencies={\n  workshop_item_id = 789\n  workshop_item_id = 1011\n}\n";
        ChecksContext.Equal("无引号多行提取", "789,1011", Join(DescriptorDependencyParser.ParseSteamIds(text)));
    }

    private static void CheckMissingAndEmpty()
    {
        ChecksContext.Equal("无 dependencies 块 → 空", string.Empty, Join(DescriptorDependencyParser.ParseSteamIds("name=\"X\"\ntags={ \"a\" }\n")));
        ChecksContext.Equal("空 dependencies 块 → 空", string.Empty, Join(DescriptorDependencyParser.ParseSteamIds("dependencies={}")));
        ChecksContext.Equal("null 文本 → 空", string.Empty, Join(DescriptorDependencyParser.ParseSteamIds(null)));
    }

    private static void CheckDedupeAndNoise()
    {
        var text = "workshop_item_id = 999\ndependencies={ \"workshop_item_id = 123\" \"workshop_item_id = 123\" }\n";
        ChecksContext.Equal("重复 id 去重、块外 id 不取", "123", Join(DescriptorDependencyParser.ParseSteamIds(text)));
    }

    private static string Join(IReadOnlyList<string> ids) => string.Join(",", ids);
}
