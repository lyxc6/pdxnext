namespace PdxLauncher.Checks;

using System.Buffers.Binary;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Imaging;

/// <summary>
/// CK3 的 DDS 成就图标自检：解析优先级、DDS → PNG 落盘缓存、损坏输入降级、
/// 惰性解码（进页面不解码，读到某行才解），以及空快照防御。
/// 全部用真实临时目录与真实文件系统，钉住「绝不把 .dds 交给 BitmapImage」这条硬约束。
/// </summary>
public static class AchievementIconCacheChecks
{
    private const string AppId = "1158310";
    private const uint DdpfFourCc = 4;
    private const uint FourCcDxt1 = 0x31545844;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-achdds-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckResolverPriority(Path.Combine(tempRoot, "priority"));
            CheckCacheConversions(Path.Combine(tempRoot, "icons"));
            CheckEmptySnapshotDefense(fs, Path.Combine(tempRoot, "vm"));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 解析优先级

    private static void CheckResolverPriority(string root)
    {
        var icons = Path.Combine(root, "game", "gfx", "interface", "icons", "achievements");
        Directory.CreateDirectory(icons);

        // 只有 DDS：Resolve 必须返回 null（界面不认 DDS），ResolveSource 才回退到 DDS。
        File.WriteAllBytes(Path.Combine(icons, "ACH_DDS.dds"), Bc1Dds(4, 4));
        ChecksContext.Check(
            "DDS 图标: Resolve 在只有 .dds 时返回 null（不把 dds 交给 BitmapImage）",
            AchievementIconResolver.Resolve(root, "ACH_DDS", unlocked: true) is null);
        var dds = AchievementIconResolver.ResolveSource(root, "ACH_DDS", unlocked: true);
        ChecksContext.Check(
            "DDS 图标: ResolveSource 回退到 .dds",
            dds is not null && dds.EndsWith("ACH_DDS.dds", StringComparison.Ordinal),
            dds ?? "null");

        // VIC3 同时有 jpg 和 dds：位图必须赢，不能白白多生成一份 PNG。
        File.WriteAllBytes(Path.Combine(icons, "ACH_BOTH.jpg"), [1, 2, 3]);
        File.WriteAllBytes(Path.Combine(icons, "ACH_BOTH.dds"), Bc1Dds(4, 4));
        var both = AchievementIconResolver.ResolveSource(root, "ACH_BOTH", unlocked: true);
        ChecksContext.Check(
            "DDS 图标: jpg 优先于 dds",
            both is not null && both.EndsWith("ACH_BOTH.jpg", StringComparison.Ordinal),
            both ?? "null");

        // 未解锁：_notachieved.dds 优先于原图 .dds（不能被原图抢先）。
        File.WriteAllBytes(Path.Combine(icons, "ACH_LOCKED.dds"), Bc1Dds(4, 4));
        File.WriteAllBytes(Path.Combine(icons, "ACH_LOCKED_notachieved.dds"), Bc1Dds(4, 4));
        var locked = AchievementIconResolver.ResolveSource(root, "ACH_LOCKED", unlocked: false);
        ChecksContext.Check(
            "DDS 图标: 未解锁优先取 _notachieved.dds",
            locked is not null && locked.EndsWith("ACH_LOCKED_notachieved.dds", StringComparison.Ordinal),
            locked ?? "null");

        // 未解锁但只有原图：退回原图（与位图逻辑一致）。
        var lockedFallback = AchievementIconResolver.ResolveSource(root, "ACH_DDS", unlocked: false);
        ChecksContext.Check(
            "DDS 图标: 缺 _notachieved 时退回原图 dds",
            lockedFallback is not null && lockedFallback.EndsWith("ACH_DDS.dds", StringComparison.Ordinal),
            lockedFallback ?? "null");

        ChecksContext.Check(
            "DDS 图标: 拒绝路径穿越（ResolveSource）",
            AchievementIconResolver.ResolveSource(root, "..\\ACH_DDS", unlocked: true) is null);
    }

    // ------------------------------------------------------------ DDS → PNG 缓存

