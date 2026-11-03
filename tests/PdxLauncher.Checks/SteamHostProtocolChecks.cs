namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「Steam 宿主子进程」契约自检：命令行识别、结果行编解码、宿主在缺依赖时的降级、
/// 以及「只有初始化成功才算被 Steam 登记」这条判定。全部离线跑，不加载 steam_api64.dll。
/// </summary>
public static class SteamHostProtocolChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckVerbRecognition();
        CheckProbeResultRoundTrip();
        CheckWorkshopResultRoundTrip();
        CheckDecodeRejectsNoise();
        CheckRunnerRejectsBadArguments(fs);
        CheckRunnerReportsMissingDll(fs);
        CheckSteamRootDetection(fs);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>断言（参数按「条件在前、标签在后」——本文件通篇用这个顺序）。</summary>
    private static void Assert(bool condition, string label) => ChecksContext.Check(label, condition);

    /// <summary>相等断言（参数按「期望、实际、标签」）。</summary>
    private static void AssertEqual<T>(T expected, T actual, string label) => ChecksContext.Equal(label, expected, actual);

    private static void CheckVerbRecognition()
    {
        var probe = new[] { SteamHostProtocol.VerbArgument, SteamHostProtocol.ProbeVerb, "--api", @"C:\x\steam_api64.dll", "--appid", "529340" };
        Assert(SteamHostProtocol.IsHostInvocation(probe), "带 --steam-host 的命令行被识别为宿主调用");
        Assert(SteamHostProtocol.TryGetVerb(probe, out var verb), "probe 动词可解析");
        AssertEqual(SteamHostProtocol.ProbeVerb, verb, "动词名保持原样");

        var normal = new[] { "--some-launcher-flag" };
        Assert(!SteamHostProtocol.IsHostInvocation(normal), "普通启动不算宿主调用");
        Assert(!SteamHostProtocol.TryGetVerb(normal, out _), "普通启动没有宿主动词");

        var unknown = new[] { SteamHostProtocol.VerbArgument, "pwn", "--api", @"C:\x.dll", "--appid", "1" };
        Assert(!SteamHostProtocol.TryGetVerb(unknown, out _), "未知动词被拒绝");

        var lone = new[] { SteamHostProtocol.VerbArgument };
        Assert(!SteamHostProtocol.IsHostInvocation(lone), "只有 --steam-host 没有动词不算宿主调用");

        // 动词表后来又多了两组：workshop-subscribed（整库订阅列表）+ workshop-batch（批量下载/本地禁用），
        // 以及 workshop-mine / workshop-mine-detail（读「我自己发布的条目」）。
        AssertEqual(9, SteamHostProtocol.Verbs.Count, "动词表恰好九个（probe + 四工坊操作 + 订阅列表 + 批量 + 我的条目两个）");
        AssertEqual(
            SteamHostProtocol.WorkshopPublishVerb,
            SteamHostProtocol.Verbs[4],
            "动词表第五位是 workshop-publish");
        AssertEqual(
            SteamHostProtocol.WorkshopSubscribedVerb,
            SteamHostProtocol.Verbs[5],
            "动词表末二位是 workshop-subscribed");
        AssertEqual(
            SteamHostProtocol.WorkshopBatchVerb,
            SteamHostProtocol.Verbs[6],
            "动词表第七位是 workshop-batch");
        AssertEqual(
            SteamHostProtocol.WorkshopMineVerb,
            SteamHostProtocol.Verbs[7],
            "动词表第八位是 workshop-mine");
        AssertEqual(
            SteamHostProtocol.WorkshopMineDetailVerb,
            SteamHostProtocol.Verbs[8],
            "动词表末位是 workshop-mine-detail");
    }

    private static void CheckProbeResultRoundTrip()
    {
        var original = new SteamHostProbeResult(
            Initialized: true,
            SteamRunning: true,
            SteamId64: 76561198370719182,
            Dlcs:
            [
                new SteamDlcOwnership("1158310", "Victoria 3: Voice of the People", IsOwned: true, IsInstalled: true),
                new SteamDlcOwnership("2157800", "Victoria 3: Dawn of Wonder", IsOwned: false, IsInstalled: false),
            ],
            Error: null,
            Detail: null);

        var line = SteamHostProtocol.Encode(original);
        Assert(line.StartsWith(SteamHostProtocol.ResultPrefix, StringComparison.Ordinal), "结果行带固定前缀");
        Assert(line.IndexOf('\n') < 0, "编码结果单行输出");

        Assert(SteamHostProtocol.TryDecode<SteamHostProbeResult>(line + "\r\n", out var decoded), "带换行的结果行可解码");
        var value = decoded!;
        Assert(value.Initialized, "Initialized 往返一致");
        Assert(value.WasRegistered, "初始化成功即视为被 Steam 登记");
        AssertEqual(76561198370719182UL, value.SteamId64, "SteamID64 往返一致");
        AssertEqual(2, value.Dlcs.Count, "DLC 数量往返一致");
        AssertEqual("1158310", value.Dlcs[0].AppId, "DLC appid 往返一致");
        Assert(value.Dlcs[0].IsOwned, "DLC 拥有位往返一致");
        Assert(!value.Dlcs[1].IsInstalled, "DLC 安装位往返一致");
        Assert(value.Error is null, "成功结果没有错误键");

        var failed = SteamHostProbeResult.Fail(SteamProbeError.NoDll, @"C:\nope\steam_api64.dll");
        Assert(SteamHostProtocol.TryDecode<SteamHostProbeResult>(SteamHostProtocol.Encode(failed), out var decodedFailed), "失败结果可解码");
        AssertEqual(SteamProbeError.NoDll, decodedFailed!.Error, "错误键往返一致");
        Assert(!decodedFailed.WasRegistered, "没初始化成功就不算被 Steam 登记");

        // null 字段默认省略：反序列化器要给回 null 而不是抛异常。
        Assert(line.IndexOf("\"error\"", StringComparison.Ordinal) < 0, "null 字段被省略，不写空值");
    }

    private static void CheckWorkshopResultRoundTrip()
    {
        var state = new WorkshopInstallState(IsSubscribed: true, IsInstalled: false, IsDownloading: true, AcfPath: null);
        var payload = new SteamHostWorkshopResult(Ok: true, State: state, Error: null, Initialized: true);
        Assert(SteamHostProtocol.TryDecode<SteamHostWorkshopResult>(SteamHostProtocol.Encode(payload), out var decoded), "工坊结果可解码");

        var value = decoded!;
        Assert(value.Ok, "Ok 往返一致");
        Assert(value.Initialized, "Initialized 往返一致");
        Assert(value.State is not null, "状态对象往返非空");
        AssertEqual(true, value.State!.IsSubscribed, "订阅位往返一致");
        AssertEqual(false, value.State.IsInstalled, "安装位往返一致");
        Assert(value.State.IsKnown, "读到的状态是「已知」");

        var unknown = new SteamHostWorkshopResult(Ok: true, State: WorkshopInstallState.Unknown, Error: null, Initialized: true);
        Assert(SteamHostProtocol.TryDecode<SteamHostWorkshopResult>(SteamHostProtocol.Encode(unknown), out var decodedUnknown), "未知状态可解码");
        Assert(!decodedUnknown!.State!.IsKnown, "未知状态解码后仍是「未确认」");

        var fail = SteamHostWorkshopResult.Fail("SteamAPI_Init 失败");
        Assert(SteamHostProtocol.TryDecode<SteamHostWorkshopResult>(SteamHostProtocol.Encode(fail), out var decodedFail), "失败结果可解码");
        AssertEqual("SteamAPI_Init 失败", decodedFail!.Error, "失败原因往返一致");
        Assert(decodedFail.State is null, "失败时没有状态对象");

        // 扩展字段（需要更新 / 本地禁用 / 磁盘占用 / 下载进度）必须一起往返：
        // 少一个字段界面就会把「有更新」显示成「已安装」。
        var detailed = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: true,
            AcfPath: "D:\\Steam\\steamapps\\workshop\\appworkshop_1158310.acf",
            NeedsUpdate: true,
            IsDisabledLocally: true,
            IsDownloadPending: false,
            SizeOnDisk: 9876543,
            Folder: "3613769385",
            BytesDownloaded: 123,
            BytesTotal: 456);

        var detailedPayload = new SteamHostWorkshopResult(Ok: true, State: detailed, Error: null, Initialized: true);
        Assert(
            SteamHostProtocol.TryDecode<SteamHostWorkshopResult>(
                SteamHostProtocol.Encode(detailedPayload),
                out var decodedDetailed),
            "扩展状态可解码");

        var ext = decodedDetailed!.State!;
        AssertEqual(true, ext.NeedsUpdate, "需要更新位往返一致");
        AssertEqual(true, ext.IsDisabledLocally, "本地禁用位往返一致");
        AssertEqual(false, ext.IsDownloadPending, "排队位往返一致");
        AssertEqual((ulong?)9876543, ext.SizeOnDisk, "磁盘占用往返一致");
        AssertEqual("3613769385", ext.Folder, "安装目录往返一致");
        AssertEqual((ulong?)123, ext.BytesDownloaded, "已下载字节往返一致");
        AssertEqual((ulong?)456, ext.BytesTotal, "总字节往返一致");
        AssertEqual("D:\\Steam\\steamapps\\workshop\\appworkshop_1158310.acf", ext.AcfPath, "acf 路径往返一致");
    }

    private static void CheckDecodeRejectsNoise()
    {
        Assert(!SteamHostProtocol.TryDecode<SteamHostProbeResult>(string.Empty, out _), "空输出不能解码");
        Assert(!SteamHostProtocol.TryDecode<SteamHostProbeResult>(null, out _), "null 输出不能解码");
        Assert(!SteamHostProtocol.TryDecode<SteamHostProbeResult>("随便一行日志\n还有一行", out _), "没有前缀的输出不能解码");
        Assert(
            !SteamHostProtocol.TryDecode<SteamHostProbeResult>(SteamHostProtocol.ResultPrefix + "{ not json", out _),
            "前缀后面不是 JSON 也不能解码");

        // 真实场景：宿主可能先打一行无关输出，再给结果行；解码器要能从中间挑出来。
        var good = SteamHostProtocol.Encode(SteamHostProbeResult.Fail(SteamProbeError.HostFailed, "x"));
        Assert(
            SteamHostProtocol.TryDecode<SteamHostProbeResult>("warning: 无关输出\r\n" + good + "\r\n", out var decoded),
            "结果行混在其它输出里也能解码");
        AssertEqual(SteamProbeError.HostFailed, decoded!.Error, "混在输出里时错误键仍正确");
    }

    private static void CheckRunnerRejectsBadArguments(IFileSystem fs)
    {
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var code = SteamHostRunner.Run([SteamHostProtocol.VerbArgument, SteamHostProtocol.ProbeVerb], stdout, stderr);
        AssertEqual(SteamHostProtocol.ExitFailure, code, "缺少 --api 时宿主以失败码退出");
        Assert(stderr.ToString().Contains("--api", StringComparison.Ordinal), "缺少 --api 时提示用法");
        Assert(stdout.ToString().Length == 0, "参数错误时不写结果行");

        var badAppId = new StringWriter(CultureInfo.InvariantCulture);
        var badStderr = new StringWriter(CultureInfo.InvariantCulture);
        code = SteamHostRunner.Run(
            [SteamHostProtocol.VerbArgument, SteamHostProtocol.ProbeVerb, "--api", @"C:\x\steam_api64.dll", "--appid", "abc"],
            badAppId,
            badStderr);
        AssertEqual(SteamHostProtocol.ExitFailure, code, "appid 不是数字时宿主以失败码退出");
        Assert(badStderr.ToString().Contains("--appid", StringComparison.Ordinal), "非法 appid 时提示用法");

        var workshopNoFile = new StringWriter(CultureInfo.InvariantCulture);
        var workshopErr = new StringWriter(CultureInfo.InvariantCulture);
        code = SteamHostRunner.Run(
            [SteamHostProtocol.VerbArgument, SteamHostProtocol.WorkshopStateVerb, "--api", @"C:\x\steam_api64.dll", "--appid", "529340"],
            workshopNoFile,
            workshopErr);
        AssertEqual(SteamHostProtocol.ExitFailure, code, "工坊动词缺 --fileid 时宿主以失败码退出");
        Assert(workshopErr.ToString().Contains("--fileid", StringComparison.Ordinal), "缺 --fileid 时提示用法");

        // 未知动词：TryGetVerb 已挡住，这里确认宿主不会执行任何操作。
        var unknownStdout = new StringWriter(CultureInfo.InvariantCulture);
        code = SteamHostRunner.Run([SteamHostProtocol.VerbArgument, "nope"], unknownStdout, new StringWriter(CultureInfo.InvariantCulture));
        AssertEqual(SteamHostProtocol.ExitFailure, code, "未知动词以失败码退出");
        Assert(fs is not null, "文件系统参数被接受");
    }

    private static void CheckRunnerReportsMissingDll(IFileSystem fs)
    {
        var missing = Path.Combine(Path.GetTempPath(), "pdx-host-checks-" + Guid.NewGuid().ToString("N"), "steam_api64.dll");
        var stdout = new StringWriter(CultureInfo.InvariantCulture);
        var stderr = new StringWriter(CultureInfo.InvariantCulture);

        var code = SteamHostRunner.Run(
            [SteamHostProtocol.VerbArgument, SteamHostProtocol.ProbeVerb, "--api", missing, "--appid", "529340"],
            stdout,
            stderr);

        AssertEqual(SteamHostProtocol.ExitOk, code, "DLL 不存在仍属于「已执行的探测」，退出码为 0");
        Assert(SteamHostProtocol.TryDecode<SteamHostProbeResult>(stdout.ToString(), out var decoded), "缺失 DLL 时仍写回可解码的结果行");
        AssertEqual(SteamProbeError.NoDll, decoded!.Error, "缺失 DLL 的错误键是 noDll");
        Assert(!decoded.WasRegistered, "没有 DLL 时不可能被 Steam 登记");
        Assert(decoded.Detail == missing, "失败细节带上尝试的 DLL 路径");
        Assert(fs is not null, "文件系统参数被接受");
    }

    private static void CheckSteamRootDetection(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdx-steam-root-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(Path.Combine(root, "steamapps"));
            Assert(!SteamClientLocator.LooksLikeSteamRoot(root), "没有 libraryfolders.vdf 的目录不算 Steam 安装目录");

            var vdf = Path.Combine(root, "steamapps", SteamClientLocator.LibraryFoldersFileName);
            File.WriteAllText(vdf, "\"libraryfolders\"\n{\n}\n", new System.Text.UTF8Encoding(false));
            Assert(SteamClientLocator.LooksLikeSteamRoot(root), "含 steamapps\\libraryfolders.vdf 的目录才算 Steam 安装目录");
            Assert(!SteamClientLocator.LooksLikeSteamRoot(null), "null 不算 Steam 安装目录");
            Assert(!SteamClientLocator.LooksLikeSteamRoot(string.Empty), "空路径不算 Steam 安装目录");

            // Steam 安装目录的候选列表只包含校验通过的目录，不会把不存在的路径塞进去。
            var candidates = SteamClientLocator.Candidates();
            foreach (var candidate in candidates)
            {
                Assert(SteamClientLocator.LooksLikeSteamRoot(candidate), "候选目录都通过校验：" + candidate);
            }

            // JSON 往返也顺带验证一次「驼峰命名」：字段名不对，跨进程读回来就是默认值。
            var json = JsonSerializer.Serialize(SteamHostProbeResult.Fail(SteamProbeError.Busy), SteamHostProtocol.JsonOptions);
            Assert(json.Contains("\"steamRunning\"", StringComparison.Ordinal), "结果 JSON 用驼峰命名");
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
    }
}
