namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 「只改数据库不写加载文件」的源码契约：主页切播放集、导入合并进激活集都必须补写。
/// </summary>
public static class ActivePlaySetLoadFileChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var writer = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "Services",
            "ActivePlaySetLoadFileWriter.cs"));
        var home = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "HomeViewModel.cs"));
        var mods = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "ModsViewModel.cs"));

        ChecksContext.Check(
            "加载文件补写: writer 读激活播放集并原子写",
            writer.Contains("GetActivePlaySetAsync", StringComparison.Ordinal)
                && writer.Contains("PlaySetEditor.BuildLoadFileContent", StringComparison.Ordinal)
                && writer.Contains("AppServices.LoadFiles", StringComparison.Ordinal)
                && writer.Contains("WriteAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "加载文件补写: 没有加载文件的格式直接跳过",
            writer.Contains("game.WritesLoadFile", StringComparison.Ordinal)
                && writer.Contains("AppResult<bool>.Ok(true)", StringComparison.Ordinal));
        ChecksContext.Check(
            "加载文件补写: 主页切换后调用 writer",
            home.Contains("ApplyPlaySetSelectionAsync", StringComparison.Ordinal)
                && home.Contains("ActivePlaySetLoadFileWriter", StringComparison.Ordinal)
                && home.Contains("RollbackActiveAsync", StringComparison.Ordinal)
                && home.Contains("RevertSelection", StringComparison.Ordinal));
        ChecksContext.Check(
            "加载文件补写: 主页检查 SetActive 结果",
            home.Contains("SetActivePlaySetAsync", StringComparison.Ordinal)
                && home.Contains("active.IsSuccess", StringComparison.Ordinal));
        ChecksContext.Check(
            "加载文件补写: 导入合并进激活集后调用 writer",
            mods.Contains("imported.Merged && target.IsActive", StringComparison.Ordinal)
                && mods.Contains("ActivePlaySetLoadFileWriter", StringComparison.Ordinal));

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
