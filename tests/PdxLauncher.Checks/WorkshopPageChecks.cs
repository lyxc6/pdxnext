namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 创意工坊页版式与查询触发的源码断言。
/// <para>
/// 这个页面曾经是「看起来没问题但用起来很难受」的版本：页头占两行（标题 + 工具条），
/// 搜索框每敲一个字符就发一次查询，Steam 的 1 秒限流让请求排在后面，页面像是卡死。
/// 重排一次页面、或者有人顺手把 TextChanged 接回查询，这些问题会静悄悄地回来，
/// 所以这里把结论钉成源码断言（先例见 <see cref="AccountPageLayoutChecks"/>）。
/// </para>
/// </summary>
public static class WorkshopPageChecks
{
    /// <summary>页面必须保留的 AutomationId：UI 自动化脚本与 HANDOFF 的走查都靠它们。</summary>
    private static readonly string[] RequiredAutomationIds =
    [
        "txtWorkshopSearch",
        "cmbWorkshopSort",
        "cmbWorkshopTimeRange",
        "ringWorkshopBusy",
        "btnWorkshopRefresh",
        "txtWorkshopGame",
        "txtWorkshopCount",
        "txtWorkshopSource",
        "btnWorkshopOpenWeb",
        "lstWorkshop",
        "txtWorkshopStatus",
        "txtWorkshopPage",
        "btnWorkshopPrevPage",
        "btnWorkshopNextPage",
        "btnWorkshopFilter",
        "btnWorkshopFilterClear",
        "cmbWorkshopSpecialFilter",
        "cmbWorkshopPageSize",
        "txtWorkshopDlcFilter",
        "chkWorkshopShowIncompatible",
        "pnlWorkshopFilters",
        "lstWorkshopPageNumbers",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var xamlPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml");
        var codeBehindPath = Path.Combine(root, "src", "PdxLauncher.App", "Views", "WorkshopPage.xaml.cs");
        var viewModelPath = Path.Combine(root, "src", "PdxLauncher.App", "ViewModels", "WorkshopViewModel.cs");

        if (!ChecksContext.Check("创意工坊页: WorkshopPage.xaml 存在", File.Exists(xamlPath), xamlPath)
            || !ChecksContext.Check("创意工坊页: WorkshopPage.xaml.cs 存在", File.Exists(codeBehindPath), codeBehindPath)
            || !ChecksContext.Check("创意工坊页: WorkshopViewModel.cs 存在", File.Exists(viewModelPath), viewModelPath))
        {
            return ChecksContext.Failures - before;
        }

        var xaml = File.ReadAllText(xamlPath);
        var codeBehind = File.ReadAllText(codeBehindPath);
        var viewModel = File.ReadAllText(viewModelPath);

        CheckHeaderIsSingleRow(xaml);
        CheckToolbarSubmitsOnEnter(xaml);
        CheckPageGridHasFourRows(xaml);
        CheckStatusBarShowsGameAndCount(xaml);
        CheckAutomationIds(xaml);
        CheckViewModelNoQueryWhileTyping(viewModel);
        CheckViewModelGuardsStaleResults(viewModel);
        CheckCacheKeyUsesAppliedSearch(viewModel);
        CheckFlushIsOffUiThread(viewModel);
        CheckNetworkFailureIsVisible(viewModel);
        CheckSteamSortsIncludeRelevance(viewModel);
        CheckFilterPanel(xaml, viewModel);
        CheckPagerAndPageSize(xaml, viewModel);
        CheckCatalogRefresh(viewModel, codeBehind);
        CheckCodeBehindHooks(codeBehind);

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 页头只剩一行工具条：搜索、排序、时间范围、忙碌环、刷新、来源徽标、打开网页都在同一块里，
    /// 并且页面内不再有标题（标题交给左侧导航栏，只保留 Page 上的读屏名）。
    /// </summary>
    private static void CheckHeaderIsSingleRow(string xaml)
    {
        string[] inToolbar =
        [
            "x:Name=\"SearchBox\"",
            "x:Name=\"SortBox\"",
            "x:Name=\"TimeRangeBox\"",
            "ringWorkshopBusy",
            "btnWorkshopRefresh",
            "txtWorkshopSource",
            "btnWorkshopOpenWeb",
        ];

        if (!TryGetBlock(
                xaml,
                "<!-- 行0：唯一一行工具条",
                "<!-- 行1：PDX 来源未登录时的提示条",
                "创意工坊页: 找到行0 工具条",
                out var toolbar))
        {
            return;
        }

        foreach (var marker in inToolbar)
        {
            ChecksContext.Check("创意工坊页: 工具条含 " + marker, toolbar.Contains(marker, StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "创意工坊页: 页面内不再有标题样式",
            !xaml.Contains("TitleTextBlockStyle", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 标题文案仍用于 Page 的读屏名",
            xaml.Contains("AutomationProperties.Name=\"{x:Bind L.WorkshopTitle", StringComparison.Ordinal));
    }

    /// <summary>搜索框是带放大镜的 AutoSuggestBox：QuerySubmitted 才提交；TextChanged 只回写文本，不触发查询。</summary>
    private static void CheckToolbarSubmitsOnEnter(string xaml)
    {
        if (!TryGetBlock(
                xaml,
                "<!-- 行0：唯一一行工具条",
                "<!-- 行1：PDX 来源未登录时的提示条",
                "创意工坊页: 工具条可断言",
                out var toolbar))
        {
            return;
        }

        ChecksContext.Check(
            "创意工坊页: 搜索框是 AutoSuggestBox",
            toolbar.Contains("<AutoSuggestBox", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 搜索框带放大镜",
            toolbar.Contains("QueryIcon=\"Find\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 提交走 QuerySubmitted",
            toolbar.Contains("QuerySubmitted=\"OnSearchQuerySubmitted\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 搜索框仍靠 TextChanged 回写文本",
            toolbar.Contains("TextChanged=\"OnSearchTextChanged\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 不再自己接 KeyDown 判回车",
            !toolbar.Contains("KeyDown=", StringComparison.Ordinal));
    }

    /// <summary>顶层 Grid 四行：工具条 / 未登录提示 / 列表 / 分页状态栏。</summary>
    private static void CheckPageGridHasFourRows(string xaml)
    {
        if (!TryGetBlock(
                xaml,
                "<Grid Padding=\"16\"",
                "</Grid.RowDefinitions>",
                "创意工坊页: 找到页面外层 Grid",
                out var header))
        {
            return;
        }

        ChecksContext.Equal(
            "创意工坊页: 页面外层 Grid 是 4 行",
            4,
            header.Split("<RowDefinition", StringSplitOptions.None).Length - 1);
    }

    /// <summary>游戏名与结果数在状态栏里，页头不再重复。</summary>
    private static void CheckStatusBarShowsGameAndCount(string xaml)
    {
        var start = xaml.IndexOf("<!-- 行3：分页 / 状态栏 -->", StringComparison.Ordinal);
        if (!ChecksContext.Check("创意工坊页: 找到分页 / 状态栏", start >= 0))
        {
            return;
        }

        var block = xaml[start..];
        ChecksContext.Check(
            "创意工坊页: 状态栏显示游戏名",
            block.Contains("ViewModel.GameTitle", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 状态栏显示结果数",
            block.Contains("ViewModel.CountText", StringComparison.Ordinal));
    }

    /// <summary>交互控件的 AutomationId 一个都不能少（重排布局容易顺手删掉）。</summary>
    private static void CheckAutomationIds(string xaml)
    {
        foreach (var id in RequiredAutomationIds)
        {
            ChecksContext.Check(
                "创意工坊页: 保留 AutomationId " + id,
                xaml.Contains("AutomationId=\"" + id + "\"", StringComparison.Ordinal));
        }
    }

    /// <summary>打字只更新文本：OnSearchTextChanged 里不能再发起查询。</summary>
    private static void CheckViewModelNoQueryWhileTyping(string viewModel)
    {
        if (!TryGetBlock(
                viewModel,
                "partial void OnSearchTextChanged",
                "partial void OnSelectedSortModeChanged",
                "创意工坊页: 找到 OnSearchTextChanged",
                out var block))
        {
            return;
        }

        ChecksContext.Check(
            "创意工坊页: 打字不触发查询",
            !block.Contains("RunQueryAsync", StringComparison.Ordinal)
                && !block.Contains("QueryAsync", StringComparison.Ordinal));
    }

    /// <summary>筛选条件与回车提交都走同一条抢占式查询路径，旧结果被代号挡掉。</summary>
    private static void CheckViewModelGuardsStaleResults(string viewModel)
    {
        if (TryGetBlock(
                viewModel,
                "partial void OnSelectedSortModeChanged",
                "partial void OnSelectedTimeRangeChanged",
                "创意工坊页: 找到 OnSelectedSortModeChanged",
                out var sortBlock))
        {
            ChecksContext.Check(
                "创意工坊页: 换排序触发查询",
                sortBlock.Contains("RunQueryAsync", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "partial void OnSelectedTimeRangeChanged",
                "partial void OnSourceChanged",
                "创意工坊页: 找到 OnSelectedTimeRangeChanged",
                out var timeBlock))
        {
            ChecksContext.Check(
                "创意工坊页: 换时间范围触发查询",
                timeBlock.Contains("RunQueryAsync", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "public Task CommitSearchAsync()",
                "public void CancelPendingQuery",
                "创意工坊页: 找到 CommitSearchAsync",
                out var commitBlock))
        {
            ChecksContext.Check(
                "创意工坊页: 回车提交回到第一页并跳过缓存",
                commitBlock.Contains("RunQueryAsync(resetPage: true, force: true)", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "创意工坊页: 结果落地前比较查询代号",
            viewModel.Contains("version != _queryVersion", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 忙碌状态只由最新一代复位",
            viewModel.Contains("if (version == _queryVersion)", StringComparison.Ordinal));
    }

    /// <summary>页缓存键用已提交的搜索词，不跟着输入框实时变。</summary>
    private static void CheckCacheKeyUsesAppliedSearch(string viewModel)
    {
        if (!TryGetBlock(
                viewModel,
                "private string BuildPageKey()",
                "/// <summary>把缓存的一页直接铺到列表上",
                "创意工坊页: 找到 BuildPageKey",
                out var block))
        {
            return;
        }

        ChecksContext.Check(
            "创意工坊页: 页缓存键用已提交的搜索词",
            block.Contains("_appliedSearch", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 页缓存键不再直接读输入框",
            !block.Contains("SearchText.Trim()", StringComparison.Ordinal));
    }

    /// <summary>缓存落盘移出 UI 线程：慢磁盘时页面不会短暂失去响应。</summary>
    private static void CheckFlushIsOffUiThread(string viewModel)
    {
        ChecksContext.Check(
            "创意工坊页: 缓存落盘放到后台线程",
            viewModel.Contains("Task.Run(_info.Flush)", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 不再在 UI 线程同步落盘",
            !viewModel.Contains("_info.Flush();", StringComparison.Ordinal));
    }

    /// <summary>
    /// 连不上 Steam 时必须说清楚，不能把「连接超时」当成「用户取消」静默吞掉。
    /// <para>
    /// 出过的事：直连 steamcommunity.com 被 DNS 污染，.NET 抛的是
    /// <c>OperationCanceledException</c>（内含 TimeoutException）。视图模型里
    /// <c>catch (OperationCanceledException) { }</c> 把它当成「页面卸载」吞掉，
    /// 页面永远停在「正在加载第 1 页…」、列表空白，日志里只剩一条
    /// 「未观察的 Task 异常」。这里把结论钉成源码断言。
    /// </para>
    /// </summary>
    private static void CheckNetworkFailureIsVisible(string viewModel)
    {
        if (TryGetBlock(
                viewModel,
                "private async Task RunQueryAsync(bool resetPage, bool force)",
                "private async Task FlushInfoCacheAsync()",
                "创意工坊页: 找到 RunQueryAsync",
                out var runQuery))
        {
            ChecksContext.Check(
                "创意工坊页: 只有页面卸载或被新查询取代才算取消",
                runQuery.Contains("when (version != _queryVersion || _pageToken.IsCancellationRequested)", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 连接超时要写状态栏而不是静默",
                runQuery.Contains("WorkshopStatusNetworkFailed", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "private string DescribeWorkshopError(string? errorCode, ErrorKind kind)",
                "private string DescribeDetectedProxyHint()",
                "创意工坊页: 找到 DescribeWorkshopError",
                out var describeError))
        {
            ChecksContext.Check(
                "创意工坊页: 错误码映射区分页面结构变了与连不上",
                describeError.Contains("steam-workshop-parse", StringComparison.Ordinal)
                    && describeError.Contains("steam-workshop-network", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "private void ShowFailureOrCache(",
                "并发补封面",
                "创意工坊页: 找到 ShowFailureOrCache",
                out var failure))
        {
            ChecksContext.Check(
                "创意工坊页: 连不上时提示检测到的代理",
                failure.Contains("DescribeDetectedProxyHint", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 排序下拉必须和 Steam 网页一样含「搜索相关度」（browsesort=textsearch）。
    /// <para>
    /// 出过的事：<c>SteamWorkshopSort.TextSearch</c> 与 <c>BuildBrowseUri</c> 一直都支持，
    /// 但排序目录漏了这一项、<c>ResolveSteamSort</c> 也永远返回不到它，用户在界面上只看得到 5 档。
    /// 这里把「菜单里有、能映射到 TextSearch、无关键词时给提示」钉成源码断言。
    /// </para>
    /// </summary>
    private static void CheckSteamSortsIncludeRelevance(string viewModel)
    {
        if (TryGetBlock(
                viewModel,
                "private static readonly string[] SteamSortKeys",
                "private static readonly string[] PdxSortKeys",
                "创意工坊页: 找到 SteamSortKeys",
                out var sortKeys))
        {
            var updatedAt = sortKeys.IndexOf("Keys.WorkshopSortMostUpdated", StringComparison.Ordinal);
            var relevanceAt = sortKeys.IndexOf("Keys.WorkshopSortRelevance", StringComparison.Ordinal);
            var subscribedAt = sortKeys.IndexOf("Keys.WorkshopSortMostSubscribed", StringComparison.Ordinal);

            ChecksContext.Check(
                "创意工坊页: 排序含「搜索相关度」",
                relevanceAt >= 0);
            ChecksContext.Check(
                "创意工坊页: 「搜索相关度」排在网页同款位置",
                updatedAt >= 0 && relevanceAt > updatedAt && subscribedAt > relevanceAt);
        }

        if (TryGetBlock(
                viewModel,
                "private SteamWorkshopSort ResolveSteamSort()",
                "private bool IsRelevanceWithoutKeyword()",
                "创意工坊页: 找到 ResolveSteamSort",
                out var resolve))
        {
            ChecksContext.Check(
                "创意工坊页: 「搜索相关度」映射到 TextSearch",
                resolve.Contains("SteamWorkshopSort.TextSearch", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "private async Task QuerySteamAsync(",
                "private async Task QueryPdxAsync(",
                "创意工坊页: 找到 QuerySteamAsync",
                out var querySteam))
        {
            ChecksContext.Check(
                "创意工坊页: 无关键词的相关度要说明清楚",
                querySteam.Contains("WorkshopSortRelevanceNeedsKeyword", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 筛选栏：左侧独立一列、分组可折叠、每个标签是三态胶囊（包含 / 排除 / 未选）、
    /// DLC 有名称过滤，且筛选变化走 700 ms 去抖后才发请求。
    /// </summary>
    private static void CheckFilterPanel(string xaml, string viewModel)
    {
        if (TryGetBlock(
                xaml,
                "<!-- 行2：筛选栏 + 列表 / 空状态 -->",
                "<!-- 行3：分页 / 状态栏 -->",
                "创意工坊页: 找到行2 筛选栏区域",
                out var row2))
        {
            ChecksContext.Check(
                "创意工坊页: 行2 是两列（筛选栏 + 列表）",
                row2.Contains("ColumnDefinition Width=\"Auto\"", StringComparison.Ordinal)
                && row2.Contains("ColumnDefinition Width=\"*\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 筛选分组可折叠",
                row2.Contains("Click=\"OnFilterGroupHeaderClick\"", StringComparison.Ordinal)
                && row2.Contains("svc:XamlVisibility.FromBool(IsExpanded)", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 标签是三态胶囊（包含/排除由同一控件显示）",
                row2.Contains("<controls:PdxTagChip", StringComparison.Ordinal)
                && row2.Contains("IsRequired=\"{x:Bind IsRequired, Mode=OneWay}\"", StringComparison.Ordinal)
                && row2.Contains("IsExcluded=\"{x:Bind IsExcluded, Mode=OneWay}\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 胶囊单列整行排布",
                row2.Contains("<ItemsPanelTemplate>", StringComparison.Ordinal)
                && row2.Contains("<StackPanel Spacing=\"4\" />", StringComparison.Ordinal)
                && !row2.Contains("<controls:PdxWrapPanel", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: DLC 组有名称过滤框",
                row2.Contains("txtWorkshopDlcFilter", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 不兼容开关绑到 Filters",
                row2.Contains("ViewModel.Filters.ShowIncompatible, Mode=TwoWay", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 特别筛选下拉绑到 Filters",
                row2.Contains("ViewModel.Filters.SpecialFilterModes", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 标签列表不再被内层滚动条压缩",
                !row2.Contains("MaxHeight=\"240\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 标签名绑定随语言切换",
                row2.Contains("Text=\"{x:Bind DisplayName, Mode=OneWay}\"", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "创意工坊页: 筛选变化走去抖",
            viewModel.Contains("FilterDebounce", StringComparison.Ordinal)
            && viewModel.Contains("ApplyFiltersAfterDebounceAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 筛选变化回到第一页并跳过缓存",
            viewModel.Contains("RunQueryAsync(resetPage: true, force: true)", StringComparison.Ordinal));
        ChecksContext.Check(
            "创意工坊页: 页面卸载取消筛选去抖",
            viewModel.Contains("CancelPendingFilterQuery()", StringComparison.Ordinal));

        if (TryGetBlock(
                viewModel,
                "private string BuildPageKey()",
                "/// <summary>把缓存的一页直接铺到列表上",
                "创意工坊页: 找到 BuildPageKey",
                out var block))
        {
            ChecksContext.Check(
                "创意工坊页: 页缓存键含每页条数",
                block.Contains("_pageSize", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 页缓存键含标签与不兼容开关",
                block.Contains("Filters.RequiredTags", StringComparison.Ordinal)
                && block.Contains("Filters.ShowIncompatible", StringComparison.Ordinal));
        }
    }

    /// <summary>
    /// 分页器：每页下拉（10/15/30/50）+ 页码序列（1 2 3 … N）+ 前后箭头；
    /// 换每页条数要回到第 1 页并跳过缓存。
    /// </summary>
    private static void CheckPagerAndPageSize(string xaml, string viewModel)
    {
        if (TryGetBlock(
                xaml,
                "<!-- 行3：分页 / 状态栏 -->",
                "<Border Grid.Column=\"1\"",
                "创意工坊页: 找到分页工具条",
                out var pager))
        {
            ChecksContext.Check(
                "创意工坊页: 分页器有每页下拉",
                pager.Contains("cmbWorkshopPageSize", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 分页器有页码序列",
                pager.Contains("ViewModel.PageButtons", StringComparison.Ordinal)
                && pager.Contains("OnPageNumberClick", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 当前页用强调色",
                pager.Contains("AccentButtonStyle", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 省略号占位",
                pager.Contains("ShowAsEllipsis", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "public Task ChangePageSizeAsync(int pageSize)",
                "/// <summary>筛选变化：去抖后回到第 1 页重查。</summary>",
                "创意工坊页: 找到 ChangePageSizeAsync",
                out var change))
        {
            ChecksContext.Check(
                "创意工坊页: 换每页条数回到第 1 页",
                change.Contains("resetPage: true, force: true)", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 每页条数写进设置",
                change.Contains("WorkshopPageSize", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "创意工坊页: 页码从首页/末页/当前页生成",
            viewModel.Contains("BuildPageButtons", StringComparison.Ordinal)
            && viewModel.Contains("totalPages - 1", StringComparison.Ordinal));
    }

    /// <summary>
    /// 筛选目录随游戏更新：读/写磁盘快照、24h TTL + 本地版本判断、单飞 + 冷却 + 失败退避，
    /// 页缓存命中时也要检查目录是否过期。
    /// </summary>
    private static void CheckCatalogRefresh(string viewModel, string codeBehind)
    {
        ChecksContext.Check(
            "创意工坊页: ViewModel 注入目录缓存",
            viewModel.Contains("WorkshopFilterCatalogCache", StringComparison.Ordinal));

        if (TryGetBlock(
                viewModel,
                "public async Task LoadAsync(GameInstallation? game",
                "public async Task RefreshAsync()",
                "创意工坊页: 找到 LoadAsync",
                out var load))
        {
            ChecksContext.Check(
                "创意工坊页: 加载时读目录快照",
                load.Contains("_catalogCache.TryLoad", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 加载后检查目录是否过期",
                load.Contains("RefreshCatalogIfStaleAsync", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "public async Task RefreshCatalogIfStaleAsync",
                "/// <summary>取消在途的目录刷新",
                "创意工坊页: 找到 RefreshCatalogIfStaleAsync",
                out var refresh))
        {
            ChecksContext.Check(
                "创意工坊页: 目录按 TTL/版本判断",
                refresh.Contains("WorkshopFilterCatalogPolicy.NeedsRefresh", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 目录刷新遵守限流冷却",
                refresh.Contains("TryGetCooldown", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 目录刷新单飞",
                refresh.Contains("_catalogRefreshing", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 目录失败后退避",
                refresh.Contains("FailureBackoff", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "private async Task QuerySteamAsync(",
                "private async Task QueryPdxAsync(",
                "创意工坊页: 找到 QuerySteamAsync 目录钩子",
                out var querySteam))
        {
            ChecksContext.Check(
                "创意工坊页: SSR 结果套到筛选栏",
                querySteam.Contains("ApplyFilterGroups(page.Groups)", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: SSR 结果更新目录快照",
                viewModel.Contains("UpdateCatalogSnapshot", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                viewModel,
                "private async Task QueryAsync(int version",
                "/// <summary>生成页缓存的键",
                "创意工坊页: 找到 QueryAsync 缓存命中路径",
                out var query))
        {
            ChecksContext.Check(
                "创意工坊页: 页缓存命中也检查目录",
                query.Contains("RefreshCatalogIfStaleAsync", StringComparison.Ordinal));
        }

        ChecksContext.Check(
            "创意工坊页: 已选标签消失后重查",
            viewModel.Contains("RemovedActiveTags", StringComparison.Ordinal)
            && viewModel.Contains("_pendingFilterRequery", StringComparison.Ordinal));

        ChecksContext.Check(
            "创意工坊页: 页面卸载取消目录刷新",
            codeBehind.Contains("CancelCatalogRefresh", StringComparison.Ordinal));
    }

    private static void CheckCodeBehindHooks(string codeBehind)
    {
        if (TryGetBlock(
                codeBehind,
                "private async void OnSearchQuerySubmitted",
                "private void OnSortSelectionChanged",
                "创意工坊页: 找到 OnSearchQuerySubmitted",
                out var querySubmitted))
        {
            ChecksContext.Check(
                "创意工坊页: 提交才查工坊",
                querySubmitted.Contains("CommitSearchAsync", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 提交成功记进最近搜索",
                querySubmitted.Contains("SearchSuggestions.Push", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 最近搜索只延迟写盘一次",
                querySubmitted.Contains("SaveSettingsDeferred", StringComparison.Ordinal));
            ChecksContext.Check(
                "创意工坊页: 不再自己判回车键",
                !querySubmitted.Contains("VirtualKey.Enter", StringComparison.Ordinal));
        }

        if (TryGetBlock(
                codeBehind,
                "private void OnUnloaded",
                "private void OnCurrentGameChanged",
                "创意工坊页: 找到 OnUnloaded",
                out var unloaded))
        {
            ChecksContext.Check(
                "创意工坊页: 页面卸载取消在途查询",
                unloaded.Contains("CancelPendingQuery", StringComparison.Ordinal));
        }
    }

    /// <summary>取 <paramref name="startMarker"/> 到 <paramref name="endMarker"/> 之间的源码并记一条 ok 断言。</summary>
    private static bool TryGetBlock(string text, string startMarker, string endMarker, string label, out string block)
    {
        block = string.Empty;
        var start = text.IndexOf(startMarker, StringComparison.Ordinal);
        if (start < 0)
        {
            return ChecksContext.Check(label, false, "找不到起点：" + startMarker);
        }

        var end = text.IndexOf(endMarker, start + startMarker.Length, StringComparison.Ordinal);
        if (end <= start)
        {
            return ChecksContext.Check(label, false, "找不到终点：" + endMarker);
        }

        block = text[start..end];
        return ChecksContext.Check(label, true);
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
