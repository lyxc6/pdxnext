namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 七处搜索框必须都是 AutoSuggestBox 的源码断言。
/// <para>
/// 换成 AutoSuggestBox 的收益（放大镜 / 清除按钮 / 下拉建议）全在控件类型上：只要有人把它改回
/// <c>TextBox</c>（"只是换个外观"看起来很无害），放大镜和下拉就一起消失，而且不会有编译错误。
/// 同样容易丢的是 <c>args.Reason</c> 守卫——少了它，程序化改 Text（绑定回写、恢复上次搜索、
/// 双列与原版两个框共用同一个 SearchText）会把下拉反复弹开，和用户抢焦点。
/// 所以这里把「控件类型 + 关键属性 + 代码后置守卫」一起钉成源码断言（先例见
/// <see cref="WorkshopPageChecks"/>）。
/// </para>
/// </summary>
public static class SearchBoxChecks
{
    /// <summary>每个搜索框所在页面与必须保留的 AutomationId：UIA 脚本按 id 定位，换控件类型也不能丢。</summary>
    private static readonly (string Xaml, string[] AutomationIds)[] Boxes =
    [
        ("WorkshopPage.xaml", ["txtWorkshopSearch"]),
        ("ModLibraryPage.xaml", ["txtModLibrarySearch"]),
        ("ModsPage.xaml", ["txtModSearch", "txtCandidateSearch", "txtModSearchTwoCol"]),
        ("AddModsPage.xaml", ["txtAddModsSearch"]),
        ("ModStudioPage.xaml", ["modStudioSearch"]),
    ];

