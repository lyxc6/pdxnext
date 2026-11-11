namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 中低危回归的源码契约：EU5 改名、依赖摘要/本地化、设置启动器/保存失败、
/// 存档未登记落点、EU5/CS2 路径、译文天数、工坊静默刷新。
/// </summary>
public static class MediumLowRegressionChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var settingsVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs");
        var appSettings = Read(root, "src", "PdxLauncher.Core", "Configuration", "AppSettings.cs");
        var appServices = Read(root, "src", "PdxLauncher.App", "Services", "AppServices.cs");
        var home = Read(root, "src", "PdxLauncher.App", "ViewModels", "HomeViewModel.cs");
        var library = Read(root, "src", "PdxLauncher.App", "ViewModels", "LibraryViewModel.cs");
        var mods = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModsViewModel.cs");
        var saves = Read(root, "src", "PdxLauncher.App", "ViewModels", "SavesViewModel.cs");
        var workshop = Read(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopViewModel.cs");
        var detail = Read(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDetailViewModel.cs");
        var dependency = Read(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopDependencyViewModel.cs");
        var detailXaml = Read(root, "src", "PdxLauncher.App", "Views", "WorkshopDetailPage.xaml");

        ChecksContext.Check(
            "中低危: EU5 改名按新名字选中",
            mods.Contains("string.Equals(p.Name, newName.Trim()", StringComparison.Ordinal));

        ChecksContext.Check(
            "中低危: 依赖摘要全 Unknown 不复用“全部就绪”",
            detail.Contains("WorkshopDependencySummaryUnknown", StringComparison.Ordinal)
                && detail.Contains("item.State == WorkshopDependencyState.Unknown", StringComparison.Ordinal));
        ChecksContext.Check(
            "中低危: 依赖摘要切语言重算",
            detail.Contains("UpdateDependencySummary(_dependencyMatches)", StringComparison.Ordinal));
        ChecksContext.Check(
            "中低危: 依赖叶子状态文案为计算属性",
            dependency.Contains("public string StateText =>", StringComparison.Ordinal)
                && dependency.Contains("public string DetailText =>", StringComparison.Ordinal));
        ChecksContext.Check(
            "中低危: 依赖展开态不被语言切换重置",
            detailXaml.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=OneTime}\"", StringComparison.Ordinal));

        ChecksContext.Check(
            "中低危: 其它可执行文件存进设置",
            appSettings.Contains("PreferredLaunchExePaths", StringComparison.Ordinal)
                && settingsVm.Contains("PreferredLaunchExePaths", StringComparison.Ordinal));
        ChecksContext.Check(
            "中低危: 启动请求读取其它可执行文件",
            appServices.Contains("CreateLaunchRequest", StringComparison.Ordinal)
                && home.Contains("AppServices.CreateLaunchRequest", StringComparison.Ordinal)
                && library.Contains("AppServices.CreateLaunchRequest", StringComparison.Ordinal));

        ChecksContext.Check(
            "中低危: 设置保存失败不再显示成功",
            settingsVm.Contains("if (!SaveSettings())", StringComparison.Ordinal)
                && settingsVm.Contains("Keys.SettingsStatusSaveFailed", StringComparison.Ordinal));

        var backupAt = saves.IndexOf("await LoadBackupsAsync(game, cancellationToken)", StringComparison.Ordinal);
        var unsupportedAt = saves.IndexOf("_state = SavesState.Unsupported", StringComparison.Ordinal);
        ChecksContext.Check(
            "中低危: 未登记落点仍先读备份用量",
            backupAt >= 0 && unsupportedAt > backupAt);

        ChecksContext.Check(
            "中低危: EU5/CS2 不再显示 content_load.json",
            settingsVm.Contains("SettingsDataFormatPlaysetsJson", StringComparison.Ordinal)
                && settingsVm.Contains("SettingsDataFormatPlaysetConfig", StringComparison.Ordinal)
                && settingsVm.Contains("Game.WritesLoadFile", StringComparison.Ordinal));

        ChecksContext.Check(
            "中低危: 译文天数钳位后回显",
            CountOccurrences(settingsVm, "OnPropertyChanged(nameof(TranslateCacheDaysText))") >= 2
                && settingsVm.Contains("SettingsTranslateStatusCacheDaysInvalid", StringComparison.Ordinal));

        ChecksContext.Check(
            "中低危: 工坊静默刷新失败保留当前页",
            workshop.Contains("silent: true", StringComparison.Ordinal)
                && CountOccurrences(workshop, "if (silent)") >= 2
                && workshop.Contains("保留当前页", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[]
                 {
                     "settings.data.format.playsetsJson",
                     "settings.data.format.playsetConfig",
                     "settings.data.noLoadFile",
                     "settings.data.noLoadFileState",
                     "settings.translate.status.cacheDaysInvalid",
                     "workshop.dependency.summary.unknown",
                 })
        {
            ChecksContext.Check("中低危: zh-CN 有 " + key, I18nResw.Get(zh, key) is { Length: > 0 });
            ChecksContext.Check("中低危: en-US 有 " + key, I18nResw.Get(en, key) is { Length: > 0 });
        }

        return ChecksContext.Failures == before ? 0 : 1;
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

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(root, Path.Combine(parts)));

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("找不到仓库根（PdxLauncher.sln）");
    }
}
