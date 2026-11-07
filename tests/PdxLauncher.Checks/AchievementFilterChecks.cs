namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Imaging;

/// <summary>
/// 成就页筛选 / 搜索自检。这条功能最容易退化的地方都不是编译错误，而是语义悄悄变了：
/// 筛选只过滤了没参与计数的列表（页头统计变成「3 / 3」却只显示一行）、
/// 搜索区分了大小写（英文成就名大小写混乱）、把「筛没了」说成「这款游戏没有成就」、
/// 切游戏后旧筛选条件还留着、以及每次改筛选都把 198 个 DDS 重新解码一遍。
/// 所以这里既读 XAML / 代码文本钉住契约，也用真实 ViewModel + 真 DdsPngCache 钉住行为。
/// </summary>
public static class AchievementFilterChecks
{
    private const string FilterAll = "achievements.filter.all";
    private const string FilterCount = "achievements.filter.count";
    private const string FilterEmptyMessage = "achievements.filter.empty.message";
    private const string FilterEmptyTitle = "achievements.filter.empty.title";
    private const string FilterLocked = "achievements.filter.locked";
    private const string FilterName = "achievements.filter.name";
    private const string FilterUnlocked = "achievements.filter.unlocked";
    private const string SearchName = "achievements.search.name";
    private const string SearchPlaceholder = "achievements.search.placeholder";

