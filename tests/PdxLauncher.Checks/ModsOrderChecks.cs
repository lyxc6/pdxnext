namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 搜索过滤下开关 Mod 不能改真实加载顺序；「全部启用/禁用」必须作用于整个播放集。
/// </summary>
public static class ModsOrderChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var source = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "ModsViewModel.cs"));

        ChecksContext.Check(
            "Mod 顺序: 有原位同步开关状态的方法",
            source.Contains("SyncVisibleEnabledStateIntoAllMods", StringComparison.Ordinal)
                && source.Contains("item with { Enabled = enabled }", StringComparison.Ordinal));

        var saveStart = source.IndexOf("private async Task<bool> SaveAsync", StringComparison.Ordinal);
        var saveEnd = source.IndexOf("private async Task<bool> ConfirmNoExternalChangeAsync", saveStart, StringComparison.Ordinal);
        var saveBody = saveStart >= 0 && saveEnd > saveStart
            ? source[saveStart..saveEnd]
            : source;
        ChecksContext.Check(
            "Mod 顺序: 保存路径不再调用 FlushVisibleRows",
            !saveBody.Contains("FlushVisibleRows()", StringComparison.Ordinal)
                && saveBody.Contains("SyncVisibleEnabledStateIntoAllMods()", StringComparison.Ordinal));

        var allStart = source.IndexOf("public void SetAllEnabled", StringComparison.Ordinal);
        var allEnd = source.IndexOf("// ------------------------------------------------------------ 自动排序", allStart, StringComparison.Ordinal);
        var allBody = allStart >= 0 && allEnd > allStart ? source[allStart..allEnd] : source;
        ChecksContext.Check(
            "Mod 顺序: 全部启停遍历 _allMods",
            allBody.Contains("_allMods = _allMods", StringComparison.Ordinal)
                && allBody.Contains("_bulkEnabledUpdate", StringComparison.Ordinal)
                && !allBody.Contains("FlushVisibleRows()", StringComparison.Ordinal));
        ChecksContext.Check(
            "Mod 顺序: 批量启停抑制逐行自动保存",
            source.Contains("if (_bulkEnabledUpdate)", StringComparison.Ordinal));

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
