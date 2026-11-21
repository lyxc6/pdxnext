namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 钉住已清理的历史设置入口不得回潮；这些入口仍能在旧版本里看到，但当前版本已经删除。
/// 检查只看源码文本与资源键，不触碰真实用户数据。
/// </summary>
public static class ObsoleteSettingsCleanupChecks
{
    private static readonly string[] RemovedResourceKeys =
    [
        "SettingsAboutReleaseNotes",
        "SettingsDangerAllowOff",
        "SettingsDataDlcOnlineOff",
        "SettingsDataNewsOnlineOff",
        "SettingsInterfaceAlwaysOnTopDesc",
        "SettingsInterfaceAlwaysOnTopOn",
        "SettingsInterfaceAlwaysOnTopOff",
        "SettingsModThumbnailsOnlineOff",
        "SettingsSectionOnline",
        "SettingsThemeBusy",
        "SettingsThemeOnlineOff",
        "SettingsTranslateGlossaryName",
        "SettingsTranslateLastFailure",
        "SettingsTranslateLocalModelStatusRemoveFailed",
        "SettingsTranslateLocalModelsName",
        "SettingsTranslateStatusEndpoint",
        "SettingsAppDataLegacy",
        "SettingsAppDataLegacyName",
        "SettingsAppDataClearLegacy",
        "SettingsAppDataClearLegacyName",
        "SettingsAppDataStatusCleared",
        "SettingsAppDataStatusMissing",
        "SettingsAppDataDialogClearTitle",
        "SettingsAppDataDialogClearContent",
        "SettingsAppDataDialogClearFailedTitle",
        "SettingsInterfacePlaysetPlaceholderTitle",
        "SettingsInterfacePlaysetPlaceholderDesc",
        "ModsPlaceholderTitle",
        "ModsPlaceholderName",
        "ModsPlaceholderDescription",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();

        foreach (var relativePath in SourceFiles)
        {
            var path = Path.Combine(root, Path.Combine(relativePath.Split('/')));
            ChecksContext.Check("清理: 检查源文件存在 " + relativePath, File.Exists(path), path);
        }

        var coreModes = Read(root, "src", "PdxLauncher.Core", "Configuration", "PlaySetPageDisplayModes.cs");
        var catalog = Read(root, "src", "PdxLauncher.App", "Services", "PlaySetPageDisplayModeCatalog.cs");
        var settingsView = Read(root, "src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs");
        var modsView = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModsViewModel.cs");
        var modsPage = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml");
        var modsCode = Read(root, "src", "PdxLauncher.App", "Views", "ModsPage.xaml.cs");
        var settingsPage = Read(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml");
        var settingsCode = Read(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml.cs");
        var uiaSections = Read(root, "scripts", "verify-settings-sections.ps1");

        ChecksContext.Check("清理: 播放集显示方式不再定义留空常量", !coreModes.Contains("Placeholder", StringComparison.Ordinal));
        ChecksContext.Check("清理: 播放集显示方式不再有 IsPlaceholder", !coreModes.Contains("IsPlaceholder", StringComparison.Ordinal));
        ChecksContext.Check(
            "清理: 未知 / 历史 placeholder 值回退原版启动器",
            PlaySetPageDisplayModes.Normalize(" PLACEHOLDER ") == PlaySetPageDisplayModes.OfficialLauncher);
        ChecksContext.Check("清理: 设置页下拉不再提供留空项", !catalog.Contains("Placeholder", StringComparison.Ordinal));
        ChecksContext.Check("清理: 播放集页不再有留空布局", !modsPage.Contains("IsPlaceholderLayout", StringComparison.Ordinal));
        ChecksContext.Check("清理: 播放集页不再有留空占位控件", !modsPage.Contains("txtPlaySetPlaceholder", StringComparison.Ordinal));
        ChecksContext.Check("清理: 播放集 VM 不再有留空布局", !modsView.Contains("IsPlaceholderLayout", StringComparison.Ordinal));
        ChecksContext.Check("清理: 播放集页代码不再判断留空布局", !modsCode.Contains("IsPlaceholderLayout", StringComparison.Ordinal));
        ChecksContext.Check("清理: 设置页不再绑定留空显示方式", !settingsView.Contains("SettingsInterfacePlaysetPlaceholder", StringComparison.Ordinal));
        ChecksContext.Check("清理: 设置页不再展示旧 C 盘数据卡片", !settingsPage.Contains("SettingsAppDataLegacy", StringComparison.Ordinal));
        ChecksContext.Check("清理: 设置页不再绑定清理旧数据按钮", !settingsPage.Contains("btnClearLegacyData", StringComparison.Ordinal));
        ChecksContext.Check("清理: 旧 C 盘数据清理处理器已删除", !settingsCode.Contains("OnClearLegacyDataClick", StringComparison.Ordinal));
        ChecksContext.Check("清理: 设置页 VM 不再暴露旧 C 盘目录", !settingsView.Contains("LegacyDataDirectoryPath", StringComparison.Ordinal));
        ChecksContext.Check("清理: UIA 分节脚本不再要求旧 C 盘按钮", !uiaSections.Contains("btnClearLegacyData", StringComparison.Ordinal));

        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var resources = Read(root, "src", "PdxLauncher.App", "Strings", language, "Resources.resw");
            foreach (var key in RemovedResourceKeys)
            {
                ChecksContext.Check(
                    "清理: " + language + " 资源键已删除 " + key,
                    !resources.Contains("<data name=\"" + key + "\"", StringComparison.Ordinal));
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static readonly string[] SourceFiles =
    [
        "src/PdxLauncher.Core/Configuration/PlaySetPageDisplayModes.cs",
        "src/PdxLauncher.App/Services/PlaySetPageDisplayModeCatalog.cs",
        "src/PdxLauncher.App/ViewModels/SettingsViewModel.cs",
        "src/PdxLauncher.App/ViewModels/ModsViewModel.cs",
        "src/PdxLauncher.App/Views/ModsPage.xaml",
        "src/PdxLauncher.App/Views/ModsPage.xaml.cs",
        "src/PdxLauncher.App/Views/SettingsPage.xaml",
        "src/PdxLauncher.App/Views/SettingsPage.xaml.cs",
        "src/PdxLauncher.App/Strings/zh-CN/Resources.resw",
        "src/PdxLauncher.App/Strings/en-US/Resources.resw",
        "scripts/verify-settings-sections.ps1",
    ];

    private static string Read(params string[] parts)
    {
        var path = Path.Combine(parts);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
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