    private static readonly string[] FilterKeys =
    [
        FilterAll,
        FilterCount,
        FilterEmptyMessage,
        FilterEmptyTitle,
        FilterLocked,
        FilterName,
        FilterUnlocked,
        SearchName,
        SearchPlaceholder,
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-achfilter-" + Guid.NewGuid().ToString("N"));
        try
        {
            var workspace = FindWorkspaceRoot();
            CheckPageContract(workspace);
            CheckCodeBehind(workspace);
            CheckFiltering(fs, root);
            CheckGameSwitchResetsFilters(fs, root);
            CheckLazyIcons(fs, root);
            CheckI18n(workspace);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果。
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ XAML / 代码契约

    private static void CheckPageContract(string workspace)
    {
        var xamlPath = Path.Combine(workspace, "src", "PdxLauncher.App", "Views", "AchievementsPage.xaml");
        if (!ChecksContext.Check("成就筛选: AchievementsPage.xaml 存在", File.Exists(xamlPath), xamlPath))
        {
            return;
        }

        var xaml = File.ReadAllText(xamlPath);

        // 三个筛选项：RadioButtons 的 ItemsSource / SelectedIndex 双向绑定缺一不可，
        // 少了 SelectedIndex 就是「点了不生效」的假工具栏。
        ChecksContext.Check(
            "成就筛选: 工具条有三个筛选项（RadioButtons）",
            xaml.Contains("<RadioButtons", StringComparison.Ordinal)
                && xaml.Contains("MaxColumns=\"3\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就筛选: 筛选项绑定 ViewModel.FilterOptions",
            xaml.Contains("ItemsSource=\"{x:Bind ViewModel.FilterOptions, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就筛选: 选中项与 FilterIndex 双向绑定",
            xaml.Contains("SelectedIndex=\"{x:Bind ViewModel.FilterIndex, Mode=TwoWay}\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就筛选: 搜索框约 200 宽且输入即筛",
            xaml.Contains("Width=\"200\"", StringComparison.Ordinal)
                && xaml.Contains("Text=\"{x:Bind ViewModel.SearchText, Mode=OneWay}\"", StringComparison.Ordinal)
                && xaml.Contains("TextChanged=\"OnSearchTextChanged\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就筛选: 右侧显示「显示 N / 共 M」",
            xaml.Contains("Text=\"{x:Bind ViewModel.VisibleCountText, Mode=OneWay}\"", StringComparison.Ordinal)
                && xaml.Contains("txtAchievementFilterCount", StringComparison.Ordinal));
        ChecksContext.Check(
            "成就筛选: 工具条在列表上方（4 行布局，列表位于最后一行）",
            Regex.Matches(xaml, "<RowDefinition", RegexOptions.CultureInvariant).Count == 4
                && Regex.IsMatch(
                    xaml,
                    @"<ListView\s+Grid\.Row=""3""",
                    RegexOptions.CultureInvariant));

        // 空状态必须区分：筛选筛空 ≠ 没有数据源。面板 id 不同、绑定属性不同。
        ChecksContext.Check(
            "成就筛选: 筛选空状态与数据源空状态是两个面板",
            xaml.Contains("panelAchievementFilterEmpty", StringComparison.Ordinal)
                && xaml.Contains("panelAchievementsEmpty", StringComparison.Ordinal)
                && xaml.Contains("ViewModel.ShowFilterEmptyState", StringComparison.Ordinal));
    }

    private static void CheckCodeBehind(string workspace)
    {
        var pagePath = Path.Combine(workspace, "src", "PdxLauncher.App", "Views", "AchievementsPage.xaml.cs");
        if (!ChecksContext.Check("成就筛选: AchievementsPage.xaml.cs 存在", File.Exists(pagePath), pagePath))
        {
            return;
        }

        var page = File.ReadAllText(pagePath);
        ChecksContext.Check(
            "成就筛选: 搜索回调只处理用户输入（不抢程序化回写）",
            Regex.IsMatch(
                page,
                @"args\.Reason\s*!=\s*AutoSuggestionBoxTextChangeReason\.UserInput",
                RegexOptions.CultureInvariant)
                && page.Contains("_viewModel.SearchText = text;", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ 过滤 / 搜索行为

    private static void CheckFiltering(IFileSystem fs, string root)
    {
        var game = Game(Path.Combine(root, "game-a"), appId: 529340, gameId: "victoria3");
        var viewModel = NewViewModel(fs, Path.Combine(root, "cache-a"), Snapshot(), null);
        viewModel.LoadAsync(game, CancellationToken.None).GetAwaiter().GetResult();

        // 1) 三个筛选项存在且互不相同（写在 ViewModel 而不是散在 XAML 里，切语言整表重建）。
        ChecksContext.Equal("成就筛选: 三个筛选项", 3, viewModel.FilterOptions.Count);
        ChecksContext.Check(
            "成就筛选: 三个筛选项互不相同",
            viewModel.FilterOptions.Distinct(StringComparer.Ordinal).Count() == 3,
            string.Join(" | ", viewModel.FilterOptions));
        ChecksContext.Check(
            "成就筛选: 默认「全部」且无搜索词",
            viewModel.FilterIndex == 0 && viewModel.SearchText.Length == 0 && !viewModel.HasActiveFilter);
        ChecksContext.Equal("成就筛选: 默认显示全部成就", 3, viewModel.Achievements.Count);

        // 2) 过滤后计数正确：只在客户端重排，行数 = 状态命中数。
        var allRows = viewModel.Achievements.ToArray();
        viewModel.FilterIndex = 1;
        ChecksContext.Equal("成就筛选: 已解锁过滤后只剩 2 行", 2, viewModel.Achievements.Count);
        ChecksContext.Check(
            "成就筛选: 已解锁过滤结果全是已解锁",
            viewModel.Achievements.All(static row => row.IsUnlocked));
        ChecksContext.Check(
            "成就筛选: 过滤后行数少于总量，但总计数不变",
            viewModel.TotalCount == 3 && viewModel.UnlockedCount == 2,
            viewModel.TotalCount.ToString(CultureInfo.InvariantCulture));

        viewModel.FilterIndex = 2;
        ChecksContext.Equal("成就筛选: 未解锁过滤后只剩 1 行", 1, viewModel.Achievements.Count);
        ChecksContext.Check(
            "成就筛选: 未解锁过滤结果确实未解锁",
            viewModel.Achievements.Count == 1 && !viewModel.Achievements[0].IsUnlocked);

        // 越界（RadioButtons 初始化会给 -1）必须回落到「全部」，不能停在「什么都没选」。
        viewModel.FilterIndex = -1;
        ChecksContext.Check(
            "成就筛选: 越界序号回落到全部",
            viewModel.FilterIndex == 0 && viewModel.Achievements.Count == 3,
            viewModel.FilterIndex.ToString(CultureInfo.InvariantCulture));

        // 3) 搜索大小写不敏感（名称）。
        viewModel.SearchText = "VICTORY";
        ChecksContext.Check(
            "成就筛选: 名称搜索大小写不敏感",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "ACH_WIN",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));
        viewModel.SearchText = "victory";
        ChecksContext.Check(
            "成就筛选: 小写关键词命中同一行",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "ACH_WIN",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));

        // 4) 搜索也匹配说明文本。
        viewModel.SearchText = "RIVAL";
        ChecksContext.Check(
            "成就筛选: 说明文本参与搜索且大小写不敏感",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "ACH_LOSS",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));

        // 5) 筛选与搜索组合生效（不是「二选一」）。
        viewModel.FilterIndex = 1;
        viewModel.SearchText = "war";
        ChecksContext.Check(
            "成就筛选: 已解锁 + 关键词只留下交集",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "ACH_WIN",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("成就筛选: 组合筛选下处于「已收窄」状态", viewModel.HasActiveFilter);

        // 6) 无结果：筛选空状态亮、数据源空状态不亮，计数是 0 / 3。
        viewModel.SearchText = "zzzz-no-hit";
        ChecksContext.Check(
            "成就筛选: 无结果时显示筛选空状态而不是数据源空状态",
            viewModel.ShowFilterEmptyState && !viewModel.ShowEmptyState && viewModel.Achievements.Count == 0);
        ChecksContext.Check(
            "成就筛选: 无结果时计数说「0 / 共 3」",
            viewModel.VisibleCountText.StartsWith(FilterCount, StringComparison.Ordinal)
                && viewModel.VisibleCountText.Contains('0')
                && viewModel.VisibleCountText.Contains('3'),
            viewModel.VisibleCountText);

        // 7) 清空条件后恢复全量，并回到「未收窄」。
        viewModel.SearchText = string.Empty;
        viewModel.FilterIndex = 0;
        ChecksContext.Check(
            "成就筛选: 清空条件后恢复全部成就",
            viewModel.Achievements.Count == 3 && !viewModel.HasActiveFilter && !viewModel.ShowFilterEmptyState);

        // 8) 行实例复用：过滤不是重建，图标工厂不会被重新挂上（惰性见 CheckLazyIcons）。
        ChecksContext.Check(
            "成就筛选: 过滤复用原行实例",
            viewModel.Achievements.Count == 3
                && viewModel.Achievements.All(row => allRows.Contains(row)));
    }

    /// <summary>切游戏必须把「全部 + 空搜索」重置回来，否则上一款游戏的筛选会静默过滤新列表。</summary>
    private static void CheckGameSwitchResetsFilters(IFileSystem fs, string root)
    {
        var gameA = Game(Path.Combine(root, "switch-a"), appId: 529340, gameId: "victoria3");
        var gameB = Game(Path.Combine(root, "switch-b"), appId: 1158310, gameId: "crusader-kings-3");
        var viewModel = NewViewModel(fs, Path.Combine(root, "cache-switch"), Snapshot(), null);
        viewModel.LoadAsync(gameA, CancellationToken.None).GetAwaiter().GetResult();

        viewModel.FilterIndex = 2;
        viewModel.SearchText = "victory";
        ChecksContext.Check("成就筛选: 切游戏前确实处于收窄状态", viewModel.HasActiveFilter);

        viewModel.LoadAsync(gameB, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "成就筛选: 切游戏重置为「全部」",
            viewModel.FilterIndex == 0,
            viewModel.FilterIndex.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            "成就筛选: 切游戏清空搜索词",
            viewModel.SearchText.Length == 0,
            viewModel.SearchText);
        ChecksContext.Check(
            "成就筛选: 切游戏后新游戏列表完整可见",
            !viewModel.HasActiveFilter && !viewModel.ShowFilterEmptyState && viewModel.Achievements.Count == 3,
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// 筛选不能把图标从「惰性」变成「进页面就全解」：解码次数只该由「读了哪一行」决定。
    /// 用真 DdsPngCache.DecodeCount 计数，改筛选 / 搜索前后对比。
    /// </summary>
    private static void CheckLazyIcons(IFileSystem fs, string root)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var gameRoot = Path.Combine(root, "lazy-game");
        var iconDir = Path.Combine(gameRoot, "game", "gfx", "interface", "icons", "achievements");
        Directory.CreateDirectory(iconDir);
        foreach (var id in new[] { "ACH_WIN", "ACH_LOSS", "ACH_MAP" })
        {
            File.WriteAllBytes(Path.Combine(iconDir, id + ".dds"), Bc1Dds(4, 4));
        }

        var iconCache = new DdsPngCache(Path.Combine(root, "lazy-icons"), maxDimension: 96);
        var viewModel = NewViewModel(fs, Path.Combine(root, "cache-lazy"), Snapshot(), iconCache);
        viewModel.LoadAsync(Game(gameRoot, appId: 529340, gameId: "victoria3"), CancellationToken.None)
            .GetAwaiter().GetResult();

        ChecksContext.Equal("成就筛选: 进页面不解码任何图标", 0, iconCache.DecodeCount);

        var first = viewModel.Achievements[0];
        var icon = first.IconUrl;
        ChecksContext.Check(
            "成就筛选: 读到某行才解码它的图标",
            iconCache.DecodeCount == 1 && icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase),
            "decodes=" + iconCache.DecodeCount.ToString(CultureInfo.InvariantCulture) + " icon=" + icon);

        viewModel.FilterIndex = 2;
        viewModel.SearchText = "loss";
        viewModel.FilterIndex = 0;
        viewModel.SearchText = string.Empty;
        ChecksContext.Equal("成就筛选: 反复改筛选不重新解码图标", 1, iconCache.DecodeCount);
        ChecksContext.Check(
            "成就筛选: 复用的行仍带着已解析的图标",
            ReferenceEquals(viewModel.Achievements[0], first) && viewModel.Achievements[0].IconUrl == icon,
            viewModel.Achievements[0].IconUrl);
    }

    // ------------------------------------------------------------ i18n

    private static void CheckI18n(string workspace)
    {
        foreach (var language in new[] { "zh-CN", "en-US" })
        {
            var path = I18nResw.ResourcesPath(workspace, language);
            if (!ChecksContext.Check("成就筛选: " + language + " Resources.resw 存在", File.Exists(path), path))
            {
                continue;
            }

            var table = I18nResw.Read(workspace, language);
            foreach (var key in FilterKeys)
            {
                ChecksContext.Check(
                    "成就筛选: " + language + " 有 " + key,
                    !string.IsNullOrWhiteSpace(I18nResw.Get(table, key)));
            }

            // 「显示 N / 共 M」必须带两个占位符，否则计数永远显示成字面量。
            var countText = I18nResw.Get(table, FilterCount);
            ChecksContext.Check(
                "成就筛选: " + language + " filter.count 带两个占位符",
                !string.IsNullOrWhiteSpace(countText)
                    && countText.Contains("{0}", StringComparison.Ordinal)
                    && countText.Contains("{1}", StringComparison.Ordinal));
        }

        // 筛选筛空的文案必须区别于「没有数据源」的三条：用户不能把「筛没了」读成「这游戏没成就」。
        var zh = I18nResw.ResourcesPath(workspace, "zh-CN");
        if (!File.Exists(zh))
        {
            return;
        }

        var zhTable = I18nResw.Read(workspace, "zh-CN");
        var filterEmptyTitle = I18nResw.Get(zhTable, FilterEmptyTitle) ?? string.Empty;
        var filterEmptyMessage = I18nResw.Get(zhTable, FilterEmptyMessage) ?? string.Empty;
        var unavailable = new[]
        {
            I18nResw.Get(zhTable, "achievements.unavailable.empty") ?? string.Empty,
            I18nResw.Get(zhTable, "achievements.unavailable.notSupported") ?? string.Empty,
            I18nResw.Get(zhTable, "achievements.unavailable.steamUnavailable") ?? string.Empty,
        };

        ChecksContext.Check(
            "成就筛选: 筛选空状态文案与「没有数据源」三句都不同",
            filterEmptyTitle.Length > 0
                && filterEmptyMessage.Length > 0
                && !unavailable.Contains(filterEmptyTitle, StringComparer.Ordinal)
                && !unavailable.Contains(filterEmptyMessage, StringComparer.Ordinal),
            filterEmptyTitle + " / " + filterEmptyMessage);
    }

    private static string Read(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static bool HasNonEmptyString(JsonElement root, string key) =>
        root.TryGetProperty(key, out var value)
        && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString());

    // ------------------------------------------------------------ 工具

    private static AchievementsViewModel NewViewModel(
        IFileSystem fs,
        string cacheDirectory,
        AchievementSnapshot snapshot,
        DdsPngCache? iconCache)
    {
        var cache = new AchievementSnapshotCache(fs, cacheDirectory);
        var source = new CompletedSource(snapshot);
        return iconCache is null
            ? new AchievementsViewModel(source, cache)
            : new AchievementsViewModel(source, cache, iconCache);
    }

    /// <summary>三条可命中的成就：两条已解锁、一条未解锁，说明文本里各埋一个关键词。</summary>
    private static AchievementSnapshot Snapshot() => new()
    {
        Availability = AchievementAvailability.Available,
        FetchedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Entries =
        [
            new AchievementEntry("ACH_WIN", "Grand Victory", "Win a war", false, true, null, 42.0, null),
            new AchievementEntry("ACH_LOSS", "Total Defeat", "Lose a war to a rival", false, false, null, null, null),
            new AchievementEntry("ACH_MAP", "World Map", "Discover every province", false, true, null, 7.5, null),
        ],
    };

    private static byte[] Bc1Dds(int width, int height)
    {
        int blocks = ((width + 3) / 4) * ((height + 3) / 4);
        var bytes = new byte[128 + (blocks * 8)];
        WriteU32(bytes, 0, 0x20534444);
        WriteU32(bytes, 4, 124);
        WriteU32(bytes, 8, 0x81007);
        WriteU32(bytes, 12, (uint)height);
        WriteU32(bytes, 16, (uint)width);
        WriteU32(bytes, 24, 1);
        WriteU32(bytes, 28, 1);
        WriteU32(bytes, 76, 32);
        WriteU32(bytes, 80, 4);
        WriteU32(bytes, 84, 0x31545844);
        return bytes;
    }

    private static void WriteU32(byte[] buffer, int offset, uint value) =>
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private static GameInstallation Game(string installRoot, int appId, string gameId) => new(
        GameId: gameId,
        DisplayName: gameId,
        Version: "test",
        InstallRoot: installRoot,
        LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: Path.Combine(installRoot, "game"),
        DlcPath: Path.Combine(installRoot, "dlc"),
        ModDir: Path.Combine(installRoot, "mod"),
        ExePath: Path.Combine(installRoot, "binaries", "game.exe"),
        ExeArgs: Array.Empty<string>(),
        Alternatives: Array.Empty<AlternativeExecutable>(),
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
        SteamAppId: appId,
        Support: GameSupport.LaunchOnly);

    private sealed class CompletedSource(AchievementSnapshot snapshot) : IAchievementSource
    {
        public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
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
