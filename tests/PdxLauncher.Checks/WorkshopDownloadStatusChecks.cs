namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「Steam 下载状态」呈现的自检：字节数文案、创意工坊列表页的徽标、以及整库状态的接线。
/// <para>
/// 这些东西编译期都不会报错——把徽标从行模板里删掉、把「已下载 x / y」退回纯百分比、
/// 或者让列表页忘记填状态，界面只会安静地少一段信息。所以这里读源文本断言，
/// 先例见 <see cref="AccountPageLayoutChecks"/> 与 <see cref="WorkshopDetailSubscribeCardChecks"/>。
/// </para>
/// </summary>
public static class WorkshopDownloadStatusChecks
{
    /// <summary>带三个占位符的下载进度文案键（两语言都必须在）。</summary>
    private static readonly string[] ProgressKeys =
    [
        "WorkshopDetailSteamDownloadProgressBytes",
        "ModlibSteamStateDownloadingWithBytes",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();

        CheckProgressKeys(root);
        CheckWorkshopListBadge(root);
        CheckStateWiring(root);
        CheckCacheInvalidated(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>两语言都要有三个占位符的键，缺一个就会在界面上打出字面的 {2}。</summary>
    private static void CheckProgressKeys(string root)
    {
        foreach (var lang in new[] { "zh-CN", "en-US" })
        {
            var path = Path.Combine(root, "src", "PdxLauncher.App", "Strings", lang, "Resources.resw");
            if (!ChecksContext.Check("下载状态: " + lang + " Resources.resw 存在", File.Exists(path), path))
            {
                continue;
            }

            var resw = File.ReadAllText(path);
            foreach (var key in ProgressKeys)
            {
                var match = Regex.Match(
                    resw,
                    "<data name=\"" + key + "\"[^>]*>\\s*<value>(.*?)</value>",
                    RegexOptions.Singleline);
                if (!ChecksContext.Check("下载状态: " + lang + " 有 " + key, match.Success, key))
                {
                    continue;
                }

                var placeholders = Regex.Matches(match.Groups[1].Value, @"\{\d+\}").Count;
                ChecksContext.Equal("下载状态: " + lang + " 的 " + key + " 有三个占位符", 3, placeholders);
            }
        }
    }

    /// <summary>创意工坊列表行必须挂上徽标（与 Mod 库同一套画刷与绑定）。</summary>
    private static void CheckWorkshopListBadge(string root)
    {
        var path = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml");
        if (!ChecksContext.Check("下载状态: WorkshopPage.xaml 存在", File.Exists(path), path))
        {
            return;
        }

        var xaml = File.ReadAllText(path);
        foreach (var token in new[]
                 {
                     "{x:Bind SteamStateBadgeText",
                     "{x:Bind SteamStateAutomationId",
                     "SystemFillColorSuccessBackgroundBrush",
                 })
        {
            ChecksContext.Check("下载状态: 创意工坊行模板包含 " + token, xaml.Contains(token, StringComparison.Ordinal), token);
        }
    }

    /// <summary>列表页要真的去读整库状态，而不是只声明一个没人填的属性。</summary>
    private static void CheckStateWiring(string root)
    {
        var pageVm = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopViewModel.cs");
        var rowVm = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopRowViewModel.cs");
        var reader = Path.Combine(root, "src", "PdxLauncher.App", "Services", "WorkshopStateReader.cs");

        foreach (var path in new[] { pageVm, rowVm, reader })
        {
            ChecksContext.Check("下载状态: " + Path.GetFileName(path) + " 存在", File.Exists(path), path);
        }

        if (!File.Exists(pageVm) || !File.Exists(rowVm) || !File.Exists(reader))
        {
            return;
        }

        var page = File.ReadAllText(pageVm);
        ChecksContext.Check("下载状态: 列表视图模型用 WorkshopStateReader", page.Contains("WorkshopStateReader", StringComparison.Ordinal));
        ChecksContext.Check("下载状态: 列表视图模型有 FillSteamStatesAsync", page.Contains("FillSteamStatesAsync", StringComparison.Ordinal));
        ChecksContext.Check("下载状态: 列表视图模型提供 RefreshSteamStates", page.Contains("RefreshSteamStates", StringComparison.Ordinal));

        var row = File.ReadAllText(rowVm);
        ChecksContext.Check("下载状态: 行模型有 SteamState 字段", row.Contains("WorkshopInstallState? _steamState", StringComparison.Ordinal));
        ChecksContext.Check("下载状态: 行模型徽标走共用格式化", row.Contains("SteamStateBadgeFormatter", StringComparison.Ordinal));
    }

    /// <summary>订阅改动后必须失效整库缓存，否则列表徽标最长 60 秒不更新。</summary>
    private static void CheckCacheInvalidated(string root)
    {
        var path = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");
        if (!ChecksContext.Check("下载状态: WorkshopDetailViewModel.cs 存在", File.Exists(path), path))
        {
            return;
        }

        var vm = File.ReadAllText(path);
        ChecksContext.Check(
            "下载状态: 详情页改订阅后失效整库缓存",
            vm.Contains("WorkshopStates.Invalidate", StringComparison.Ordinal));
        ChecksContext.Check(
            "下载状态: 详情页用带字节的进度文案",
            vm.Contains("WorkshopDetailSteamDownloadProgressBytes", StringComparison.Ordinal));
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