    private static void CheckCacheConversions(string root)
    {
        var icons = Path.Combine(root, "game", "gfx", "interface", "icons", "achievements");
        Directory.CreateDirectory(icons);
        var cacheRoot = Path.Combine(root, "cache");

        var source = Path.Combine(icons, "ACH_ONE.dds");
        File.WriteAllBytes(source, Bc1Dds(4, 4));
        var cache = new DdsPngCache(cacheRoot, maxDimension: 96);

        var png = cache.Ensure(source, Path.Combine("1158310", "ACH_ONE.png"));
        ChecksContext.Check(
            "DDS 缓存: 首次解码生成 PNG 且路径在 <appid>/ 下",
            png is not null
                && png.StartsWith(Path.Combine(cacheRoot, "1158310"), StringComparison.OrdinalIgnoreCase)
                && png.EndsWith("ACH_ONE.png", StringComparison.Ordinal),
            png ?? "null");
        ChecksContext.Check("DDS 缓存: 生成文件存在", png is not null && File.Exists(png), png ?? "null");
        ChecksContext.Check(
            "DDS 缓存: 生成的是合法 PNG",
            png is not null && IsPng(File.ReadAllBytes(png)));
        ChecksContext.Equal("DDS 缓存: 首次解码只解一次", 1, cache.DecodeCount);
        if (png is not null)
        {
            var (width, height) = ReadPngSize(File.ReadAllBytes(png));
            ChecksContext.Check("DDS 缓存: 4×4 原图不被放大", width == 4 && height == 4, width + "x" + height);
        }

        // 第二次命中：不再解码，即使源文件已经不在了也要命中。
        File.Delete(source);
        var hit = cache.Ensure(source, Path.Combine("1158310", "ACH_ONE.png"));
        ChecksContext.Equal("DDS 缓存: 二次命中不再解码", 1, cache.DecodeCount);
        ChecksContext.Check("DDS 缓存: 二次命中返回同一路径", string.Equals(hit, png, StringComparison.Ordinal), hit ?? "null");

        // 缩放只缩不放：8×8 缩到 4×4。
        var bigSource = Path.Combine(icons, "ACH_BIG.dds");
        File.WriteAllBytes(bigSource, Bc1Dds(8, 8));
        var scaled = new DdsPngCache(Path.Combine(root, "cache-scaled"), maxDimension: 4)
            .Ensure(bigSource, Path.Combine("1158310", "ACH_BIG.png"));
        if (ChecksContext.Check("DDS 缓存: maxDimension 缩放生成成功", scaled is not null && File.Exists(scaled), scaled ?? "null"))
        {
            var (width, height) = ReadPngSize(File.ReadAllBytes(scaled!));
            ChecksContext.Check("DDS 缓存: 8×8 缩到 4×4", width == 4 && height == 4, width + "x" + height);
        }

        // 损坏 DDS：返回 null、不留目标文件、不解出坏图。
        var badSource = Path.Combine(icons, "ACH_BAD.dds");
        File.WriteAllBytes(badSource, "this is definitely not a dds file"u8.ToArray());
        var badCache = new DdsPngCache(Path.Combine(root, "cache-bad"));
        var badTarget = Path.Combine("1158310", "ACH_BAD.png");
        var bad = badCache.Ensure(badSource, badTarget);
        ChecksContext.Check("DDS 缓存: 损坏 DDS 返回 null", bad is null, bad ?? "null");
        ChecksContext.Check(
            "DDS 缓存: 损坏 DDS 不留下目标文件",
            !File.Exists(Path.Combine(badCache.DirectoryPath, badTarget)));
        ChecksContext.Equal("DDS 缓存: 损坏 DDS 计入一次尝试", 1, badCache.DecodeCount);

        // 路径防御：绝对路径、..、非 png 扩展名都拒绝。
        ChecksContext.Check(
            "DDS 缓存: 拒绝绝对目标路径",
            cache.Ensure(source, Path.Combine(cacheRoot, "1158310", "evil.png")) is null);
        ChecksContext.Check(
            "DDS 缓存: 拒绝 .. 穿越",
            cache.Ensure(source, Path.Combine("..", "evil.png")) is null);
        ChecksContext.Check(
            "DDS 缓存: 拒绝非 png 目标",
            cache.Ensure(source, Path.Combine("1158310", "evil.jpg")) is null);

        CheckPngSelfHealing(icons, root);
        CheckConcurrentGeneration(icons, root);
    }

    // ------------------------------------------------------------ PNG 缓存自愈