    /// <summary>带搜索框的页面及其代码后置处理器；处理器必须带 args.Reason 守卫。</summary>
    private static readonly (string CodeBehind, string[] Handlers)[] Handlers =
    [
        ("WorkshopPage.xaml.cs", ["OnSearchTextChanged", "OnSearchQuerySubmitted"]),
        ("ModLibraryPage.xaml.cs", ["OnSearchTextChanged", "OnSearchQuerySubmitted"]),
        (
            "ModsPage.xaml.cs",
            [
                "OnSearchTextChanged",
                "OnCandidateSearchTextChanged",
                "OnSearchQuerySubmitted",
                "OnCandidateSearchQuerySubmitted",
            ]),
        ("AddModsPage.xaml.cs", ["OnSearchTextChanged", "OnSearchQuerySubmitted"]),
        ("ModStudioPage.xaml.cs", ["OnSearchTextChanged", "OnSearchQuerySubmitted"]),
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();

        foreach (var (xamlFile, automationIds) in Boxes)
        {
            var path = Path.Combine(root, "src", "PdxLauncher.App", "Views", xamlFile);
            if (!ChecksContext.Check("搜索框: " + xamlFile + " 存在", File.Exists(path), path))
            {
                continue;
            }

            var xaml = File.ReadAllText(path);
            foreach (var automationId in automationIds)
            {
                CheckSearchBox(xamlFile, xaml, automationId);
            }
        }

        foreach (var (codeBehindFile, handlers) in Handlers)
        {
            var path = Path.Combine(root, "src", "PdxLauncher.App", "Views", codeBehindFile);
            if (!ChecksContext.Check("搜索框: " + codeBehindFile + " 存在", File.Exists(path), path))
            {
                continue;
            }

            var code = File.ReadAllText(path);
            foreach (var handler in handlers)
            {
                CheckHandler(codeBehindFile, code, handler);
            }
        }

        CheckWorkshopHistory(root);
        CheckSuggestionGlue(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>一个搜索框：是 AutoSuggestBox、带放大镜、Text 走 OneWay、挂了两个处理器。</summary>
    private static void CheckSearchBox(string xamlFile, string xaml, string automationId)
    {
        var label = "搜索框: " + xamlFile + " " + automationId;
        var element = FindElement(xaml, automationId);
        if (!ChecksContext.Check(label + " 存在", element is not null, automationId))
        {
            return;
        }

        ChecksContext.Check(
            label + " 是 AutoSuggestBox",
            element!.StartsWith("<AutoSuggestBox", StringComparison.Ordinal),
            element);
        ChecksContext.Check(
            label + " 带放大镜查询按钮",
            element.Contains("QueryIcon=\"Find\"", StringComparison.Ordinal),
            element);
        ChecksContext.Check(
            label + " 文本绑定是 OneWay（输入由代码后置写回）",
            element.Contains("Text=\"{x:Bind ViewModel.", StringComparison.Ordinal)
                && element.Contains("Mode=OneWay", StringComparison.Ordinal),
            element);
        ChecksContext.Check(
            label + " 挂 TextChanged",
            element.Contains("TextChanged=\"On", StringComparison.Ordinal),
            element);
        ChecksContext.Check(
            label + " 挂 QuerySubmitted",
            element.Contains("QuerySubmitted=\"On", StringComparison.Ordinal),
            element);
    }

    /// <summary>
    /// 一个代码后置处理器：必须认 <c>args.Reason</c>、跳过非 UserInput，并且不再把自己当 TextBox 用。
    /// TextChanged 处理器还要负责算建议；QuerySubmitted 处理器要负责收起下拉。
    /// </summary>
    private static void CheckHandler(string codeBehindFile, string code, string handlerName)
    {
        var label = "搜索框: " + codeBehindFile + " " + handlerName;
        var isSubmitted = handlerName.Contains("QuerySubmitted", StringComparison.Ordinal);
        // 只比到签名部分、不带 "private "：工坊页的提交处理器是 async void，带前缀会匹配不到。
        var signature = "void "
            + handlerName
            + (isSubmitted
                ? "(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)"
                : "(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)");

        var block = FindHandler(code, signature);
        if (!ChecksContext.Check(label + " 存在且是 AutoSuggestBox 处理器", block is not null, signature))
        {
            return;
        }

        ChecksContext.Check(
            label + " 不再按 TextBox 处理",
            !block!.Contains("sender is TextBox", StringComparison.Ordinal),
            block);

        if (!isSubmitted)
        {
            ChecksContext.Check(
                label + " 只对用户输入弹建议",
                block.Contains("args.Reason != AutoSuggestionBoxTextChangeReason.UserInput", StringComparison.Ordinal),
                block);
            ChecksContext.Check(
                label + " 用公共逻辑算建议",
                block.Contains("SearchSuggestBox.Update", StringComparison.Ordinal),
                block);
            return;
        }

        ChecksContext.Check(
            label + " 提交后收起下拉",
            block.Contains("IsSuggestionListOpen = false", StringComparison.Ordinal),
            block);
    }

    /// <summary>工坊搜索历史必须落在设置里，并且提交成功后写盘（延迟写，失败不阻断搜索）。</summary>
    private static void CheckWorkshopHistory(string root)
    {
        var settingsPath = Path.Combine(root, "src", "PdxLauncher.Core", "Configuration", "AppSettings.cs");
        var workshopPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml.cs");

        if (ChecksContext.Check("搜索框: AppSettings.cs 存在", File.Exists(settingsPath), settingsPath)
            && ChecksContext.Check("搜索框: WorkshopPage.xaml.cs 存在", File.Exists(workshopPath), workshopPath))
        {
            var settings = File.ReadAllText(settingsPath);
            ChecksContext.Check(
                "搜索框: 工坊最近搜索存在设置里",
                settings.Contains("WorkshopSearchHistory", StringComparison.Ordinal),
                settings);

            var workshop = File.ReadAllText(workshopPath);
            ChecksContext.Check(
                "搜索框: 提交成功才记历史",
                workshop.Contains("SearchSuggestions.Push", StringComparison.Ordinal),
                workshop);
            ChecksContext.Check(
                "搜索框: 历史用延迟写盘",
                workshop.Contains("SaveSettingsDeferred", StringComparison.Ordinal),
                workshop);
        }
    }

    /// <summary>建议的 UI 粘合层：空建议要收起下拉，否则会留下一个空白弹层。</summary>
    private static void CheckSuggestionGlue(string root)
    {
        var path = Path.Combine(root, "src", "PdxLauncher.App", "Services", "SearchSuggestBox.cs");
        if (!ChecksContext.Check("搜索框: SearchSuggestBox.cs 存在", File.Exists(path), path))
        {
            return;
        }

        var glue = File.ReadAllText(path);
        ChecksContext.Check(
            "搜索框: 粘合层复用纯逻辑",
            glue.Contains("SearchSuggestions.Build", StringComparison.Ordinal),
            glue);
        ChecksContext.Check(
            "搜索框: 没有建议时收起下拉",
            glue.Contains("IsSuggestionListOpen = false", StringComparison.Ordinal),
            glue);
    }

    /// <summary>按 AutomationId 取出所在的那个元素（从最近的 <c>&lt;</c> 到下一个 <c>/&gt;</c>）。</summary>
    private static string? FindElement(string xaml, string automationId)
    {
        var marker = "AutomationProperties.AutomationId=\"" + automationId + "\"";
        var index = xaml.IndexOf(marker, StringComparison.Ordinal);
        if (index < 0)
        {
            return null;
        }

        var start = xaml.LastIndexOf('<', index);
        var end = xaml.IndexOf("/>", index, StringComparison.Ordinal);
        return start < 0 || end < 0 ? null : xaml[start..(end + 2)];
    }

    /// <summary>取出一个处理器的方法体（从签名到下一个方法）。</summary>
    private static string? FindHandler(string code, string signature)
    {
        var start = code.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        var nextMethod = code.IndexOf("    private ", start + signature.Length, StringComparison.Ordinal);
        var end = nextMethod < 0 ? code.Length : nextMethod;
        return code[start..end];
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
