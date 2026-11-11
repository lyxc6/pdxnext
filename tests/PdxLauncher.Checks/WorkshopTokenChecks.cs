namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊页从详情返回后查询令牌必须刷新（导航缓存 Required + Unloaded 取消旧令牌）。
/// </summary>
public static class WorkshopTokenChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var page = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "Views",
            "WorkshopPage.xaml.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "WorkshopViewModel.cs"));

        ChecksContext.Check(
            "工坊令牌: ViewModel 提供 RebindPageToken",
            viewModel.Contains("public void RebindPageToken(CancellationToken cancellationToken)", StringComparison.Ordinal)
                && viewModel.Contains("_pageToken = cancellationToken;", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊令牌: 详情返回的早退分支先换令牌",
            page.Contains("_viewModel.RebindPageToken(_lifetime.Token);", StringComparison.Ordinal)
                && page.IndexOf("RebindPageToken(_lifetime.Token)", StringComparison.Ordinal)
                < page.IndexOf("RefreshCatalogIfStaleAsync(_lifetime.Token)", StringComparison.Ordinal));

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