    private static void CheckPngSelfHealing(string icons, string root)
    {
        var source = Path.Combine(icons, "ACH_HEAL.dds");
        File.WriteAllBytes(source, Bc1Dds(4, 4));
        var cache = new DdsPngCache(Path.Combine(root, "cache-heal"), maxDimension: 96);
        const string Relative = "1158310\\ACH_HEAL.png";
        var target = Path.Combine(cache.DirectoryPath, Relative);

        var seeded = cache.Ensure(source, Relative);
        ChecksContext.Check("DDS 缓存自愈: 预生成成功", seeded is not null && File.Exists(target), seeded ?? "null");
        var goodBytes = File.Exists(target) ? File.ReadAllBytes(target) : [];
        ChecksContext.Check(
            "DDS 缓存自愈: 预生成是完整 PNG",
            IsPng(goodBytes) && goodBytes.Length >= 64,
            goodBytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));

        // 半截 PNG 用「删掉 IEND」构造：签名、IHDR、宽高全都合法，只有 IEND 校验能发现它。
        byte[][] corrupt =
        [
            [],
            "not-a-png"u8.ToArray(),
            goodBytes.Length > 12 ? goodBytes[..^12] : [],
        ];
        string[] labels = ["0 字节", "9 字节垃圾", "半截 PNG（缺 IEND）"];

        var before = cache.DecodeCount;
        for (int i = 0; i < corrupt.Length; i++)
        {
            File.WriteAllBytes(target, corrupt[i]);
            var healed = cache.Ensure(source, Relative);
            var bytes = healed is not null && File.Exists(healed) ? File.ReadAllBytes(healed) : [];
            var (width, height) = bytes.Length >= 24 ? ReadPngSize(bytes) : (0, 0);
            ChecksContext.Check(
                "DDS 缓存自愈: " + labels[i] + " 被重建为有效 PNG",
                healed is not null && IsPng(bytes) && bytes.Length >= 64 && width > 0 && height > 0,
                healed ?? "null");
        }

        ChecksContext.Equal("DDS 缓存自愈: 三次坏缓存各重建一次", 3, cache.DecodeCount - before);

