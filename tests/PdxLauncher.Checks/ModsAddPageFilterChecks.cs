namespace PdxLauncher.Checks;

using System.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 「添加模组」页筛选的行为级自检：链接真实 <see cref="AddModsViewModel"/> / <see cref="AddModRowViewModel"/>，
/// 用假的 <see cref="IPlaySetRepository"/> / <see cref="IModCatalog"/> 喂若干来源与版本各异的候选，
/// 直接驱动 VM 属性断言来源、版本、搜索三道条件叠加、切语言按键还原、空状态区分、
/// 整表替换（每次筛选只发一次 Rows 通知）与行实例复用（勾选保留）。
/// <para>断言全部针对运行期结果，不是"文本里有没有这个键"；逻辑错了这些断言会真的失败。</para>
/// </summary>
public static class ModsAddPageFilterChecks
{
    private const string GameVersion = "1.15.0";
    private static readonly Guid PlaySetId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var oldPrefix = Localizer.Prefix;
        try
        {
            // 默认前缀空：T(key) == key，选项文案就是键；与 AchievementViewModelStubs 既有用例一致。
            Localizer.Prefix = string.Empty;
            var vm = CreateLoadedViewModel(out var ids);

            CheckDefaults(vm);
            CheckSourceFilters(vm, ids);
            CheckVersionFilters(vm, ids);
            CheckSearchAndFiltersCombine(vm, ids);
            CheckRowReuseAndSingleReplace(vm, ids);
            CheckSelectionPreservedAcrossFilters(vm, ids);
            CheckEmptyStateDistinction(vm);
            CheckRelocalizeRestoresKey(vm, ids);
            CheckRescansCatalogOnLoad();
        }
        finally
        {
            Localizer.Prefix = oldPrefix;
        }

