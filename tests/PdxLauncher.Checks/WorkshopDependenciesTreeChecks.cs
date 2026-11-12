namespace PdxLauncher.Checks;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods;

/// <summary>
/// 工坊详情依赖树（一期只做分组）的源码契约。
/// <para>
/// 数据面原来是平铺 <c>ItemsControl</c>；换成 <c>TreeView</c> 后，<c>ItemTemplate</c> 根
/// 必须是 <c>TreeViewItem</c>，子项靠 <c>ItemsSource="{x:Bind Children}"</c> 展开。
/// 写错不会崩，只会静默显示空列表或没有展开箭头，所以用源码断言钉住。
/// </para>
/// </summary>
public static class WorkshopDependenciesTreeChecks
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
            "WorkshopDetailPage.xaml"));
        var node = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "WorkshopDependencyNodeViewModel.cs"));
        var viewModel = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "WorkshopDetailViewModel.cs"));
        var dependency = File.ReadAllText(Path.Combine(
            root,
            "src",
            "PdxLauncher.App",
            "ViewModels",
            "WorkshopDependencyViewModel.cs"));

        ChecksContext.Check(
            "依赖树: 详情页换成 TreeView",
            xaml.Contains("<TreeView ItemsSource=\"{x:Bind ViewModel.DependencyTree", StringComparison.Ordinal)
                && xaml.Contains("AutomationProperties.AutomationId=\"lstWorkshopDependencies\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: ItemTemplate 根是 TreeViewItem 并绑定 Children",
            xaml.Contains("<TreeViewItem ItemsSource=\"{x:Bind Children", StringComparison.Ordinal)
                && xaml.Contains("AutomationProperties.Name=\"{x:Bind Name, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 展开态用 OneTime，切语言不重置用户操作",
            xaml.Contains("IsExpanded=\"{x:Bind IsExpanded, Mode=OneTime}\"", StringComparison.Ordinal));

        foreach (var id in new[]
                 {
                     "workshopDependencyGroup_attention",
                     "workshopDependencyGroup_ready",
                     "workshopDependencyGroup_unknown",
                 })
        {
            ChecksContext.Check(
                "依赖树: 分组 id " + id,
                node.Contains(id, StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "依赖树: 叶子沿用原依赖行 id",
            node.Contains("AutomationId = leaf.AutomationId", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 需处理组默认展开",
            node.Contains("IsExpanded = group == DependencyGroup.Attention", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: ViewModel 构建三组",
            viewModel.Contains("BuildDependencyTree", StringComparison.Ordinal)
                && viewModel.Contains("DependencyGroup.Attention", StringComparison.Ordinal)
                && viewModel.Contains("DependencyGroup.Ready", StringComparison.Ordinal)
                && viewModel.Contains("DependencyGroup.Unknown", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 切页清空树",
            viewModel.Contains("DependencyTree.Clear()", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[]
                 {
                     "workshop.dependencies.groupAttention",
                     "workshop.dependencies.groupReady",
                     "workshop.dependencies.groupUnknown",
                 })
        {
            ChecksContext.Check("依赖树: zh-CN 有 " + key, I18nResw.Get(zh, key) is { Length: > 0 });
            ChecksContext.Check("依赖树: en-US 有 " + key, I18nResw.Get(en, key) is { Length: > 0 });
        }

        ChecksContext.Check(
            "依赖树: 全 Unknown 不再报全部就绪",
            viewModel.Contains("WorkshopDependencySummaryUnknown", StringComparison.Ordinal)
                && viewModel.Contains("item.State == WorkshopDependencyState.Unknown", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 摘要切语言重算",
            viewModel.Contains("_dependencyMatches", StringComparison.Ordinal)
                && viewModel.Contains("UpdateDependencySummary(_dependencyMatches)", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 叶子状态文案是计算属性",
            dependency.Contains("public string StateText =>", StringComparison.Ordinal)
                && dependency.Contains("public string DetailText =>", StringComparison.Ordinal));
        ChecksContext.Check(
            "依赖树: 中英都有 Unknown 摘要",
            I18nResw.Get(zh, "workshop.dependency.summary.unknown") is { Length: > 0 }
                && I18nResw.Get(en, "workshop.dependency.summary.unknown") is { Length: > 0 });

        CheckNodeBehavior();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>直接构造节点：分组标题、展开态、子项 id 映射都不能靠肉眼。</summary>
    private static void CheckNodeBehavior()
    {
        var missing = new WorkshopDependencyViewModel(
            0,
            new WorkshopDependency(
                new PdxModRequirement("MOD", "74417", "Unified Icon Library", 1, null),
                WorkshopDependencyState.Missing));
        var disabled = new WorkshopDependencyViewModel(
            1,
            new WorkshopDependency(
                new PdxModRequirement("MOD", "74418", "Extra Assets", 2, null),
                WorkshopDependencyState.InstalledDisabled));
        var ready = new WorkshopDependencyViewModel(
            2,
            new WorkshopDependency(
                new PdxModRequirement("MOD", "74419", "Base Library", 1, null),
                WorkshopDependencyState.Enabled));

        var attention = new WorkshopDependencyNodeViewModel(
            WorkshopDependencyNodeViewModel.DependencyGroup.Attention,
            [missing, disabled]);
        ChecksContext.Check("依赖树: 需处理组是分组节点", attention.IsGroup);
        ChecksContext.Equal("依赖树: 需处理组子项数", 2, attention.Children.Count);
        ChecksContext.Check(
            "依赖树: 需处理组标题带计数",
            attention.Name.StartsWith("workshop.dependencies.groupAttention:2", StringComparison.Ordinal),
            attention.Name);
        ChecksContext.Check("依赖树: 需处理组默认展开", attention.IsExpanded);
        ChecksContext.Equal(
            "依赖树: 叶子沿用原自动化 id",
            "workshopDependency_0",
            attention.Children[0].AutomationId);
        ChecksContext.Check("依赖树: 缺依赖叶子显示提醒", attention.Children[0].ShowAttentionIcon);
        ChecksContext.Check("依赖树: 叶子有状态徽标", attention.Children[0].HasState);

        var readyGroup = new WorkshopDependencyNodeViewModel(
            WorkshopDependencyNodeViewModel.DependencyGroup.Ready,
            [ready]);
        ChecksContext.Check("依赖树: 已就绪组默认折叠", !readyGroup.IsExpanded);
        ChecksContext.Check(
            "依赖树: 已就绪组标题带计数",
            readyGroup.Name.StartsWith("workshop.dependencies.groupReady:1", StringComparison.Ordinal),
            readyGroup.Name);
        ChecksContext.Check("依赖树: 已就绪叶子不显示提醒", !readyGroup.Children[0].ShowAttentionIcon);
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
