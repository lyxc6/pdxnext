namespace PdxLauncher.Checks;

using System.IO.Compression;
using System.Security.Cryptography;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地翻译模型下载器的自检：逐字节进度、节流、断点续传、服务端忽略 Range 的退化、取消保留残片、
/// 校验失败删残片、残片占用与删除。
/// <para>
/// 起因：Index-Translate-2B 是单个 1.31 GB 文件，旧实现只在「每个文件下完」时上报一次进度，
/// 界面上整段下载停在 0%、结束时直接跳 100%。这类缺陷不会崩、只在真机上表现为「下载卡死」，
/// 所以必须由自检钉住。用例全部落在临时目录与假 fetcher 上，绝不碰真实的
/// <c>%LOCALAPPDATA%\PdxLauncher\models</c>（那里有 1.3 GB 的权重）。
/// </para>
/// </summary>
public static class LocalTranslationModelDownloadChecks
{
    private const string FileName = "model-test.bin";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "pdxnext-model-download-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckProgressModel();
            CheckSingleFileProgress(tempRoot);
            CheckResume(tempRoot);
            CheckRangeIgnoredByServer(tempRoot);
            CheckCancelKeepsPartial(tempRoot);
            CheckHashMismatchDropsPartial(tempRoot);
            CheckAlreadyCompleteIsSkipped(tempRoot);
            CheckCompletePartialIsFinalized(tempRoot);
            CheckLongerPartialIsDropped(tempRoot);
            CheckUnknownTotalStillReportsBytes(tempRoot);
            CheckCancelDuringVerifyKeepsPartial(tempRoot);
            CheckCompressedResume(tempRoot);
            CheckFallbackWithoutRangeSupport(tempRoot);
            CheckStagedBytesAndRemove(tempRoot);
        }
        finally
        {
            TryDeleteDirectory(tempRoot);
        }

        return ChecksContext.Failures - before;
    }

    /// <summary>进度模型的边界：0 总长不除零、超过总长被夹住。</summary>
    private static void CheckProgressModel()
    {
        var unknown = new LocalTranslationDownloadProgress(0, 0);
        ChecksContext.Equal("下载器: 总长未知时比例为 0", 0d, unknown.Fraction);
        ChecksContext.Equal("下载器: 总长未知时百分比为 0", 0, unknown.Percent);

        var half = new LocalTranslationDownloadProgress(5, 10);
        ChecksContext.Equal("下载器: 一半 → 50%", 50, half.Percent);

        var overflow = new LocalTranslationDownloadProgress(15, 10);
        ChecksContext.Equal("下载器: 超过总长夹到 100%", 100, overflow.Percent);
        ChecksContext.Equal("下载器: 超过总长夹到 1.0", 1d, overflow.Fraction);
    }

    /// <summary>
    /// 核心回归：单个大文件下载过程中必须出现「中间进度」。
    /// 旧实现在这里只有一次 0% 和一次 100%，正是用户看到「下载进度不动」的原因。
    /// </summary>
    private static void CheckSingleFileProgress(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "single");
        var payload = Fill(256 * 1024, 0x5a);
        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload) { ChunkSize = 16 * 1024 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var reports = new List<LocalTranslationDownloadProgress>();
        var result = downloader
            .DownloadAsync(
                LocalTranslationModelKind.IndexTranslate,
                new SyncProgress(reports.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 单文件下载成功", result.Success, result.Message);
        ChecksContext.EqualBytes(
            "下载器: 落盘内容与源一致",
            payload,
            File.ReadAllBytes(TargetPath(root)));
        ChecksContext.Check(
            "下载器: 中途至少上报 5 次",
            reports.Count >= 5,
            "实际 " + reports.Count);
        ChecksContext.Check(
            "下载器: 出现 0 < 进度 < 100 的中间值（旧实现只有 0% 与 100%）",
            reports.Any(item => item.BytesReceived > 0 && item.BytesReceived < item.TotalBytes),
            "上报 " + reports.Count + " 次");
        ChecksContext.Check(
            "下载器: 进度单调不减",
            IsMonotonic(reports));
        ChecksContext.Equal(
            "下载器: 末次上报 100%",
            100,
            reports[^1].Percent);
        ChecksContext.Equal(
            "下载器: DownloadedBytes 等于实际传输量",
            payload.Length,
            result.DownloadedBytes);
        ChecksContext.Check(
            "下载器: 上报次数被 0.5% 步长节流（≤ 208 次）",
            reports.Count <= 208,
            "实际 " + reports.Count);
    }

    /// <summary>续传：预置 <c>.part</c> 后必须从断点起请求，并且只把剩余部分算进 DownloadedBytes。</summary>
    private static void CheckResume(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "resume");
        var payload = Fill(64 * 1024, 0x61);
        const int staged = 30 * 1024;
        WriteStaged(root, payload, staged);

        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload) { ChunkSize = 8 * 1024 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var reports = new List<LocalTranslationDownloadProgress>();
        var result = downloader
            .DownloadAsync(
                LocalTranslationModelKind.IndexTranslate,
                new SyncProgress(reports.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 续传成功", result.Success, result.Message);
        ChecksContext.Equal("下载器: 续传请求带上 Range 起点", (long?)staged, fetcher.LastStartOffset);
        ChecksContext.EqualBytes(
            "下载器: 续传后文件内容与源一致",
            payload,
            File.ReadAllBytes(TargetPath(root)));
        ChecksContext.Equal(
            "下载器: DownloadedBytes 只算这次传输的剩余部分",
            (long)(payload.Length - staged),
            result.DownloadedBytes);
        ChecksContext.Check(
            "下载器: 续传完成后残片被清掉",
            !File.Exists(PartPath(root)));
        ChecksContext.Check(
            "下载器: 续传的首次进度就跳过已下载的那段",
            reports.Count > 0 && reports[0].BytesReceived >= staged,
            reports.Count == 0 ? "(无上报)" : reports[0].BytesReceived.ToString());
    }

    /// <summary>服务端忽略 Range（回 200 全量）时，必须截断残片重写，而不是把新内容拼在旧内容后面。</summary>
    private static void CheckRangeIgnoredByServer(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "no-range");
        var payload = Fill(48 * 1024, 0x62);
        const int staged = 20 * 1024;
        WriteStaged(root, payload, staged);

        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload)
        {
            ChunkSize = 8 * 1024,
            SupportsRange = false,
        };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var result = downloader
            .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 服务端忽略 Range 时仍成功", result.Success, result.Message);
        ChecksContext.Equal("下载器: 仍然提出了续传请求", (long?)staged, fetcher.LastStartOffset);
        ChecksContext.EqualBytes(
            "下载器: 忽略 Range 后文件内容没有被拼接污染",
            payload,
            File.ReadAllBytes(TargetPath(root)));
        ChecksContext.Equal(
            "下载器: 全量重下时 DownloadedBytes 是整份长度",
            (long)payload.Length,
            result.DownloadedBytes);
    }

    /// <summary>取消（暂停）：抛 OperationCanceledException，但已下载的残片必须留在盘上供续传。</summary>
    private static void CheckCancelKeepsPartial(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "cancel");
        var payload = Fill(512 * 1024, 0x63);
        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload)
        {
            ChunkSize = 32 * 1024,
            StallAfterChunks = 1,
        };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        using var cancellation = new CancellationTokenSource();
        var reports = 0;
        var cancelled = false;
        try
        {
            downloader
                .DownloadAsync(
                    LocalTranslationModelKind.IndexTranslate,
                    new SyncProgress(_ =>
                    {
                        reports++;
                        if (reports == 1)
                        {
                            cancellation.Cancel();
                        }
                    }),
                    cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        ChecksContext.Check("下载器: 取消会抛 OperationCanceledException", cancelled);
        var part = PartPath(root);
        ChecksContext.Check("下载器: 取消后残片保留", File.Exists(part));
        ChecksContext.Equal(
            "下载器: 残片里是第一块数据（32 KiB）",
            (long)(32 * 1024),
            File.Exists(part) ? new FileInfo(part).Length : 0);
        ChecksContext.Check("下载器: 取消后目标文件不存在", !File.Exists(TargetPath(root)));
    }

    /// <summary>哈希不对：整份重来，残片必须删掉（继续续传只会续出一个坏文件）。</summary>
    private static void CheckHashMismatchDropsPartial(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "hash");
        var payload = Fill(16 * 1024, 0x64);
        var wrong = new string('0', 64);
        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length, sha: wrong);
        var fetcher = new FakeDownloadFetcher(payload) { ChunkSize = 4 * 1024 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var result = downloader
            .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 哈希不符时返回失败", !result.Success);
        ChecksContext.Check(
            "下载器: 失败文案里带文件名",
            result.Message.Contains(FileName, StringComparison.Ordinal),
            result.Message);
        ChecksContext.Check("下载器: 哈希不符后删掉残片", !File.Exists(PartPath(root)));
        ChecksContext.Check("下载器: 哈希不符后不落目标文件", !File.Exists(TargetPath(root)));
    }

    /// <summary>已经装好的文件直接跳过：不发请求，顺手清掉上一轮的残片。</summary>
    private static void CheckAlreadyCompleteIsSkipped(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "complete");
        var payload = Fill(8 * 1024, 0x65);
        WriteTarget(root, payload);
        WriteStaged(root, payload, 4 * 1024);

        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload);
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var reports = new List<LocalTranslationDownloadProgress>();
        var result = downloader
            .DownloadAsync(
                LocalTranslationModelKind.IndexTranslate,
                new SyncProgress(reports.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 已装好时直接成功", result.Success, result.Message);
        ChecksContext.Equal("下载器: 已装好时不发任何请求", 0, fetcher.RequestCount);
        ChecksContext.Equal("下载器: 已装好时 DownloadedBytes 为 0", 0L, result.DownloadedBytes);
        ChecksContext.Check("下载器: 已装好时清掉旧残片", !File.Exists(PartPath(root)));
        ChecksContext.Equal("下载器: 已装好时上报 100%", 100, reports[^1].Percent);
    }

    /// <summary>
    /// 残片已经是完整长度（上次传完、进程在收尾前退出）：不该再下一遍，直接校验落位。
    /// 否则 416 自愈会把 1.31 GB 从头重下——这正是「续传」最该救的那个场景。
    /// </summary>
    private static void CheckCompletePartialIsFinalized(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "complete-part");
        var payload = Fill(32 * 1024, 0x6a);
        WriteStaged(root, payload, payload.Length);

        var catalog = CreateCatalog(
            compressed: false,
            payload.Length,
            payload.Length,
            sha: Sha256Hex(payload));
        var fetcher = new FakeDownloadFetcher(payload);
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var result = downloader
            .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 完整残片直接校验落位", result.Success, result.Message);
        ChecksContext.Equal("下载器: 完整残片不再发请求（不重下 1.31 GB）", 0, fetcher.RequestCount);
        ChecksContext.EqualBytes(
            "下载器: 完整残片落位后内容一致",
            payload,
            File.ReadAllBytes(TargetPath(root)));
        ChecksContext.Check("下载器: 完整残片落位后清掉 .part", !File.Exists(PartPath(root)));
    }

    /// <summary>
    /// 残片比期望还长（模型换过版本 / 上次写到别的文件里）：不续传、不去撞 416，直接删掉重下。
    /// </summary>
    private static void CheckLongerPartialIsDropped(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "longer-part");
        var payload = Fill(16 * 1024, 0x6b);
        var part = PartPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        File.WriteAllBytes(part, Fill(payload.Length + 4096, 0x6c));

        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new FakeDownloadFetcher(payload) { ChunkSize = 4 * 1024 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var result = downloader
            .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 残片过长时仍能恢复", result.Success, result.Message);
        ChecksContext.Equal("下载器: 残片过长时不发续传请求（从 0 开始）", 0L, fetcher.LastStartOffset ?? 0L);
        ChecksContext.EqualBytes(
            "下载器: 残片过长后内容被整份覆盖",
            payload,
            File.ReadAllBytes(TargetPath(root)));
    }

    /// <summary>
    /// 分母拿不到（chunked / 无 Content-Length）：必须仍上报已下载字节，且总长是 0，
    /// 让界面显示「已下载 N MB」而不是一个冻住的百分比——冻住的进度条正是这次要修的故障形态。
    /// </summary>
    private static void CheckUnknownTotalStillReportsBytes(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "unknown-total");
        var content = Fill(32 * 1024, 0x6d);
        var gzip = Gzip(content);

        // 压缩 + 假 fetcher 报「总长未知」：分母只能靠 ExpectedSize 折算，而压缩文件不能用它，
        // 所以这条路径必须退化成「按字节上报」。
        var catalog = CreateCatalog(compressed: true, content.Length, content.Length);
        var fetcher = new FakeDownloadFetcher(gzip)
        {
            ChunkSize = 8 * 1024,
            ReportUnknownTotal = true,
        };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var reports = new List<LocalTranslationDownloadProgress>();
        var result = downloader
            .DownloadAsync(
                LocalTranslationModelKind.IndexTranslate,
                new SyncProgress(reports.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 分母未知时仍能下载完成", result.Success, result.Message);
        ChecksContext.Check(
            "下载器: 分母未知时首条上报的总长是 0（界面按字节显示而不是百分比）",
            reports.Count > 0 && reports[0].TotalBytes == 0,
            reports.Count == 0 ? "(无上报)" : "TotalBytes=" + reports[0].TotalBytes + "，共 " + reports.Count + " 条");
        ChecksContext.Check(
            "下载器: 分母未知时已下载字节仍在增长",
            reports.Count >= 2 && reports[^1].BytesReceived > reports[0].BytesReceived,
            reports.Count == 0 ? "(无上报)" : reports[0].BytesReceived + " → " + reports[^1].BytesReceived);
    }

    /// <summary>
    /// 收尾校验阶段被暂停：残片内容其实是完整的，必须保留，下次点「下载」直接落位而不是重下 1.31 GB；
    /// 同时暂停要立刻生效，不能等整份 SHA-256 算完。
    /// </summary>
    private static void CheckCancelDuringVerifyKeepsPartial(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "cancel-verify");
        var content = Fill(2 * 1024 * 1024, 0x6e);
        var gzip = Gzip(content);
        WriteStaged(root, gzip, gzip.Length);

        var catalog = CreateCatalog(
            compressed: true,
            content.Length,
            content.Length,
            sha: Sha256Hex(content));
        var fetcher = new FakeDownloadFetcher(gzip);
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = false;
        try
        {
            downloader
                .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, cancellation.Token)
                .GetAwaiter()
                .GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        ChecksContext.Check("下载器: 收尾阶段也响应取消", cancelled);
        ChecksContext.Check("下载器: 收尾被取消后 .part 保留（内容完整可续）", File.Exists(PartPath(root)));
        ChecksContext.Check(
            "下载器: 收尾被取消后不留下 .tmp 残渣",
            !File.Exists(TargetPath(root) + LocalTranslationModelStore.VerifyTempSuffix));
    }

    /// <summary>压缩文件续传：<c>.part</c> 存 gzip 原始字节，续传后解压结果必须完整正确。</summary>
    private static void CheckCompressedResume(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "compressed");
        var content = Fill(64 * 1024, 0x66);
        var gzip = Gzip(content);
        const int staged = 32;
        WriteStaged(root, gzip, staged);

        var catalog = CreateCatalog(
            compressed: true,
            content.Length,
            content.Length,
            sha: Sha256Hex(content));
        var fetcher = new FakeDownloadFetcher(gzip) { ChunkSize = 16 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var result = downloader
            .DownloadAsync(LocalTranslationModelKind.IndexTranslate, null, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 压缩文件续传成功", result.Success, result.Message);
        ChecksContext.Equal("下载器: 压缩续传的 Range 起点是 gzip 字节偏移", (long?)staged, fetcher.LastStartOffset);
        ChecksContext.EqualBytes(
            "下载器: 压缩续传后解压内容与源一致",
            content,
            File.Exists(TargetPath(root)) ? File.ReadAllBytes(TargetPath(root)) : []);
        ChecksContext.Check("下载器: 压缩续传后残片被清掉", !File.Exists(PartPath(root)));
    }

    /// <summary>对方只有 <see cref="IHttpFetcher"/>（无 Range）：仍要能下、且仍有中间进度。</summary>
    private static void CheckFallbackWithoutRangeSupport(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "fallback");
        var payload = Fill(128 * 1024, 0x67);
        var catalog = CreateCatalog(compressed: false, payload.Length, payload.Length);
        var fetcher = new PlainOnlyFetcher(payload) { ChunkSize = 16 * 1024 };
        var downloader = new LocalTranslationModelDownloader(fetcher, new NullLogger(), catalog, root);

        var reports = new List<LocalTranslationDownloadProgress>();
        var result = downloader
            .DownloadAsync(
                LocalTranslationModelKind.IndexTranslate,
                new SyncProgress(reports.Add),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Check("下载器: 无 Range 能力时仍能下载", result.Success, result.Message);
        ChecksContext.EqualBytes(
            "下载器: 无 Range 能力时内容一致",
            payload,
            File.ReadAllBytes(TargetPath(root)));
        ChecksContext.Check(
            "下载器: 无 Range 能力时仍有中间进度",
            reports.Any(item => item.BytesReceived > 0 && item.BytesReceived < item.TotalBytes),
            "上报 " + reports.Count + " 次");
    }

    /// <summary>残片占用与删除：1.31 GB 的 .part 必须能被算进占用、被删除清干净。</summary>
    private static void CheckStagedBytesAndRemove(string tempRoot)
    {
        var root = Path.Combine(tempRoot, "staged");
        var catalog = CreateCatalog(compressed: false, expectedSize: 16, totalBytes: 16);
        var store = new LocalTranslationModelStore(catalog, root);

        WriteStaged(root, Fill(8, 0x68), 8);
        ChecksContext.Equal("下载器: 残片占用只算 .part", 8L, store.GetStagedBytes(LocalTranslationModelKind.IndexTranslate));

        var directory = LocalTranslationModelStore.GetModelDirectory(LocalTranslationModelKind.IndexTranslate, root);
        File.WriteAllBytes(Path.Combine(directory, FileName + LocalTranslationModelStore.VerifyTempSuffix), Fill(4, 0x69));
        ChecksContext.Equal(
            "下载器: .tmp 也算进残片占用",
            12L,
            store.GetStagedBytes(LocalTranslationModelKind.IndexTranslate));
        ChecksContext.Equal(
            "下载器: 未装完时安装占用为 0",
            0L,
            store.GetInstalledSize(LocalTranslationModelKind.IndexTranslate));

        var removed = store.Remove(LocalTranslationModelKind.IndexTranslate);
        ChecksContext.Check(
            "下载器: 删除把残片一起清掉",
            !Directory.Exists(directory),
            removed.Status.ToString());
        ChecksContext.Equal(
            "下载器: 删除后残片占用归零",
            0L,
            store.GetStagedBytes(LocalTranslationModelKind.IndexTranslate));
    }

    // ---------------------------------------------------------------- 辅助

    private static bool IsMonotonic(IReadOnlyList<LocalTranslationDownloadProgress> reports)
    {
        for (var index = 1; index < reports.Count; index++)
        {
            if (reports[index].BytesReceived < reports[index - 1].BytesReceived)
            {
                return false;
            }
        }

        return true;
    }

    private static LocalTranslationModelCatalog CreateCatalog(
        bool compressed,
        long expectedSize,
        long totalBytes,
        string? sha = null) =>
        new(new LocalTranslationModel(
            LocalTranslationModelKind.IndexTranslate,
            "自检模型",
            totalBytes,
            sha,
            new[]
            {
                new LocalTranslationModelFile(
                    FileName,
                    "https://example.invalid/" + FileName,
                    Sha256: sha,
                    ExpectedSize: expectedSize,
                    Compressed: compressed),
            }));

    private static string TargetPath(string root) =>
        Path.Combine(
            LocalTranslationModelStore.GetModelDirectory(LocalTranslationModelKind.IndexTranslate, root),
            FileName);

    private static string PartPath(string root) =>
        TargetPath(root) + LocalTranslationModelStore.PartialSuffix;

    private static void WriteStaged(string root, byte[] payload, int count)
    {
        var part = PartPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(part)!);
        File.WriteAllBytes(part, payload[..Math.Min(count, payload.Length)]);
    }

    private static void WriteTarget(string root, byte[] payload)
    {
        var target = TargetPath(root);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllBytes(target, payload);
    }

    private static byte[] Fill(int size, byte value)
    {
        var buffer = new byte[size];
        Array.Fill(buffer, value);
        return buffer;
    }

    private static byte[] Gzip(byte[] content)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            gzip.Write(content, 0, content.Length);
        }

        return output.ToArray();
    }

    private static string Sha256Hex(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content));

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>同步的进度收集器：回调就在下载线程里跑，便于断言顺序与节流。</summary>
    private sealed class SyncProgress : IProgress<LocalTranslationDownloadProgress>
    {
        private readonly Action<LocalTranslationDownloadProgress> _report;

        public SyncProgress(Action<LocalTranslationDownloadProgress> report) => _report = report;

        public void Report(LocalTranslationDownloadProgress value) => _report(value);
    }

    /// <summary>假下载源：可控制分块大小、是否支持 Range，并记录请求参数。</summary>
    private sealed class FakeDownloadFetcher : IHttpFetcher, IDownloadFetcher
    {
        private readonly byte[] _payload;

        public FakeDownloadFetcher(byte[] payload) => _payload = payload;

        public int ChunkSize { get; init; } = 64 * 1024;

        public bool SupportsRange { get; init; } = true;

        public int StallAfterChunks { get; init; }

        /// <summary>true 表示模拟「服务端不给总长」（chunked），进度只能按字节上报。</summary>
        public bool ReportUnknownTotal { get; init; }

        public int RequestCount { get; private set; }

        public long? LastStartOffset { get; private set; }

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult<Stream>(CreateStream(0, _payload.Length));
        }

        public Task<HttpDownload> GetDownloadAsync(
            Uri uri,
            long? startOffset,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            LastStartOffset = startOffset;

            var from = startOffset.GetValueOrDefault();
            if (from < 0 || from > _payload.Length || !SupportsRange)
            {
                from = 0;
            }

            var remaining = _payload.Length - (int)from;
            // 模拟 chunked：总长与本次长度都不给，下载器只能退化成「按字节上报」。
            return Task.FromResult(new HttpDownload(
                CreateStream((int)from, remaining),
                ReportUnknownTotal ? null : remaining,
                from,
                ReportUnknownTotal ? null : _payload.Length));
        }

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        private Stream CreateStream(int offset, int count) =>
            new ControlledStream(_payload.AsSpan(offset, count).ToArray(), ChunkSize, StallAfterChunks);
    }

    /// <summary>只实现 <see cref="IHttpFetcher"/> 的假源：验证「没有 Range 也必须能下」。</summary>
    private sealed class PlainOnlyFetcher : IHttpFetcher
    {
        private readonly byte[] _payload;

        public PlainOnlyFetcher(byte[] payload) => _payload = payload;

        public int ChunkSize { get; init; } = 64 * 1024;

        public Task<Stream> GetAsync(Uri uri, CancellationToken cancellationToken) =>
            Task.FromResult<Stream>(new ControlledStream(_payload, ChunkSize, 0));

        public Task<string> PostJsonAsync(Uri uri, string jsonBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<string> PostFormAsync(Uri uri, string formBody, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>按固定块大小吐字节的流；<c>StallAfterChunks</c> 之后会一直等到被取消。</summary>
    private sealed class ControlledStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _chunkSize;
        private readonly int _stallAfterChunks;
        private int _position;
        private int _chunksRead;

        public ControlledStream(byte[] data, int chunkSize, int stallAfterChunks)
        {
            _data = data;
            _chunkSize = chunkSize;
            _stallAfterChunks = stallAfterChunks;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => _data.Length;

        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (_stallAfterChunks > 0 && _chunksRead >= _stallAfterChunks)
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            }

            if (_position >= _data.Length)
            {
                return 0;
            }

            var count = Math.Min(Math.Min(buffer.Length, _chunkSize), _data.Length - _position);
            _data.AsSpan(_position, count).CopyTo(buffer.Span);
            _position += count;
            _chunksRead++;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(new Memory<byte>(buffer, offset, count)).AsTask().GetAwaiter().GetResult();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class NullLogger : IAppLogger
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