        return ChecksContext.Failures - before;
    }

    /// <summary>默认应为「全部来源 + 全部版本」，全部候选可见且按名称排序。</summary>
    private static void CheckDefaults(AddModsViewModel vm)
    {
        ChecksContext.Equal("添加模组筛选: 默认来源文案", "ModsAddPageSourceFilterAll", vm.SelectedSourceFilter);
        ChecksContext.Equal("添加模组筛选: 默认版本文案", "ModsAddPageVersionFilterAll", vm.SelectedVersionFilter);
        ChecksContext.Equal("添加模组筛选: 三个来源选项", 3, vm.SourceFilterModes.Count);
        ChecksContext.Equal("添加模组筛选: 三个版本选项", 3, vm.VersionFilterModes.Count);
        ChecksContext.Equal("添加模组筛选: 默认显示全部 5 行", 5, vm.Rows.Count);
        ChecksContext.Check(
            "添加模组筛选: 默认排序名称 A-Z",
            vm.Rows.Select(static row => row.Name).SequenceEqual(["Alpha mod", "Bravo mod", "Charlie mod", "Delta mod", "Echo mod"]),
            string.Join(",", vm.Rows.Select(static row => row.Name)));
    }

    /// <summary>来源=本地/订阅各自只留对应来源的行；两道筛选与搜索无关。</summary>
    private static void CheckSourceFilters(AddModsViewModel vm, FixtureIds ids)
    {
        vm.SelectedSourceFilter = "ModsAddPageSourceFilterLocal";
        ChecksContext.Check(
            "添加模组筛选: 来源=本地只留本地行",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.LocalBelow, ids.LocalOther]),
            Names(vm));
        ChecksContext.Check("添加模组筛选: 来源=本地仍显示有结果", vm.ShowNoMatch is false && vm.ShowEmptyState is false);

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterSubscribed";
        ChecksContext.Check(
            "添加模组筛选: 来源=订阅只留订阅行",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.SubBelow, ids.SubOther, ids.SubUndeclared]),
            Names(vm));

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterAll";
        ChecksContext.Equal("添加模组筛选: 来源=全部恢复 5 行", 5, vm.Rows.Count);
    }

    /// <summary>
    /// 版本=偏低只留 ClassifyVersionSupport==Below 的两行；「其余」正好是补集，
    /// 未声明 / 游戏版本未知 / 不低于都归其余。
    /// </summary>
    private static void CheckVersionFilters(AddModsViewModel vm, FixtureIds ids)
    {
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterBelow";
        ChecksContext.Check(
            "添加模组筛选: 版本=偏低只留 Below 行",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.LocalBelow, ids.SubBelow]),
            Names(vm));
        ChecksContext.Check(
            "添加模组筛选: Below 行确实走 Core 判定",
            vm.Rows.All(static row => row.VersionSupport == PdxLauncher.Core.Playsets.ModVersionSupport.Below));

        vm.SelectedVersionFilter = "ModsAddPageVersionFilterOther";
        ChecksContext.Check(
            "添加模组筛选: 版本=其余是 Below 的补集（含未声明）",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.LocalOther, ids.SubOther, ids.SubUndeclared]),
            Names(vm));
        ChecksContext.Check("添加模组筛选: 其余行都不是 Below",
            vm.Rows.All(static row => row.VersionSupport != PdxLauncher.Core.Playsets.ModVersionSupport.Below));

        vm.SelectedVersionFilter = "ModsAddPageVersionFilterAll";
        ChecksContext.Equal("添加模组筛选: 版本=全部恢复 5 行", 5, vm.Rows.Count);
    }

    /// <summary>搜索 AND 来源 AND 版本叠加：同时满足三条才留下。</summary>
    private static void CheckSearchAndFiltersCombine(AddModsViewModel vm, FixtureIds ids)
    {
        // "mod" 命中全部名字；本地 + 偏低 ⇒ 只剩 LocalBelow。
        vm.SearchText = "mod";
        vm.SelectedSourceFilter = "ModsAddPageSourceFilterLocal";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterBelow";
        ChecksContext.Check(
            "添加模组筛选: 搜索 AND 本地 AND 偏低只剩一行",
            vm.Rows.Count == 1 && vm.Rows[0].ModId == ids.LocalBelow,
            Names(vm));

        // 再收窄到只有订阅行才有的关键词：三道叠加后为空，但候选非空 ⇒ 无匹配而不是空状态。
        vm.SearchText = "Sub";
        ChecksContext.Equal("添加模组筛选: 三道叠加后 0 行", 0, vm.Rows.Count);
        ChecksContext.Check("添加模组筛选: 有候选被筛光显示无匹配", vm.ShowNoMatch && !vm.ShowEmptyState,
            "noMatch=" + vm.ShowNoMatch + " empty=" + vm.ShowEmptyState);

        vm.SearchText = string.Empty;
        vm.SelectedSourceFilter = "ModsAddPageSourceFilterAll";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterAll";
        ChecksContext.Equal("添加模组筛选: 复位后恢复 5 行", 5, vm.Rows.Count);
    }

    /// <summary>每次筛选只换一次 Rows 引用；行实例复用，不清空重建。</summary>
    private static void CheckRowReuseAndSingleReplace(AddModsViewModel vm, FixtureIds ids)
    {
        var beforeRows = vm.Rows.ToArray();
        var events = 0;
        var rowsPropertyChanged = 0;
        void OnChanged(object? sender, PropertyChangedEventArgs e)
        {
            events++;
            if (e.PropertyName == nameof(AddModsViewModel.Rows))
            {
                rowsPropertyChanged++;
            }
        }

        vm.PropertyChanged += OnChanged;
        try
        {
            vm.SelectedSourceFilter = "ModsAddPageSourceFilterLocal";
        }
        finally
        {
            vm.PropertyChanged -= OnChanged;
        }

        // SelectedSourceFilter 自身一次 + Rows 一次 + HasRows/ShowEmptyState/ShowNoMatch/CandidateCountText 各一次。
        ChecksContext.Equal("添加模组筛选: 一次筛选 Rows 只发一次 PropertyChanged", 1, rowsPropertyChanged);
        ChecksContext.Equal("添加模组筛选: 一次筛选共发 6 次属性通知", 6, events);
        ChecksContext.Check("添加模组筛选: 筛选后换了 Rows 引用（整表替换）",
            !ReferenceEquals(beforeRows, vm.Rows));
        ChecksContext.Check("添加模组筛选: 可见行复用了同一批行实例",
            vm.Rows.All(row => beforeRows.Contains(row, ReferenceEqualityComparer.Instance)),
            Names(vm));

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterAll";
        _ = ids;
    }

    /// <summary>行实例复用意味着筛选来回复选中状态留在行上。</summary>
    private static void CheckSelectionPreservedAcrossFilters(AddModsViewModel vm, FixtureIds ids)
    {
        var target = vm.Rows.Single(row => row.ModId == ids.SubUndeclared);
        target.IsSelected = true;
        ChecksContext.Check("添加模组筛选: 勾选行后 VM 有选中项", vm.HasSelection && vm.GetSelectedEntries().Count == 1);

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterLocal"; // 勾选行被筛掉
        ChecksContext.Check("添加模组筛选: 筛掉勾选行后计数仍是 1（行对象保留状态）",
            vm.GetSelectedEntries().Count == 1 && vm.HasSelection,
            "selected=" + vm.GetSelectedEntries().Count);
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterBelow";
        vm.SearchText = "Sub";
        ChecksContext.Check("添加模组筛选: 三道筛选下勾选状态仍不丢", vm.GetSelectedEntries().Count == 1);

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterAll";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterAll";
        vm.SearchText = string.Empty;
        var restored = vm.Rows.Single(row => row.ModId == ids.SubUndeclared);
        ChecksContext.Check("添加模组筛选: 回到全部后同一行仍是勾中态",
            ReferenceEquals(target, restored) && restored.IsSelected);

        restored.IsSelected = false; // 复位，供后续用例使用
        ChecksContext.Check("添加模组筛选: 取消勾选后清空", !vm.HasSelection && vm.GetSelectedEntries().Count == 0);
    }

    /// <summary>
    /// ShowEmptyState 只在候选一条都没有时为真；有候选被筛光走 ShowNoMatch。
    /// 用同一套假仓储直接喂空 mod 库，验证前者不会误触发。
    /// </summary>
    private static void CheckEmptyStateDistinction(AddModsViewModel emptyVm)
    {
        var vm = CreateLoadedViewModel(out _, mods: []);
        ChecksContext.Check("添加模组筛选: 候选为空显示空状态", vm.ShowEmptyState && !vm.ShowNoMatch,
            "empty=" + vm.ShowEmptyState + " noMatch=" + vm.ShowNoMatch);
        ChecksContext.Equal("添加模组筛选: 候选为空时行数为 0", 0, vm.Rows.Count);

        vm.SelectedSourceFilter = "ModsAddPageSourceFilterLocal";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterBelow";
        vm.SearchText = "anything";
        ChecksContext.Check("添加模组筛选: 空候选叠加筛选仍是空状态而非无匹配",
            vm.ShowEmptyState && !vm.ShowNoMatch,
            "empty=" + vm.ShowEmptyState + " noMatch=" + vm.ShowNoMatch);
        _ = emptyVm;
    }

    /// <summary>
    /// 切语言：先改"译文前缀"（模拟 Localizer.Apply 切了资源上下文，旧文案已失效），
    /// 再走与真实 Localizer 相同的 RefreshLocalizedText → Relocalize，断言选中项按键还原到新文案。
    /// </summary>
    private static void CheckRelocalizeRestoresKey(AddModsViewModel vm, FixtureIds ids)
    {
        vm.SelectedSourceFilter = "ModsAddPageSourceFilterSubscribed";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterBelow";
        ChecksContext.Check("添加模组筛选: 切语言前筛选生效",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.SubBelow]), Names(vm));

        Localizer.Prefix = "L2:";
        vm.RefreshLocalizedText();
        // 选项列表整表换成新语言文案；选中项必须也换成新语言文案，而不是旧文案 / 回落「全部」。
        ChecksContext.Check("添加模组筛选: 切语言后来源选中项按键还原",
            vm.SourceFilterModes.Contains(vm.SelectedSourceFilter, StringComparer.Ordinal)
                && vm.SelectedSourceFilter == "L2:ModsAddPageSourceFilterSubscribed",
            vm.SelectedSourceFilter);
        ChecksContext.Check("添加模组筛选: 切语言后版本选中项按键还原",
            vm.VersionFilterModes.Contains(vm.SelectedVersionFilter, StringComparer.Ordinal)
                && vm.SelectedVersionFilter == "L2:ModsAddPageVersionFilterBelow",
            vm.SelectedVersionFilter);
        ChecksContext.Check("添加模组筛选: 切语言后筛选结果不变（没有回落成全部）",
            vm.Rows.Select(static row => row.ModId).Order().SequenceEqual([ids.SubBelow]), Names(vm));

        // 外部硬塞一个对不上任何键的文案：析构时 Resolve 对不上任何文案，
        // 已存的合法 key 仍有效 ⇒ 保留上一次筛选（不误清空用户的选择）。
        // 再把内置 key 也塞成非法值：两级都不认识才回落到「全部」。
        vm.SelectedVersionFilter = "乱码";
        Localizer.Prefix = "L3:";
        vm.RefreshLocalizedText();
        ChecksContext.Equal("添加模组筛选: 非法文案切语言后按上次合法 key 还原",
            "L3:ModsAddPageVersionFilterBelow", vm.SelectedVersionFilter);

        Localizer.Prefix = string.Empty;
        vm.RefreshLocalizedText();
        vm.SelectedSourceFilter = "ModsAddPageSourceFilterAll";
        vm.SelectedVersionFilter = "ModsAddPageVersionFilterAll";
    }

    private static AddModsViewModel CreateLoadedViewModel(out FixtureIds ids, IReadOnlyList<ModLibraryEntry>? mods = null)
    {
        IReadOnlyList<ModLibraryEntry> entries;
        if (mods is null)
        {
            entries = BuildEntries(out ids);
        }
        else
        {
            entries = mods;
            ids = new FixtureIds(Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty, Guid.Empty);
        }

        var repository = new StubPlaySetRepository(entries);
        var catalog = new StubModCatalog();
        var service = new ModLibraryService(repository, catalog, new StubLogger());
        var vm = new AddModsViewModel(repository, service, new StubLogger());
        vm.LoadAsync(Game(), PlaySetId, CancellationToken.None).GetAwaiter().GetResult();
        return vm;
    }

    /// <summary>
    /// 进入本页必须让 mod 扫描缓存失效：刚订阅的工坊 mod 可能已经下载到本地、但官方库还没登记，
    /// 60 秒缓存会让「先看过 Mod 库再进本页」拿到订阅前的快照，表现为 Mod 库有、添加模组没有。
    /// 用带失效计数的最小目录替身直接驱动 <see cref="AddModsViewModel.LoadAsync"/>，验证第二次进入能拿到新条目。
    /// </summary>
    private static void CheckRescansCatalogOnLoad()
    {
        var existingId = Guid.Parse("00000006-0000-0000-0000-000000000006");
        var newId = Guid.Parse("00000007-0000-0000-0000-000000000007");
        var repository = new StubPlaySetRepository(
        [
            Entry(existingId, "Existing", source: "steam", required: "1.21.*", steamId: "100"),
        ]);
        var catalog = new MutableModCatalog(
        [
            new PlaySetMod(
                existingId,
                "Existing mod",
                DirPath: string.Empty,
                ArchivePath: null,
                GameRegistryId: string.Empty,
                ThumbnailPath: null,
                RequiredVersion: "1.21.*",
                Status: "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: "100"),
        ]);
        var service = new ModLibraryService(repository, catalog, new StubLogger());
        var vm = new AddModsViewModel(repository, service, new StubLogger());

        vm.LoadAsync(Game(), PlaySetId, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("添加模组重新扫描: 首次进入 1 个候选", 1, vm.Rows.Count);
        // 每次进入都要先失效再扫：只检查「失效次数随扫描次数同步增长」，
        // 不假设首次进入时全局缓存是否已被别的页面填过。
        ChecksContext.Check(
            "添加模组重新扫描: 每次扫描前都失效了缓存",
            catalog.InvalidateCalls == catalog.DiscoverCalls,
            "discover=" + catalog.DiscoverCalls + " invalidate=" + catalog.InvalidateCalls);

        catalog.Mods.Add(new PlaySetMod(
            newId,
            "Just subscribed mod",
            DirPath: string.Empty,
            ArchivePath: null,
            GameRegistryId: string.Empty,
            ThumbnailPath: null,
            RequiredVersion: "*",
            Status: "ready_to_play",
            Enabled: true,
            Position: 0,
            SteamId: "200"));

        // 模拟「离开后重新进入」：同一个目录实例（缓存仍在），第二次加载必须强制失效再扫。
        vm.LoadAsync(Game(), PlaySetId, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "添加模组重新扫描: 第二次进入仍先失效缓存再扫描",
            catalog.InvalidateCalls == catalog.DiscoverCalls && catalog.DiscoverCalls == 2,
            "discover=" + catalog.DiscoverCalls + " invalidate=" + catalog.InvalidateCalls);
        ChecksContext.Check(
            "添加模组重新扫描: 刚订阅的 mod 出现在候选里",
            vm.Rows.Any(row => row.ModId == newId),
            Names(vm));
        ChecksContext.Check(
            "添加模组重新扫描: 未声明的支持版本（*）不会被当作版本偏低",
            vm.Rows.Single(row => row.ModId == newId).VersionSupport
                == PdxLauncher.Core.Playsets.ModVersionSupport.Undeclared,
            Names(vm));
    }

    private static IReadOnlyList<ModLibraryEntry> BuildEntries(out FixtureIds ids)
    {
        var localBelow = Guid.Parse("00000001-0000-0000-0000-000000000001");
        var localOther = Guid.Parse("00000002-0000-0000-0000-000000000002");
        var subBelow = Guid.Parse("00000003-0000-0000-0000-000000000003");
        var subOther = Guid.Parse("00000004-0000-0000-0000-000000000004");
        var subUndeclared = Guid.Parse("00000005-0000-0000-0000-000000000005");
        ids = new FixtureIds(localBelow, localOther, subBelow, subOther, subUndeclared);

        return
        [
            Entry(localBelow, "Alpha", source: "local", required: "1.0.0"),                 // 本地 + Below
            Entry(localOther, "Charlie", source: "local", required: "1.16.0"),              // 本地 + NotBelow
            Entry(subBelow, "Bravo", source: "steam", required: "1.0.0", steamId: "100"),   // 订阅 + Below
            Entry(subOther, "Delta", source: "steam", required: "1.15.0", steamId: "200"),  // 订阅 + NotBelow
            Entry(subUndeclared, "Echo", source: "steam", required: string.Empty, steamId: "300"), // 订阅 + Undeclared
        ];
    }

    private static ModLibraryEntry Entry(Guid id, string name, string source, string required, string? steamId = null) =>
        new(
            ModId: id,
            Name: name + " mod",
            GameRegistryId: string.Empty,
            DirPath: null,
            ArchivePath: null,
            ThumbnailPath: null,
            RequiredVersion: required,
            Status: "ready_to_play",
            Source: source,
            SizeBytes: null,
            SubscribedOn: null,
            PlaySetIds: [],
            SteamId: steamId);

    private static GameInstallation Game() =>
        new(
            GameId: "test",
            DisplayName: "Test Game",
            Version: GameVersion,
            InstallRoot: "C:\\pdxnext-test",
            LauncherSettingsPath: "C:\\pdxnext-test\\launcher-settings.json",
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: "C:\\pdxnext-test\\game",
            DlcPath: "C:\\pdxnext-test\\dlc",
            ModDir: "C:\\pdxnext-test\\mod",
            ExePath: "C:\\pdxnext-test\\game.exe",
            ExeArgs: [],
            Alternatives: [],
            Theme: new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 14,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: null,
            Support: GameSupport.Full);

    private static string Names(AddModsViewModel vm) => string.Join(",", vm.Rows.Select(static row => row.Name));

    private sealed record FixtureIds(
        Guid LocalBelow,
        Guid LocalOther,
        Guid SubBelow,
        Guid SubOther,
        Guid SubUndeclared);

    private sealed class StubPlaySetRepository(IReadOnlyList<ModLibraryEntry> mods) : IPlaySetRepository
    {
        public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySet>>([]);

        public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<PlaySet?>(null);

        public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
            GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetMod>>([]);

        public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
            GameInstallation game, CancellationToken cancellationToken) => Task.FromResult(mods);

        public Task<AppResult<int>> RemoveWorkshopModAsync(
            GameInstallation game, string workshopId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<int>.Ok(0));

        public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
            GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetDlc>>([]);

        public Task<AppResult<bool>> UpdateModPathsAsync(
            GameInstallation game, Guid modId, string? dirPath, string? archivePath, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> UpdateModRegistryIdAsync(
            GameInstallation game, Guid modId, string? gameRegistryId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> AdoptLocalModAsync(
            GameInstallation game, Guid oldModId, string? oldDirPath, string? oldArchivePath,
            PlaySetMod localMod, CancellationToken cancellationToken) => Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> RevertLocalModAdoptionAsync(
            GameInstallation game, Guid newModId, Guid oldModId, string? oldDirPath, string? oldArchivePath,
            CancellationToken cancellationToken, string? newDirPath = null, string? newArchivePath = null) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> SavePlaySetModsAsync(
            GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetMod> mods, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> SavePlaySetDlcsAsync(
            GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetDlc> dlcs, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<Guid>> CreatePlaySetAsync(
            GameInstallation game, string name, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<Guid>.Ok(Guid.NewGuid()));

        public Task<AppResult<bool>> SetActivePlaySetAsync(
            GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> RenamePlaySetAsync(
            GameInstallation game, Guid playSetId, string name, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));

        public Task<AppResult<bool>> DeletePlaySetAsync(
            GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Ok(true));
    }

    private sealed class StubModCatalog : IModCatalog
    {
        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(
            GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetMod>>([]);
    }

    /// <summary>可变的目录替身：每次扫描返回当前列表，并记录扫描 / 失效次数。</summary>
    private sealed class MutableModCatalog(IReadOnlyList<PlaySetMod>? initial = null)
        : IModCatalog, IScanCacheInvalidator
    {
        public List<PlaySetMod> Mods { get; } = initial is null ? [] : [.. initial];

        public int DiscoverCalls { get; private set; }

        public int InvalidateCalls { get; private set; }

        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(
            GameInstallation game, CancellationToken cancellationToken)
        {
            DiscoverCalls++;
            return Task.FromResult<IReadOnlyList<PlaySetMod>>(Mods.ToList());
        }

        public void InvalidateCache(GameInstallation game) => InvalidateCalls++;

        public void ClearCache() => InvalidateCalls++;
    }

    private sealed class StubLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