        var hit = cache.Ensure(source, Relative);
        ChecksContext.Check("DDS 缓存自愈: 重建后再命中不再解码", hit is not null && cache.DecodeCount - before == 3, "decodes=" + (cache.DecodeCount - before));
    }

    // ------------------------------------------------------------ 并发同 key

    private static void CheckConcurrentGeneration(string icons, string root)
    {
        var source = Path.Combine(icons, "ACH_CONC.dds");
        File.WriteAllBytes(source, Bc1Dds(8, 8));
        var cache = new DdsPngCache(Path.Combine(root, "cache-conc"), maxDimension: 96);

        const int Threads = 6;
        const int Rounds = 3;
        for (int round = 0; round < Rounds; round++)
        {
            // 每轮都从「没有缓存」开始，才会真的发生 6 线程抢同一次生成。
            string relative = Path.Combine("1158310", "ACH_CONC_" + round.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");
            string target = Path.Combine(cache.DirectoryPath, relative);
            if (File.Exists(target))
            {
                File.Delete(target);
            }

            var results = new string?[Threads];
            var errors = new Exception?[Threads];
            using (var barrier = new Barrier(Threads))
            {
                var threads = new Thread[Threads];
                for (int i = 0; i < threads.Length; i++)
                {
                    int index = i;
                    threads[i] = new Thread(() =>
                    {
                        try
                        {
                            barrier.SignalAndWait();
                            results[index] = cache.Ensure(source, relative);
                        }
                        catch (Exception ex)
                        {
                            errors[index] = ex;
                        }
                    });
                    threads[i].Start();
                }

                foreach (var thread in threads)
                {
                    thread.Join();
                }
            }

            ChecksContext.Check(
                "DDS 缓存并发: 第 " + (round + 1) + " 轮 6 线程都拿到同一个有效路径",
                errors.All(static e => e is null)
                    && results.All(r => r is not null && string.Equals(r, results[0], StringComparison.OrdinalIgnoreCase))
                    && File.Exists(target)
                    && IsPng(File.ReadAllBytes(target)),
                string.Join(",", results.Select(static r => r ?? "null")));
        }

        ChecksContext.Equal("DDS 缓存并发: 三轮各只解码一次", Rounds, cache.DecodeCount);
        ChecksContext.Check(
            "DDS 缓存并发: 不残留 .tmp 临时文件",
            Directory.GetFiles(cache.DirectoryPath, "*.tmp", SearchOption.AllDirectories).Length == 0);
    }

    // ------------------------------------------------------------ ViewModel：惰性 + 空快照

    private static void CheckEmptySnapshotDefense(IFileSystem fs, string root)
    {
        Directory.CreateDirectory(root);

        // 1) 数据源返回 Available + 空数组：不能写缓存，界面按 Empty 解释。
        var emptyCache = new AchievementSnapshotCache(fs, Path.Combine(root, "cache-empty"));
        var emptyVm = new AchievementsViewModel(
            new CompletedSource(AvailableSnapshot()),
            emptyCache,
            new DdsPngCache(Path.Combine(root, "icons-empty")));
        emptyVm.LoadAsync(Game(Path.Combine(root, "game-empty")), CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check("空快照防御: Available+0 条按 Empty 展示", emptyVm.Availability == AchievementAvailability.Empty, emptyVm.Availability.ToString());
        ChecksContext.Check("空快照防御: 空结果不写缓存", !CachedFileExists(emptyCache), DescribeCache(emptyCache));

        // 2) 已存在的空快照（旧版本写的）必须视为未命中，并在刷新失败时不冒充成功快照。
        var poisonCache = new AchievementSnapshotCache(fs, Path.Combine(root, "cache-poison"));
        ChecksContext.Check("空快照防御: 预置旧版空快照成功", poisonCache.TrySave(AppId, AvailableSnapshot()));
        var pending = new TaskCompletionSource<AchievementSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var poisonVm = new AchievementsViewModel(
            new PendingSource(pending.Task),
            poisonCache,
            new DdsPngCache(Path.Combine(root, "icons-poison")));
        var loading = poisonVm.LoadAsync(Game(Path.Combine(root, "game-poison")), CancellationToken.None);

        ChecksContext.Check(
            "空快照防御: 预置空快照不被复用（等待刷新时列表仍为空）",
            poisonVm.Achievements.Count == 0 && !poisonVm.HasSnapshot,
            "rows=" + poisonVm.Achievements.Count);
        ChecksContext.Check("空快照防御: 空快照文件被删除", !CachedFileExists(poisonCache), DescribeCache(poisonCache));

        pending.SetResult(new AchievementSnapshot
        {
            Availability = AchievementAvailability.SteamUnavailable,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Entries = [],
        });
        loading.GetAwaiter().GetResult();
        ChecksContext.Check(
            "空快照防御: 刷新失败时不会把空快照当成可用数据",
            poisonVm.Availability == AchievementAvailability.SteamUnavailable,
            poisonVm.Availability.ToString());
        ChecksContext.Check("空快照防御: 失败后仍无快照", !poisonVm.HasSnapshot);

        // 3) 非空缓存 + 刷新失败：照旧保留（回归，不因空快照防御被误伤）。
        var goodCache = new AchievementSnapshotCache(fs, Path.Combine(root, "cache-good"));
        var goodSnapshot = new AchievementSnapshot
        {
            Availability = AchievementAvailability.Available,
            FetchedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            Entries = [Entry("ACH_GOOD", unlocked: true)],
        };
        ChecksContext.Check("空快照防御: 预置非空缓存成功", goodCache.TrySave(AppId, goodSnapshot));
        var goodPending = new TaskCompletionSource<AchievementSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var goodVm = new AchievementsViewModel(new PendingSource(goodPending.Task), goodCache);
        var goodLoad = goodVm.LoadAsync(Game(Path.Combine(root, "game-good")), CancellationToken.None);
        ChecksContext.Check(
            "空快照防御: 非空缓存仍先渲染",
            goodVm.Achievements.Count == 1 && goodVm.HasSnapshot,
            "rows=" + goodVm.Achievements.Count);
        goodPending.SetResult(new AchievementSnapshot
        {
            Availability = AchievementAvailability.SteamUnavailable,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Entries = [],
        });
        goodLoad.GetAwaiter().GetResult();
        ChecksContext.Check(
            "空快照防御: 刷新失败仍保留非空缓存",
            goodVm.Achievements.Count == 1 && goodVm.Availability == AchievementAvailability.Available,
            "rows=" + goodVm.Achievements.Count + " " + goodVm.Availability);

        // 4) 空状态遮挡：不可用与行同时存在时，只留列表，不许再叠一条「没有可显示的成就」。
        var staleVm = new AchievementsViewModel(
            new CompletedSource(new AchievementSnapshot
            {
                Availability = AchievementAvailability.SteamUnavailable,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Entries = [Entry("ACH_STALE", unlocked: true)],
            }),
            null,
            new DdsPngCache(Path.Combine(root, "icons-stale")));
        staleVm.LoadAsync(Game(Path.Combine(root, "game-stale")), CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "空状态遮挡: 有行 + SteamUnavailable 不显示空状态横条",
            staleVm.Achievements.Count == 1 && !staleVm.IsAvailable && !staleVm.ShowEmptyState,
            "rows=" + staleVm.Achievements.Count + " " + staleVm.Availability + " show=" + staleVm.ShowEmptyState);
        ChecksContext.Check(
            "空状态遮挡: 有行时不生成空状态标题",
            staleVm.EmptyStateTitle.Length == 0);
        ChecksContext.Check(
            "空状态遮挡: 无行 + SteamUnavailable 仍显示空状态横条",
            poisonVm.Achievements.Count == 0
                && poisonVm.Availability == AchievementAvailability.SteamUnavailable
                && poisonVm.ShowEmptyState,
            "rows=" + poisonVm.Achievements.Count + " " + poisonVm.Availability + " show=" + poisonVm.ShowEmptyState);

        // 4) 惰性 + DDS：加载后 0 次解码；读到某行才解码；二次进页面全命中。
        var lazyRoot = Path.Combine(root, "lazy-game");
        var lazyIcons = Path.Combine(lazyRoot, "game", "gfx", "interface", "icons", "achievements");
        Directory.CreateDirectory(lazyIcons);
        File.WriteAllBytes(Path.Combine(lazyIcons, "ACH_ONE.dds"), Bc1Dds(4, 4));
        File.WriteAllBytes(Path.Combine(lazyIcons, "ACH_LOCKED_notachieved.dds"), Bc1Dds(4, 4));
        File.WriteAllBytes(Path.Combine(lazyIcons, "ACH_JPG.jpg"), [1, 2, 3]);

        var lazyCacheRoot = Path.Combine(root, "icons-lazy");
        var lazyIconsCache = new DdsPngCache(lazyCacheRoot, 96);
        var lazySnapshot = new AchievementSnapshot
        {
            Availability = AchievementAvailability.Available,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Entries =
            [
                Entry("ACH_ONE", unlocked: true),
                Entry("ACH_LOCKED", unlocked: false),
                Entry("ACH_JPG", unlocked: true),
            ],
        };

        var lazyVm = new AchievementsViewModel(
            new CompletedSource(lazySnapshot),
            new AchievementSnapshotCache(fs, Path.Combine(root, "cache-lazy")),
            lazyIconsCache);
        lazyVm.LoadAsync(Game(lazyRoot), CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("DDS 惰性: 进页面不解码任何图标", 0, lazyIconsCache.DecodeCount);
        ChecksContext.Equal("DDS 惰性: 行已建立", 3, lazyVm.Achievements.Count);

        var firstIcon = lazyVm.Achievements[0].IconUrl;
        ChecksContext.Check(
            "DDS 惰性: 读到第一行才解码，返回 PNG 而非 DDS",
            firstIcon.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !firstIcon.Contains(".dds", StringComparison.OrdinalIgnoreCase)
                && File.Exists(firstIcon),
            firstIcon);
        ChecksContext.Equal("DDS 惰性: 第一行解码一次", 1, lazyIconsCache.DecodeCount);
        _ = lazyVm.Achievements[0].HasIcon;
        ChecksContext.Equal("DDS 惰性: 同一行重复读不再解码", 1, lazyIconsCache.DecodeCount);

        var lockedIcon = lazyVm.Achievements[1].IconUrl;
        ChecksContext.Check(
            "DDS 惰性: 未解锁行用 _locked.png 缓存名",
            lockedIcon.EndsWith(Path.Combine(AppId, "ACH_LOCKED_locked.png"), StringComparison.OrdinalIgnoreCase),
            lockedIcon);
        ChecksContext.Equal("DDS 惰性: 第二行再解码一次", 2, lazyIconsCache.DecodeCount);

        var jpgIcon = lazyVm.Achievements[2].IconUrl;
        ChecksContext.Check(
            "DDS 惰性: jpg 图标原样返回、不生成 PNG",
            jpgIcon.EndsWith("ACH_JPG.jpg", StringComparison.Ordinal) && lazyIconsCache.DecodeCount == 2,
            jpgIcon + " decodes=" + lazyIconsCache.DecodeCount);

        var secondRunCache = new DdsPngCache(lazyCacheRoot, 96);
        var secondRunVm = new AchievementsViewModel(
            new CompletedSource(lazySnapshot),
            new AchievementSnapshotCache(fs, Path.Combine(root, "cache-lazy2")),
            secondRunCache);
        secondRunVm.LoadAsync(Game(lazyRoot), CancellationToken.None).GetAwaiter().GetResult();
        var secondIcon = secondRunVm.Achievements[0].IconUrl;
        ChecksContext.Check(
            "DDS 惰性: 二次进页面直接命中已有 PNG",
            File.Exists(secondIcon) && secondRunCache.DecodeCount == 0,
            "decodes=" + secondRunCache.DecodeCount);

        // 5) 损坏 DDS 经 VM 走占位：IconUrl 为空、不抛异常。
        File.WriteAllBytes(Path.Combine(lazyIcons, "ACH_BROKEN.dds"), "not a dds"u8.ToArray());
        var brokenVm = new AchievementsViewModel(
            new CompletedSource(new AchievementSnapshot
            {
                Availability = AchievementAvailability.Available,
                FetchedAtUtc = DateTimeOffset.UtcNow,
                Entries = [Entry("ACH_BROKEN", unlocked: true)],
            }),
            null,
            new DdsPngCache(Path.Combine(root, "icons-broken"), 96));
        brokenVm.LoadAsync(Game(lazyRoot), CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "DDS 惰性: 损坏 DDS 行显示占位（无图标路径）",
            brokenVm.Achievements.Count == 1
                && !brokenVm.Achievements[0].HasIcon
                && string.IsNullOrEmpty(brokenVm.Achievements[0].IconUrl));
    }

    // ------------------------------------------------------------ 工具

    private static AchievementSnapshot AvailableSnapshot() => new()
    {
        Availability = AchievementAvailability.Available,
        FetchedAtUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        Entries = [],
    };

    private static AchievementEntry Entry(string id, bool unlocked) => new(
        id,
        id,
        "说明",
        IsHidden: false,
        IsUnlocked: unlocked,
        UnlockTimeUtc: null,
        GlobalPercent: null,
        IconUrl: null);

    private static bool CachedFileExists(AchievementSnapshotCache cache) =>
        cache.PathFor(AppId) is { } path && File.Exists(path);

    private static string DescribeCache(AchievementSnapshotCache cache) =>
        (cache.PathFor(AppId) is { } path && File.Exists(path)) ? "缓存文件仍存在" : "无缓存文件";

    private static bool IsPng(byte[] bytes) =>
        bytes.Length > 8
        && bytes[0] == 0x89 && bytes[1] == (byte)'P' && bytes[2] == (byte)'N' && bytes[3] == (byte)'G';

    private static (int Width, int Height) ReadPngSize(byte[] png)
    {
        int width = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4));
        int height = BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4));
        return (width, height);
    }

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
        WriteU32(bytes, 80, DdpfFourCc);
        WriteU32(bytes, 84, FourCcDxt1);
        return bytes;
    }

    private static void WriteU32(byte[] buffer, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);

    private static GameInstallation Game(string installRoot) => new(
        GameId: "crusader-kings-3",
        DisplayName: "Crusader Kings III",
        Version: "test",
        InstallRoot: installRoot,
        LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: Path.Combine(installRoot, "game"),
        DlcPath: Path.Combine(installRoot, "dlc"),
        ModDir: Path.Combine(installRoot, "mod"),
        ExePath: Path.Combine(installRoot, "binaries", "ck3.exe"),
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
        SteamAppId: 1158310,
        Support: GameSupport.LaunchOnly);

    private sealed class CompletedSource(AchievementSnapshot snapshot) : IAchievementSource
    {
        public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult(snapshot);
    }

    private sealed class PendingSource(Task<AchievementSnapshot> pending) : IAchievementSource
    {
        public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken) => pending;
    }
}
