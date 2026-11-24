namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 标题栏全局忙态登记器自检：页面级加载与翻译共用同一个圈时，最新操作必须优先，
/// 乱序释放必须自动回落到仍活跃的一项，否则界面会一直转或显示错误来源。
/// </summary>
public static class GlobalBusyMonitorChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckEmptyAndSingle();
        CheckNewestWinsAndFallback();
        CheckDuplicateDispose();
        CheckBlankLabelRejected();
        CheckSourceContracts();

        return ChecksContext.Failures - before;
    }

    private static void CheckEmptyAndSingle()
    {
        var monitor = new GlobalBusyMonitor();
        var changed = 0;
        monitor.Changed += (_, _) => changed++;

        ChecksContext.Check("全局忙态：初始为空", monitor.Current is null);

        using (var lease = monitor.Begin("ShellLoadingHome"))
        {
            ChecksContext.Equal("全局忙态：单条登记显示对应键", "ShellLoadingHome", monitor.Current?.LabelKey);
            ChecksContext.Equal("全局忙态：0→1 一次通知", 1, changed);
            ChecksContext.Check("全局忙态：租约非空", lease is not null);
        }

        ChecksContext.Check("全局忙态：释放后为空", monitor.Current is null);
        ChecksContext.Equal("全局忙态：1→0 一次通知", 2, changed);
    }

    private static void CheckNewestWinsAndFallback()
    {
        var monitor = new GlobalBusyMonitor();
        var changed = 0;
        monitor.Changed += (_, _) => changed++;

        var first = monitor.Begin("ShellLoadingLibrary");
        var second = monitor.Begin("ShellTranslating");

        ChecksContext.Equal("全局忙态：最新登记优先", "ShellTranslating", monitor.Current?.LabelKey);

        first.Dispose();
        ChecksContext.Equal("全局忙态：旧租约释放不改变显示", "ShellTranslating", monitor.Current?.LabelKey);
        ChecksContext.Equal("全局忙态：旧租约释放不发通知", 2, changed);

        second.Dispose();
        ChecksContext.Equal("全局忙态：全部释放后为空", 3, changed);

        var older = monitor.Begin("ShellLoadingLibrary");
        var newer = monitor.Begin("ShellConvertingToLocal");
        newer.Dispose();
        ChecksContext.Equal("全局忙态：最新释放后回落到旧项", "ShellLoadingLibrary", monitor.Current?.LabelKey);
        older.Dispose();
    }

    private static void CheckDuplicateDispose()
    {
        var monitor = new GlobalBusyMonitor();
        var first = monitor.Begin("A");
        first.Dispose();
        first.Dispose();

        ChecksContext.Check("全局忙态：重复 Dispose 后为空", monitor.Current is null);
    }

    private static void CheckBlankLabelRejected()
    {
        var monitor = new GlobalBusyMonitor();
        var threw = false;
        try
        {
            monitor.Begin(string.Empty);
        }
        catch (ArgumentException)
        {
            threw = true;
        }

        ChecksContext.Check("全局忙态：空标签被拒绝", threw);
    }

    private static void CheckSourceContracts()
    {
        var root = FindWorkspaceRoot();
        var main = File.ReadAllText(Path.Combine(root, "src", "PdxLauncher.App", "MainWindow.xaml"));
        var mainCs = File.ReadAllText(Path.Combine(root, "src", "PdxLauncher.App", "MainWindow.xaml.cs"));
        var pages = Path.Combine(root, "src", "PdxLauncher.App", "Views");

        ChecksContext.Check("全局忙态：标题栏保留 ringTranslating", main.Contains("x:Name=\"ringTranslating\""));
        ChecksContext.Check("全局忙态：标题栏保留 txtTranslating", main.Contains("x:Name=\"txtTranslating\""));
        ChecksContext.Check("全局忙态：主窗口订阅全局登记器", mainCs.Contains("AppServices.GlobalBusy.Changed"));

        string[] removedRings =
        [
            "ringHomeBusy", "ringLibraryBusy", "ringModLibraryBusy", "ringModsBusy",
            "ringWorkshopBusy", "ringDlcBusy", "ringSavesBusy", "ringAchievementsBusy",
            "ringSettingsBusy", "ringAddModsBusy", "modStudioBusy", "ringConvertToLocalBusy",
        ];
        foreach (var ring in removedRings)
        {
            var found = Directory.EnumerateFiles(pages, "*.xaml", SearchOption.TopDirectoryOnly)
                .Any(path => File.ReadAllText(path).Contains("AutomationId=\"" + ring + "\"", StringComparison.Ordinal));
            ChecksContext.Check("全局忙态：页面内不再有 " + ring, !found);
        }

        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var resw = File.ReadAllText(Path.Combine(
                root, "src", "PdxLauncher.App", "Strings", language, "Resources.resw"));
            ChecksContext.Check(
                "全局忙态：" + language + " 有所有忙态文案",
                ShellKeys.All(key => resw.Contains("name=\"" + key + "\"", StringComparison.Ordinal)));
        }
    }

    private static readonly string[] ShellKeys =
    [
        "ShellTranslating", "ShellLoadingHome", "ShellLoadingLibrary", "ShellLoadingPlaySets",
        "ShellLoadingModLibrary", "ShellLoadingWorkshop", "ShellLoadingWorkshopDetail",
        "ShellLoadingDlc", "ShellLoadingSaves", "ShellLoadingAchievements", "ShellLoadingAddMods",
        "ShellLoadingModStudio", "ShellLoadingModEditor", "ShellLoadingAccount", "ShellLoadingSettings",
        "ShellLoadingCrashAnalysis", "ShellLoadingDiagnostics", "ShellConvertingToLocal",
    ];

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
