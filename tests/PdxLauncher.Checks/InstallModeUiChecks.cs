namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 设置页「安装模式」自检：把本次修复的关键结论钉成源码断言。
/// <para>
/// 这条需求来自用户故障：官方安装包装在 <c>D:\app\PdxNext</c>，设置页却显示「便携 / 未安装」。
/// 根因是设置页自己比较 <c>%LOCALAPPDATA%\Programs</c> 前缀 —— 装在非 C 盘必然判成便携。
/// 判定已收口到 <c>InstallModeResolver</c>，这里防止两件事回退：
/// ① 前缀判定被重新写回设置页；② 有人把「便携 / 已安装」的结论重新接回
/// <c>PdxDataRoot.IsPerMachineInstall</c>（那个 API 只允许用于数据目录选址）。
/// </para>
/// 同类的先例见 <see cref="AccountPageLayoutChecks"/>：读源文本断言，而不是运行时截图。
/// </summary>
public static class InstallModeUiChecks
{
    /// <summary>五类判定原因各自必须被引用的文案键（MatchedInstallDir 由两条「已安装」覆盖）。</summary>
    private static readonly string[] ReasonTextKeys =
    [
        "SettingsAboutInstallModeInstalledPerUser",
        "SettingsAboutInstallModeInstalledPerMachine",
        "SettingsAboutInstallModePathMismatch",
        "SettingsAboutInstallModeExeMissing",
        "SettingsAboutInstallModeMarkerOnly",
        "SettingsAboutInstallModeNoEntry",
    ];

    /// <summary>两种语言都必须存在的文案键（点分写法，经 I18nResw 转成资源名查找）。</summary>
    private static readonly string[] I18nKeys =
    [
        "settings.about.installMode",
        "settings.about.installModeChecking",
        "settings.about.installModeDataDirectory",
        "settings.about.installModeDataNote",
        "settings.about.installModeDirectory",
        "settings.about.installModeExeMissing",
        "settings.about.installModeExeMissingHint",
        "settings.about.installModeInstalledPerMachine",
        "settings.about.installModeInstalledPerUser",
        "settings.about.installModeMarkerOnly",
        "settings.about.installModeMarkerOnlyHint",
        "settings.about.installModeName",
        "settings.about.installModeNoEntry",
        "settings.about.installModeOpenDirectory",
        "settings.about.installModePathMismatch",
        "settings.about.installModeRecordedDirectory",
        "settings.about.installModeVersion",
        "settings.about.installModeVersionLabel",
    ];

    private static readonly Regex Placeholder = new(@"\{(\d+)\}", RegexOptions.CultureInvariant);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = FindWorkspaceRoot();

        var viewModelPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs");
        var pageXamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml");
        var pageCodePath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml.cs");

        if (!ChecksContext.Check("安装模式界面: SettingsViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath) ||
            !ChecksContext.Check("安装模式界面: SettingsPage.xaml 存在", File.Exists(pageXamlPath), pageXamlPath) ||
            !ChecksContext.Check("安装模式界面: SettingsPage.xaml.cs 存在", File.Exists(pageCodePath), pageCodePath))
        {
            return ChecksContext.Failures - before;
        }

        var viewModel = File.ReadAllText(viewModelPath);
        var pageXaml = File.ReadAllText(pageXamlPath);
        var pageCode = File.ReadAllText(pageCodePath);

        CheckOldPrefixJudgementIsGone(viewModel);
        CheckNoDataRootJudgement(viewModel, pageCode);
        CheckResolverIsUsedOnWorkerThread(viewModel);
        CheckReasonTextKeysAreReferenced(viewModel);
        CheckInstallDirectoryBinding(pageXaml, pageCode);
        CheckI18nKeys(root);

        return ChecksContext.Failures - before;
    }

    /// <summary>旧判定必须彻底消失：一旦写回来，D 盘安装又会显示「便携 / 未安装」。</summary>
    private static void CheckOldPrefixJudgementIsGone(string viewModel)
    {
        ChecksContext.Check(
            "安装模式界面: 不再用 LOCALAPPDATA 前缀判断安装形态",
            !viewModel.Contains("SpecialFolder.LocalApplicationData", StringComparison.Ordinal),
            "旧实现比较 %LOCALAPPDATA%\\Programs 前缀");
        ChecksContext.Check(
            "安装模式界面: 不再出现 \"Programs\" 字面量",
            !viewModel.Contains("\"Programs\"", StringComparison.Ordinal),
            "旧实现把 Programs 拼进比较路径");
    }

    /// <summary>
    /// 安装形态的结论只能来自 InstallModeResolver。PdxDataRoot.IsPerMachineInstall 只允许
    /// 用于数据目录选址（marker / Program Files），拿它当「便携还是已安装」的结论会把
    /// Program Files 里的绿色拷贝判成已安装，也会和设置页文案打架。
    /// </summary>
    private static void CheckNoDataRootJudgement(string viewModel, string pageCode)
    {
        ChecksContext.Check(
            "安装模式界面: SettingsViewModel 不调用 IsPerMachineInstall",
            !viewModel.Contains("IsPerMachineInstall", StringComparison.Ordinal),
            "安装形态只能由 InstallModeResolver 判定");
        ChecksContext.Check(
            "安装模式界面: SettingsPage 不调用 IsPerMachineInstall",
            !pageCode.Contains("IsPerMachineInstall", StringComparison.Ordinal),
            "页面不应参与安装形态判定");
    }

