namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 游戏库只显示一处计数。页头「共 N 个已安装游戏」是唯一计数来源；
/// 页脚状态行只承担异常提示（例如服务未就绪），正常加载后必须为空，不能又显示「已发现 N 个游戏」。
/// 空库由页面正中的大提示说明，页脚不再重复。
/// </summary>
public static class GameLibraryCountChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[GameLibraryCountChecks] 游戏库计数只显示一处");

        var root = I18nResw.RepoRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "GameLibraryPage.xaml");
        var viewModelPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "LibraryViewModel.cs");
        var codePath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "GameLibraryPage.xaml.cs");
        var zhPath = I18nResw.ResourcesPath(root, "zh-CN");
        var enPath = I18nResw.ResourcesPath(root, "en-US");

        if (!ChecksContext.Check("游戏库计数: XAML 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("游戏库计数: ViewModel 存在", File.Exists(viewModelPath), viewModelPath)
            || !ChecksContext.Check("游戏库计数: code-behind 存在", File.Exists(codePath), codePath)
            || !ChecksContext.Check("游戏库计数: 中文文案源存在", File.Exists(zhPath), zhPath)
            || !ChecksContext.Check("游戏库计数: 英文文案源存在", File.Exists(enPath), enPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var viewModel = File.ReadAllText(viewModelPath);
        var code = File.ReadAllText(codePath);
        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");

        ChecksContext.Equal("游戏库计数: 页头只绑定一次 CountText", 1, CountOccurrences(xaml, "ViewModel.CountText"));
        ChecksContext.Check(
            "游戏库计数: CountText 位于页头 txtLibraryCount",
            IsInsideElement(xaml, "AutomationProperties.AutomationId=\"txtLibraryCount\"", "ViewModel.CountText"));
        ChecksContext.Check(
            "游戏库计数: 页脚不再绑计数",
            !IsInsideElement(xaml, "AutomationProperties.AutomationId=\"txtLibraryStatus\"", "CountText"));
        ChecksContext.Check(
            "游戏库计数: 页脚不再引用 LibraryFound",
            !xaml.Contains("LibraryFound", StringComparison.Ordinal));

        ChecksContext.Check(
            "游戏库计数: ViewModel 不再写成功计数句",
            !viewModel.Contains("Keys.LibraryFound", StringComparison.Ordinal));
        ChecksContext.Check(
            "游戏库计数: ViewModel 不再写空库页脚句",
            !viewModel.Contains("Keys.LibraryNotFound", StringComparison.Ordinal));
        ChecksContext.Check(
            "游戏库计数: 正常加载先清空页脚状态",
            viewModel.Contains("_statusKey = null;", StringComparison.Ordinal)
                && viewModel.Contains("StatusMessage = string.Empty;", StringComparison.Ordinal));
        ChecksContext.Check(
            "游戏库计数: 仍保留服务未就绪提示",
            code.Contains("Keys.LibraryStatusServicesNotReady", StringComparison.Ordinal)
                && viewModel.Contains("_statusKey", StringComparison.Ordinal));

        ChecksContext.Check(
            "游戏库计数: 空库只靠页面正中提示",
            IsInsideElement(xaml, "AutomationProperties.AutomationId=\"txtLibraryEmpty\"", "L.LibraryEmptyHint")
                && IsInsideElement(xaml, "AutomationProperties.AutomationId=\"txtLibraryEmpty\"", "ViewModel.HasNoCards"));

        foreach (var (language, table) in new[] { ("zh-CN", zh), ("en-US", en) })
        {
            ChecksContext.Check(
                "游戏库计数: " + language + " 保留 LibraryCount",
                table.TryGetValue("LibraryCount", out var count) && count.Contains("{0}", StringComparison.Ordinal));
            ChecksContext.Check(
                "游戏库计数: " + language + " 已删除 LibraryFound",
                !table.ContainsKey("LibraryFound"));
            ChecksContext.Check(
                "游戏库计数: " + language + " 已删除 LibraryNotFound",
                !table.ContainsKey("LibraryNotFound"));
            ChecksContext.Check(
                "游戏库计数: " + language + " 保留 LibraryStatusServicesNotReady",
                table.ContainsKey("LibraryStatusServicesNotReady"));
            ChecksContext.Check(
                "游戏库计数: " + language + " 保留 LibraryEmptyHint",
                table.ContainsKey("LibraryEmptyHint"));
        }

        return ChecksContext.Failures - before;
    }

    /// <summary>取以给定属性开头的元素文本（到下一个 '>' 为止），再判断片段里是否包含指定文本。</summary>
    private static bool IsInsideElement(string xaml, string anchor, string contained)
    {
        var start = xaml.IndexOf(anchor, StringComparison.Ordinal);
        if (start < 0)
        {
            return false;
        }

        var end = xaml.IndexOf('>', start);
        return end > start && xaml[start..(end + 1)].Contains(contained, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
