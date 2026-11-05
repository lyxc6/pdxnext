namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// 成就接入口自检。这里钉的是三件最容易悄悄坏掉的事：
/// <list type="number">
/// <item>结果码混淆：把「Steam 没运行 / 超时」当成「没有成就」，页面就会给出错误的结论。</item>
/// <item>宿主动词分发：<c>--steam-host achievements</c> 必须走成就分支，且 DLL 缺失时给出 noDll 而不是崩。</item>
/// <item>请求代次：切游戏时旧请求的 finally 不能关掉新请求的忙碌态。</item>
/// </list>
/// 全部用假数据 / 不存在的 dll 路径，不启动 Steam、不加载原生库。
/// </summary>
public static class SteamAchievementsChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckVerb();
        CheckResultCodes();
        CheckProtocolRoundTrip();
        CheckMapping();
        CheckRunnerDispatch();
        CheckRequestTracker();
        CheckInitFailureCode();
        CheckCancellation(fs);
        CheckNativeLayout();

        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------ 动词

    private static void CheckVerb()
    {
        ChecksContext.Equal("成就: 动词名", "achievements", SteamHostProtocol.AchievementsVerb);
        ChecksContext.Check(
            "成就: 动词已登记（宿主白名单）",
            SteamHostProtocol.Verbs.Contains(SteamHostProtocol.AchievementsVerb, StringComparer.Ordinal));

        ChecksContext.Check(
            "成就: 识别宿主调用",
            SteamHostProtocol.IsHostInvocation(new[] { "--steam-host", "achievements", "--appid", "1158310" }));

        var ok = SteamHostProtocol.TryGetVerb(
            new[] { "--steam-host", "achievements", "--appid", "1158310" },
            out var verb);
        ChecksContext.Check("成就: TryGetVerb 命中", ok && verb == SteamHostProtocol.AchievementsVerb);

        ChecksContext.Check(
            "成就: 未知动词被拒绝",
            !SteamHostProtocol.TryGetVerb(new[] { "--steam-host", "achievementss" }, out _));
    }

    // ------------------------------------------------------------ 结果码

    private static void CheckResultCodes()
    {
        string[] codes =
        [
            SteamAchievementCodes.Ok,
            SteamAchievementCodes.NoDll,
            SteamAchievementCodes.Native,
            SteamAchievementCodes.SteamNotRunning,
            SteamAchievementCodes.InitFailed,
            SteamAchievementCodes.NoSteamUser,
            SteamAchievementCodes.Unsupported,
            SteamAchievementCodes.RequestFailed,
            SteamAchievementCodes.Timeout,
            SteamAchievementCodes.NoAchievements,
            SteamAchievementCodes.HostFailed,
        ];

        ChecksContext.Equal("成就: 结果码数量", 11, codes.Distinct(StringComparer.Ordinal).Count());
        ChecksContext.Check("成就: 结果码都非空", codes.All(static code => !string.IsNullOrWhiteSpace(code)));

        ChecksContext.Check("成就: ok 不算不可用", !SteamAchievementCodes.IsUnavailable(SteamAchievementCodes.Ok));
        ChecksContext.Check(
            "成就: noAchievements 不算「不可用」（是明确的空）",
            !SteamAchievementCodes.IsUnavailable(SteamAchievementCodes.NoAchievements));
        ChecksContext.Check(
            "成就: steamNotRunning 算不可用",
            SteamAchievementCodes.IsUnavailable(SteamAchievementCodes.SteamNotRunning));
        ChecksContext.Check(
            "成就: timeout 算不可用",
            SteamAchievementCodes.IsUnavailable(SteamAchievementCodes.Timeout));
        ChecksContext.Check("成就: null 不算不可用", !SteamAchievementCodes.IsUnavailable(null));
    }

    // ------------------------------------------------------------ 协议 / JSON

    private static void CheckProtocolRoundTrip()
    {
        var payload = SteamHostAchievementsResult.Success(
        [
            new SteamHostAchievementItem
            {
                Name = "ACH_WIN_ONE_GAME",
                DisplayName = "首胜",
                Description = "赢一局",
                Hidden = true,
                Achieved = true,
                UnlockTime = 1_700_000_000,
                GlobalPercent = 12.5,
            },
        ]);

        var encoded = SteamHostProtocol.Encode(payload);
        ChecksContext.Check(
            "成就: 编码带结果前缀",
            encoded.StartsWith(SteamHostProtocol.ResultPrefix, StringComparison.Ordinal));
        ChecksContext.Check("成就: JSON 用 name 表示 API 名", encoded.Contains("\"name\":\"ACH_WIN_ONE_GAME\"", StringComparison.Ordinal));
        ChecksContext.Check("成就: JSON 用 displayName 表示显示名", encoded.Contains("\"displayName\":", StringComparison.Ordinal));
        ChecksContext.Check("成就: JSON 含 unlockTime", encoded.Contains("\"unlockTime\":1700000000", StringComparison.Ordinal));
        ChecksContext.Check("成就: JSON 含 globalPercent", encoded.Contains("\"globalPercent\":12.5", StringComparison.Ordinal));

        ChecksContext.Check("成就: 解码成功", SteamHostProtocol.TryDecode<SteamHostAchievementsResult>(encoded, out var decoded));
        ChecksContext.Check("成就: 解码 ok", decoded is { Ok: true, Code: SteamAchievementCodes.Ok });
        ChecksContext.Equal("成就: 解码条数", 1, decoded?.Items.Count ?? -1);
        ChecksContext.Equal("成就: 解码 API 名", "ACH_WIN_ONE_GAME", decoded?.Items[0].Name ?? "");
        ChecksContext.Equal("成就: 解码显示名", "首胜", decoded?.Items[0].DisplayName ?? "");
        ChecksContext.Equal("成就: 解码解锁时间", 1_700_000_000u, decoded?.Items[0].UnlockTime ?? 0u);
        ChecksContext.Equal("成就: 解码隐藏标记", true, decoded?.Items[0].Hidden ?? false);

        // 前缀之前混入别的输出（宿主日志）也要能解出结果行。
        ChecksContext.Check(
            "成就: 前缀前的噪声不干扰解码",
            SteamHostProtocol.TryDecode<SteamHostAchievementsResult>("noise line\n" + encoded + "\nmore", out _));

        // 缺字段：老宿主少给字段时整条结果仍应可解，默认值补齐。
        var partial = SteamHostProtocol.ResultPrefix + "{\"ok\":true,\"items\":[{\"name\":\"ACH_A\"}]}";
        ChecksContext.Check("成就: 缺字段仍可解码", SteamHostProtocol.TryDecode<SteamHostAchievementsResult>(partial, out var partialResult));
        ChecksContext.Equal("成就: 缺显示名时为空串", "", partialResult?.Items[0].DisplayName ?? "null");
        ChecksContext.Equal("成就: 缺隐藏标记时为 false", false, partialResult?.Items[0].Hidden ?? true);
        ChecksContext.Equal("成就: 缺百分比时为 null", null, partialResult?.Items[0].GlobalPercent);

        // 坏 JSON / 非 JSON 一律解码失败，不抛异常。
        ChecksContext.Check(
            "成就: 坏 JSON 解码失败",
            !SteamHostProtocol.TryDecode<SteamHostAchievementsResult>(SteamHostProtocol.ResultPrefix + "{not json", out _));
        ChecksContext.Check(
            "成就: 没有结果行时解码失败",
            !SteamHostProtocol.TryDecode<SteamHostAchievementsResult>("只打印了日志\n", out _));
        ChecksContext.Check("成就: null 输入解码失败", !SteamHostProtocol.TryDecode<SteamHostAchievementsResult>(null, out _));
    }

    // ------------------------------------------------------------ 映射

    private static void CheckMapping()
    {
        var fetchedAt = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);

        var nullResult = SteamAchievementMapping.FromHostResult(null, fetchedAt);
        ChecksContext.Equal("映射: 解码失败 → SteamUnavailable", AchievementAvailability.SteamUnavailable, nullResult.Availability);
        ChecksContext.Equal(
            "映射: 解码失败带 steamUnavailable 文案键",
            SteamAchievementMapping.SteamUnavailableMessageKey,
            nullResult.Message);
        ChecksContext.Equal("映射: 解码失败时保留取数时间", fetchedAt, nullResult.FetchedAtUtc);

        var timeout = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.Fail(SteamAchievementCodes.Timeout, "等 UserStatsReceived 超时"),
            fetchedAt);
        ChecksContext.Equal("映射: 超时 → SteamUnavailable（不是 Empty）", AchievementAvailability.SteamUnavailable, timeout.Availability);
        ChecksContext.Check(
            "映射: 超时带着具体原因",
            timeout.Message == SteamAchievementMapping.SteamUnavailableMessageKey,
            timeout.Message ?? "null");

        var notRunning = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.Fail(SteamAchievementCodes.SteamNotRunning, "Steam 未运行"),
            fetchedAt);
        ChecksContext.Equal("映射: Steam 未运行 → SteamUnavailable", AchievementAvailability.SteamUnavailable, notRunning.Availability);

        var none = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.None(initialized: true),
            fetchedAt);
        ChecksContext.Equal("映射: noAchievements → Empty", AchievementAvailability.Empty, none.Availability);
        ChecksContext.Equal("映射: Empty 的文案键", SteamAchievementMapping.EmptyMessageKey, none.Message);

        var emptyItems = SteamAchievementMapping.FromHostResult(SteamHostAchievementsResult.Success([]), fetchedAt);
        ChecksContext.Equal("映射: ok + 空数组 → Empty", AchievementAvailability.Empty, emptyItems.Availability);

        var blankName = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.Success([new SteamHostAchievementItem { Name = "  " }]),
            fetchedAt);
        ChecksContext.Equal("映射: 只有空 id 的条目被过滤后 → Empty", AchievementAvailability.Empty, blankName.Availability);

        var available = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.Success(
            [
                new SteamHostAchievementItem
                {
                    Name = "ACH_WIN",
                    DisplayName = "首胜",
                    Description = "赢一局",
                    Hidden = false,
                    Achieved = true,
                    UnlockTime = 1_700_000_000,
                    GlobalPercent = 123.4,
                },
                new SteamHostAchievementItem
                {
                    Name = "ACH_SECRET",
                    Achieved = false,
                    Hidden = true,
                    UnlockTime = 1_700_000_000,
                },
            ]),
            fetchedAt);

        ChecksContext.Equal("映射: 有数据 → Available", AchievementAvailability.Available, available.Availability);
        ChecksContext.Equal("映射: 条目数", 2, available.Entries.Count);
        ChecksContext.Equal(
            "映射: 缺显示名时用占位符，绝不把 API 名当名字",
            SteamAchievementMapping.UnknownName,
            available.Entries[1].Name);
        ChecksContext.Equal("映射: 已解锁", true, available.Entries[0].IsUnlocked);
        ChecksContext.Equal(
            "映射: 解锁时间转 UTC",
            DateTimeOffset.FromUnixTimeSeconds(1_700_000_000),
            available.Entries[0].UnlockTimeUtc);
        ChecksContext.Equal("映射: 未解锁不给解锁时间", null, available.Entries[1].UnlockTimeUtc);
        ChecksContext.Equal("映射: 百分比上限收敛到 100", 100d, available.Entries[0].GlobalPercent);
        ChecksContext.Equal("映射: 读不到百分比时为 null", null, available.Entries[1].GlobalPercent);
        ChecksContext.Equal("映射: 隐藏成就保留标记", true, available.Entries[1].IsHidden);
        ChecksContext.Equal("映射: 图标暂不接入（留 null）", null, available.Entries[0].IconUrl);
        ChecksContext.Equal("映射: 保留取数时间", fetchedAt, available.FetchedAtUtc);

        // 空 id 的脏条目被过滤，其余条目照常保留。
        var dirty = SteamAchievementMapping.MapEntries(
        [
            new SteamHostAchievementItem(),
            new SteamHostAchievementItem { Name = "ACH_OK" },
        ]);
        ChecksContext.Equal("映射: 脏条目被过滤", 1, dirty.Count);
        ChecksContext.Equal("映射: 其余条目保留", "ACH_OK", dirty[0].Id);

        var notSupported = SteamAchievementMapping.NotSupported(fetchedAt);
        ChecksContext.Equal("映射: NotSupported 文案键复用契约常量", NullAchievementSource.NotSupportedMessageKey, notSupported.Message);
    }

    // ------------------------------------------------------------ 宿主分发（假路径）

    private static void CheckRunnerDispatch()
    {
        var missingApi = Path.Combine(Path.GetTempPath(), "pdxnext-no-such-steam_api64_" + Guid.NewGuid().ToString("N") + ".dll");
        ChecksContext.Check("宿主动词: 假 dll 路径不存在（前提）", !File.Exists(missingApi));

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var exit = SteamHostRunner.Run(
            ["--steam-host", "achievements", "--api", missingApi, "--appid", "1158310"],
            stdout,
            stderr);

        ChecksContext.Equal("宿主动词: 结果缺失 dll 仍以 0 退出（结果码在 JSON 里）", 0, exit);
        ChecksContext.Check(
            "宿主动词: stdout 只有一条结果行",
            SteamHostProtocol.TryDecode<SteamHostAchievementsResult>(stdout.ToString(), out var result),
            stdout.ToString().Trim());
        ChecksContext.Equal("宿主动词: 结果码 noDll", SteamAchievementCodes.NoDll, result?.Code ?? "");
        ChecksContext.Equal("宿主动词: 没有初始化", false, result?.Initialized ?? true);
        ChecksContext.Check(
            "宿主动词: noDll 的说明指向具体路径",
            result?.Error is { Length: > 0 } error && error.Contains(missingApi, StringComparison.Ordinal),
            result?.Error ?? "null");
        ChecksContext.Check("宿主动词: 不往 stderr 写东西", string.IsNullOrWhiteSpace(stderr.ToString()));

        // 缺 --appid：参数错误走 ExitFailure，且不产出结果行。
        var badOut = new StringWriter();
        var badErr = new StringWriter();
        var badExit = SteamHostRunner.Run(["--steam-host", "achievements", "--api", missingApi], badOut, badErr);
        ChecksContext.Equal("宿主动词: 缺 --appid 退出码为失败", 1, badExit);
        ChecksContext.Check("宿主动词: 缺 --appid 不产出结果行", string.IsNullOrWhiteSpace(badOut.ToString()));
        ChecksContext.Check("宿主动词: 缺 --appid 在 stderr 说明原因", !string.IsNullOrWhiteSpace(badErr.ToString()));

        // 未知动词同样拒绝，避免把成就参数派发到别的原生操作。
        var unknownOut = new StringWriter();
        var unknownErr = new StringWriter();
        var unknownExit = SteamHostRunner.Run(
            ["--steam-host", "achievementss", "--api", missingApi, "--appid", "1158310"],
            unknownOut,
            unknownErr);
        ChecksContext.Equal("宿主动词: 未知动词退出码为失败", 1, unknownExit);
        ChecksContext.Check("宿主动词: 未知动词在 stderr 说明原因", !string.IsNullOrWhiteSpace(unknownErr.ToString()));
    }

    // ------------------------------------------------------------ 请求代次

    private static void CheckRequestTracker()
    {
        var tracker = new AchievementRequestTracker();
        var first = tracker.Begin();
        var second = tracker.Begin();

        ChecksContext.Check("代次: 新请求取代旧请求", !tracker.IsCurrent(first));
        ChecksContext.Check("代次: 最新请求有效", tracker.IsCurrent(second));

        var current = second;
        for (var i = 0; i < 1000; i++)
        {
            current = tracker.Begin();
        }

        ChecksContext.Check("代次: 连点 1000 次后只有最后一代有效", tracker.IsCurrent(current));
        ChecksContext.Check("代次: 连点后更早的代次全部失效", !tracker.IsCurrent(second));

        // 模拟真实交错：旧请求在新请求之后走 finally —— 它不能把新请求的忙碌态关掉。
        var oldRequest = tracker.Begin();
        var newRequest = tracker.Begin();
        var busy = true;
        if (tracker.IsCurrent(oldRequest))
        {
            busy = false;
        }

        ChecksContext.Check("代次: 旧请求 finally 不动新请求的 IsBusy", busy && tracker.IsCurrent(newRequest));

        // 并发推进也不能出现两个「当前代次」。
        var concurrent = new AchievementRequestTracker();
        var generations = new int[64];
        Parallel.For(0, generations.Length, i => generations[i] = concurrent.Begin());
        var newest = generations.Max();
        ChecksContext.Equal("代次: 并发推进后当前代次唯一", 1, generations.Count(g => concurrent.IsCurrent(g)));
        ChecksContext.Check("代次: 当前代次是最大值", concurrent.IsCurrent(newest));
    }

    // ------------------------------------------------------------ Init 失败的结果码

    private static void CheckInitFailureCode()
    {
        // 真机复现过：Steam 在跑 + appid 999999，Init 失败，但 IsSteamRunning 在受限会话里也返回 false。
        // 只信 IsSteamRunning 会报成 steamNotRunning；补上进程表旁证后必须报 initFailed。
        ChecksContext.Equal(
            "Init 失败: Steam 在跑（IsSteamRunning=true）→ initFailed",
            SteamAchievementCodes.InitFailed,
            SteamAchievementsHostOperation.ClassifyInitFailure(isSteamRunning: true, steamClientProcessFound: false));

        ChecksContext.Equal(
            "Init 失败: IsSteamRunning=false 但进程表有 Steam（appid 无效场景）→ initFailed",
            SteamAchievementCodes.InitFailed,
            SteamAchievementsHostOperation.ClassifyInitFailure(isSteamRunning: false, steamClientProcessFound: true));

        ChecksContext.Equal(
            "Init 失败: 确实没跑（无进程、判据为 false）→ steamNotRunning",
            SteamAchievementCodes.SteamNotRunning,
            SteamAchievementsHostOperation.ClassifyInitFailure(isSteamRunning: false, steamClientProcessFound: false));

        ChecksContext.Equal(
            "Init 失败: 判不出来（导出缺失）→ initFailed，不武断说没运行",
            SteamAchievementCodes.InitFailed,
            SteamAchievementsHostOperation.ClassifyInitFailure(isSteamRunning: null, steamClientProcessFound: false));
    }

    // ------------------------------------------------------------ 取消传播

    private static void CheckCancellation(IFileSystem fs)
    {
        var hostPath = FindFakeHostPath();
        ChecksContext.Check("取消: 找到假宿主可执行文件", hostPath is not null, "构建输出里应有 PdxLauncher.Checks.exe");
        if (hostPath is null)
        {
            return;
        }

        var logger = new SilentLogger();

        // 宿主客户端层：调用方取消必须抛 OperationCanceledException，不能变成一个「失败结果」。
        var client = new SteamHostClient(logger, hostPath);
        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(300));
            Exception? caught = null;
            try
            {
                client.AchievementsAsync("pdxnext-no-such.dll", 1158310, cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            ChecksContext.Check(
                "取消: 客户端把调用方取消原样抛出（不是 hostFailed）",
                caught is OperationCanceledException,
                caught is null ? "没有抛出" : caught.GetType().Name);
        }

        // 数据源层：取消不能被 catch 吞成 SteamUnavailable。
        var source = new SteamAchievementSource(
            new SteamHostClient(logger, hostPath),
            new SteamApiLocator(fs, logger),
            fs,
            logger,
            () => Path.Combine(AppContext.BaseDirectory, "PdxLauncher.Checks.dll"),
            () => null);
        using (var cts = new CancellationTokenSource())
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(300));
            Exception? caught = null;
            AchievementSnapshot? snapshot = null;
            try
            {
                snapshot = source.GetAsync("ck3", cts.Token).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }

            ChecksContext.Check(
                "取消: 数据源把取消原样抛出而不是 SteamUnavailable",
                caught is OperationCanceledException,
                caught is null ? "返回了 " + snapshot?.Availability : caught.GetType().Name);
        }
    }

    private static string? FindFakeHostPath()
    {
        var own = Path.Combine(AppContext.BaseDirectory, "PdxLauncher.Checks.exe");
        if (File.Exists(own))
        {
            return own;
        }

        var process = Environment.ProcessPath;
        return process is not null
            && File.Exists(process)
            && !process.EndsWith("dotnet.exe", StringComparison.OrdinalIgnoreCase)
                ? process
                : null;
    }

    // ------------------------------------------------------------ 原生布局

    private static void CheckNativeLayout()
    {
        // UserStatsReceived_t（x64）：uint64 gameId + int result + uint64 steamIdUser = 24 字节。
        // 布局错了会把 EResult 读成垃圾，进而把「成功」误判成失败。
        ChecksContext.Equal("原生: UserStatsReceived_t x64 布局为 24 字节", 24, SteamAchievementsHostOperation.UserStatsReceivedSize);
        ChecksContext.Equal(
            "原生: 成就上限定为 4096（防止脏 count 撑爆内存）",
            4096,
            SteamAchievementsHostOperation.MaxAchievements);
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