    /// <summary>注册表读取必须在后台线程上，且判定走 Core 的纯函数。</summary>
    private static void CheckResolverIsUsedOnWorkerThread(string viewModel)
    {
        var taskRun = viewModel.IndexOf("Task.Run", StringComparison.Ordinal);
        var resolve = viewModel.IndexOf("InstallModeResolver.Resolve(", StringComparison.Ordinal);

        ChecksContext.Check("安装模式界面: 调用 InstallModeResolver.Resolve", resolve >= 0);
        ChecksContext.Check(
            "安装模式界面: 调用 RegistryUninstallEntrySource.ReadEntries",
            viewModel.Contains("RegistryUninstallEntrySource", StringComparison.Ordinal));
        ChecksContext.Check("安装模式界面: 注册表读取放在 Task.Run 里", taskRun >= 0 && resolve > taskRun,
            "Task.Run 下标 " + taskRun + "，Resolve 下标 " + resolve);
        ChecksContext.Check(
            "安装模式界面: marker 由 PdxDataRoot.ReadInstallMode 读入",
            viewModel.Contains("PdxDataRoot.ReadInstallMode", StringComparison.Ordinal));
    }

    /// <summary>五类 Reason 的文案键一个都不能丢（否则某个原因会显示成空白）。</summary>
    private static void CheckReasonTextKeysAreReferenced(string viewModel)
    {
        foreach (var key in ReasonTextKeys)
        {
            ChecksContext.Check(
                "安装模式界面: 引用文案键 " + key,
                viewModel.Contains("Keys." + key, StringComparison.Ordinal));
        }
    }

    /// <summary>安装目录要真的显示出来，并且「打开安装目录」复用页面的 OpenPath。</summary>
    private static void CheckInstallDirectoryBinding(string pageXaml, string pageCode)
    {
        ChecksContext.Check(
            "安装模式界面: 安装模式文案绑定在 ViewModel.InstallModeText",
            pageXaml.Contains("Text=\"{x:Bind ViewModel.InstallModeText, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 安装目录文本框存在",
            pageXaml.Contains("AutomationId=\"txtInstallModeDirectory\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 安装目录绑定 ViewModel.InstallModeDirectory",
            pageXaml.Contains("Text=\"{x:Bind ViewModel.InstallModeDirectory, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 打开按钮按目录可用性禁用",
            pageXaml.Contains("IsEnabled=\"{x:Bind ViewModel.CanOpenInstallModeDirectory, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 页面有 OnOpenInstallModeDirectoryClick 且复用 OpenPath",
            pageCode.Contains("OnOpenInstallModeDirectoryClick", StringComparison.Ordinal) &&
            pageCode.Contains("_viewModel.InstallModeDirectory", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 当前数据目录一并显示",
            pageXaml.Contains("Text=\"{x:Bind ViewModel.InstallModeDataDirectory, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 数据目录解释句在页面上",
            pageXaml.Contains("AutomationId=\"txtInstallModeDataNote\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "安装模式界面: 主程序缺失 / 仅有 marker 的解释句在页面上",
            pageXaml.Contains("AutomationId=\"txtInstallModeHint\"", StringComparison.Ordinal));
    }

    /// <summary>中英两套文案键必须齐全；带占位符的键在两种语言里占位符数量一致。</summary>
    private static void CheckI18nKeys(string root)
    {
        var zh = LoadTemplates(root, "zh-CN");
        var en = LoadTemplates(root, "en-US");

        if (!ChecksContext.Check("安装模式界面: 能读到中英文案源", zh.Count > 500 && en.Count > 500,
                "zh " + zh.Count + " / en " + en.Count))
        {
            return;
        }

        foreach (var key in I18nKeys)
        {
            ChecksContext.Check("安装模式界面: zh-CN 有 " + key, I18nResw.Get(zh, key) is not null);
            ChecksContext.Check("安装模式界面: en-US 有 " + key, I18nResw.Get(en, key) is not null);
        }

        foreach (var key in I18nKeys)
        {
            var zhText = I18nResw.Get(zh, key);
            var enText = I18nResw.Get(en, key);
            if (zhText is null || enText is null)
            {
                continue;
            }

            var zhPlaceholders = Placeholder.Matches(zhText).Count;
            var enPlaceholders = Placeholder.Matches(enText).Count;
            ChecksContext.Equal("安装模式界面: " + key + " 占位符数量一致", zhPlaceholders, enPlaceholders);
        }
    }

    private static Dictionary<string, string> LoadTemplates(string root, string language) =>
        I18nResw.Read(root, language);

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
