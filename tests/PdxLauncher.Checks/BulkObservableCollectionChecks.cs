namespace PdxLauncher.Checks;

using System.Collections.Specialized;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊列表的批量替换自检。
/// <para>
/// 逐条 Clear + Add 会让 ListView 对 30 行各做一次集合变更，真机上量出过几百毫秒的
/// UI 线程停顿；<see cref="BulkObservableCollection{T}"/>.ReplaceAll 必须只发一次 Reset。
/// 另外钉住 <c>WorkshopViewModel.ReplaceRows</c> 真的走了批量入口，别再退回逐条 Add。
/// </para>
/// </summary>
public static class BulkObservableCollectionChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var collection = new BulkObservableCollection<string>();
        var actions = new List<NotifyCollectionChangedAction>();
        collection.CollectionChanged += (_, args) => actions.Add(args.Action);

        collection.ReplaceAll(Enumerable.Range(0, 30).Select(static i => "row" + i));
        ChecksContext.Equal("批量替换 30 条只发 1 次通知", 1, actions.Count);
        ChecksContext.Equal("批量替换 30 条是 Reset", NotifyCollectionChangedAction.Reset, actions[0]);
        ChecksContext.Equal("批量替换后行数", 30, collection.Count);
        ChecksContext.Equal("批量替换后顺序保持", "row0", collection[0]);

        actions.Clear();
        collection.ReplaceAll(["second"]);
        ChecksContext.Equal("再次整体替换仍只发 1 次通知", 1, actions.Count);
        ChecksContext.Equal("再次整体替换后行数", 1, collection.Count);
        ChecksContext.Equal("再次整体替换后内容更新", "second", collection[0]);

        actions.Clear();
        collection.ReplaceAll([]);
        ChecksContext.Equal("替换为空只发 1 次通知", 1, actions.Count);
        ChecksContext.Equal("替换为空后行数", 0, collection.Count);

        actions.Clear();
        collection.Clear();
        ChecksContext.Equal("Clear 仍只发 1 次 Reset", 1, actions.Count);

        var root = FindWorkspaceRoot();
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "WorkshopViewModel.cs"));
        ChecksContext.Check(
            "工坊 ViewModel: ReplaceRows 走 ReplaceAll",
            viewModel.Contains("Rows.ReplaceAll(rows)", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊 ViewModel: ReplaceRows 不再逐条 Add",
            !viewModel.Contains("Rows.Add(row)", StringComparison.Ordinal));
        ChecksContext.Check(
            "工坊 ViewModel: Rows 使用批量集合",
            viewModel.Contains("BulkObservableCollection<WorkshopRowViewModel> Rows", StringComparison.Ordinal));

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
