namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// DLC 页保存守卫：列表未加载完时不能保存，否则空 Dlcs 快照会把激活播放集的 DLC 状态清空。
/// </summary>
public static class DlcSaveGuardChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xaml = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "Views",
            "DlcPage.xaml"));
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "DlcViewModel.cs"));

        ChecksContext.Check(
            "DLC 保存: 保存/全部启停绑定 CanSave",
            xaml.Contains("AutomationProperties.AutomationId=\"btnSaveDlcs\"", StringComparison.Ordinal)
                && xaml.Contains("IsEnabled=\"{x:Bind ViewModel.CanSave, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "DLC 保存: CanSave 要求已加载且不忙",
            viewModel.Contains("public bool CanSave => IsLoaded && !IsBusy && Game is not null", StringComparison.Ordinal));
        ChecksContext.Check(
            "DLC 保存: SaveAsync 未加载时早退",
            viewModel.IndexOf("if (!IsLoaded)", StringComparison.Ordinal) >= 0
                && viewModel.Contains("Keys.DlcStatusNotLoaded", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        ChecksContext.Check("DLC 保存: 中英都有未加载提示", I18nResw.Get(zh, "dlc.status.notLoaded") is { Length: > 0 }
            && I18nResw.Get(en, "dlc.status.notLoaded") is { Length: > 0 });

        return ChecksContext.Failures == before ? 0 : 1;
    }

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
