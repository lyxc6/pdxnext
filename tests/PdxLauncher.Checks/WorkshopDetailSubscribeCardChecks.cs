namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊详情页订阅卡自检：把「同一句『已订阅』说三遍」的版式结论钉成源码断言。
/// <para>
/// 这条需求来自真机截图：卡片里同时有蓝色「已订阅」徽章、暗色「已订阅」状态行，
/// 再加一段「已开启直连订阅…」的长说明，而按钮自己已经写着「取消订阅」。
/// 更糟的是 <c>StatusText</c> 在切 mod 时没清空，上一轮的文案会串到下一个详情页。
/// </para>
/// 重排订阅卡很容易把它们悄悄加回来，所以这里读源文本断言，不靠运行时截图。
/// 同类先例见 <see cref="AccountPageLayoutChecks"/>。
/// </summary>
public static class WorkshopDetailSubscribeCardChecks
{
    /// <summary>已删掉的重复文案键：两语言都不能再出现（否则又会被绑回界面）。</summary>
    private static readonly string[] RemovedKeys =
    [
        "WorkshopDetailInstalled",
        "WorkshopDetailSubscribeOk",
        "WorkshopDetailUnsubscribeOk",
        "WorkshopDetailSteamSubscribeHintOn",
    ];

    /// <summary>订阅卡必须保留的 AutomationId（UI 自动化与走查靠它们）。</summary>
    private static readonly string[] RequiredAutomationIds =
    [
        "btnWorkshopDetailSubscribe",
        "btnWorkshopDetailSubscribeSteam",
        "txtWorkshopDetailStatus",
        "txtWorkshopDetailSteamSubscribeHint",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopDetailPage.xaml");
        var vmPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");

        if (!ChecksContext.Check("工坊详情订阅卡: WorkshopDetailPage.xaml 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("工坊详情订阅卡: WorkshopDetailViewModel.cs 存在", File.Exists(vmPath), vmPath))
        {
            return ChecksContext.Failures - before;
        }

        CheckNoDuplicateBadge(File.ReadAllText(xamlPath));
        CheckAutomationIds(File.ReadAllText(xamlPath));
        CheckStatusTextClearedOnReload(File.ReadAllText(vmPath));
        CheckRemovedKeys(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>徽章与支持它们的属性都不该再出现在 XAML 里。</summary>
    private static void CheckNoDuplicateBadge(string xaml)
    {
        foreach (var token in new[] { "WorkshopDetailInstalled", "ShowSteamSubscribeState", "ShowSubscriptionState", "ShowInstalled" })
        {
            ChecksContext.Check(
                "工坊详情订阅卡: XAML 不再引用 " + token,
                !xaml.Contains(token, StringComparison.Ordinal),
                token);
        }
    }

    private static void CheckAutomationIds(string xaml)
    {
        foreach (var id in RequiredAutomationIds)
        {
            ChecksContext.Check(
                "工坊详情订阅卡: 保留 AutomationId " + id,
                xaml.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal));
        }
    }

    /// <summary>切 mod 时必须清掉上一个 mod 的订阅失败提示。</summary>
    private static void CheckStatusTextClearedOnReload(string vm)
    {
        const string anchor = "private void ResetForLoad";
        var start = vm.IndexOf(anchor, StringComparison.Ordinal);
        if (!ChecksContext.Check("工坊详情订阅卡: 找到 ResetForLoad", start >= 0))
        {
            return;
        }

        var end = vm.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = end > start ? vm[start..end] : vm[start..];
        ChecksContext.Check(
            "工坊详情订阅卡: ResetForLoad 清空 StatusText",
            body.Contains("StatusText = string.Empty", StringComparison.Ordinal));
    }

    /// <summary>两种语言都不许再留下已删的键（留一半还会在编译期报键集不一致）。</summary>
    private static void CheckRemovedKeys(string root)
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
        {
            var path = Path.Combine(root, "src", "PdxLauncher.App", "Strings", lang, "Resources.resw");
            if (!ChecksContext.Check("工坊详情订阅卡: " + lang + " Resources.resw 存在", File.Exists(path), path))
            {
                continue;
            }

            var resw = File.ReadAllText(path);
            foreach (var key in RemovedKeys)
            {
                ChecksContext.Check(
                    "工坊详情订阅卡: " + lang + " 不再有 " + key,
                    !resw.Contains("\"" + key + "\"", StringComparison.Ordinal),
                    key);
            }
        }
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
