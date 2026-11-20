namespace PdxLauncher.Core.Online.Translation.Local;

using System.IO.Compression;
using System.Security.Cryptography;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 本地翻译模型的下载与安装。
/// <para>
/// 三条硬约束（都是踩过的）：
/// <list type="number">
/// <item><b>进度必须逐字节上报</b>：Index-Translate-2B 是单个 1.31 GB 文件，只在「文件下完」时上报
/// 等于整段下载停在 0%，下完直接跳 100%。</item>
/// <item><b>中断可续传</b>：<c>.part</c> 里存<b>原始传输字节</b>（压缩文件就是 gzip 原始字节），
/// 所以 Range 续传对压缩 / 非压缩统一成立；解压只发生在收尾阶段。</item>
/// <item><b>写坏就整份重来</b>：尺寸 / 哈希不符时删掉残片，绝不把「长度对、内容错」的文件拼进模型目录。</item>
/// </list>
/// 走 <see cref="IHttpFetcher"/>，自动受 <see cref="OnlineHosts"/> 白名单约束；只有对方同时实现
/// <see cref="IDownloadFetcher"/> 时才启用续传，否则退回「无续传、但有逐字节进度」的普通下载。
/// </para>
/// </summary>
public sealed class LocalTranslationModelDownloader
{
    private const int BufferSize = 128 * 1024;

    /// <summary>进度上报的最小分数跨度（0.5%）：1.31 GB 约每 6.5 MB 一次。</summary>
    private const double ProgressFractionStep = 0.005;

    /// <summary>进度上报的最长间隔：慢链路（1 MB/s）也要每 250 ms 动一次。</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromMilliseconds(250);

    private readonly IHttpFetcher _http;
    private readonly IAppLogger _logger;
    private readonly LocalTranslationModelCatalog _catalog;
    private readonly string? _rootDirectory;

    /// <summary>创建下载器。</summary>
    /// <param name="http">HTTP 抓取器。</param>
    /// <param name="logger">日志。</param>
    /// <param name="catalog">模型清单。</param>
    /// <param name="rootDirectory">模型根目录覆盖（自检用临时目录）；null 表示默认根目录。</param>
    public LocalTranslationModelDownloader(
        IHttpFetcher http,
        IAppLogger logger,
        LocalTranslationModelCatalog catalog,
        string? rootDirectory = null)
    {
        _http = http;
        _logger = logger;
        _catalog = catalog;
        _rootDirectory = string.IsNullOrWhiteSpace(rootDirectory) ? null : rootDirectory;
    }

    /// <summary>
    /// 下载并安装某个模型。已装好的文件会跳过。
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="progress">进度回调（逐字节节流上报）。</param>
    /// <param name="cancellationToken">取消令牌；取消时已下载的 <c>.part</c> 会保留，下次续传。</param>
    public async Task<LocalTranslationModelDownloadResult> DownloadAsync(
        LocalTranslationModelKind kind,
        IProgress<LocalTranslationDownloadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return new LocalTranslationModelDownloadResult(false, "未知的本地模型。", 0);
        }

        var directory = LocalTranslationModelStore.GetModelDirectory(kind, _rootDirectory);
        Directory.CreateDirectory(directory);

        var reporter = new ProgressReporter(model.TotalBytes, progress);
        long downloaded = 0;
        long done = 0;

        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(directory, file.FileName);
            if (IsComplete(target, file))
            {
                // 已经完整：顺手清掉上一轮留下的残片，别白占 1.3 GB。
                TryDelete(PartPath(target));
                TryDelete(TempPath(target));
                done += file.ExpectedSize > 0 ? file.ExpectedSize : SizeOf(target);
                reporter.Report(done, file.FileName, resumed: false, force: true);
                continue;
            }

            DownloadedFile written;
            try
            {
                written = await DownloadFileAsync(
                        file,
                        target,
                        done,
                        reporter,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 取消不是失败：残片留着续传，进度文案由调用方按「暂停」处理。
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"本地模型下载失败：{file.FileName}", ex);
                return new LocalTranslationModelDownloadResult(
                    false,
                    $"下载 {file.FileName} 失败：{ex.Message}",
                    downloaded);
            }

            // DownloadedBytes 只说「这次真的从网络搬了多少」，续传时不会把上次那 1 GB 再算一遍。
            downloaded += written.Transferred;
            done += file.ExpectedSize > 0 ? file.ExpectedSize : written.Size;
            reporter.Report(done, file.FileName, resumed: false, force: true);
        }

        try
        {
            LocalTranslationModelConfigWriter.Write(kind, directory);
        }
        catch (Exception ex)
        {
            _logger.Error("写入本地模型配置失败", ex);
            return new LocalTranslationModelDownloadResult(false, $"写入模型配置失败：{ex.Message}", downloaded);
        }

        reporter.Report(Math.Max(done, model.TotalBytes), null, resumed: false, force: true);
        _logger.Info($"本地模型已就绪：{model.DisplayName}（本次下载 {downloaded} 字节）");
        return new LocalTranslationModelDownloadResult(true, $"{model.DisplayName} 已下载完成。", downloaded);
    }

    /// <summary>
    /// 下载单个文件：先落 <c>.part</c>（原始字节），下完再解压 / 校验 / 改名到位。
    /// </summary>
    /// <param name="file">文件清单项。</param>
    /// <param name="target">最终文件路径。</param>
    /// <param name="doneBefore">本文件之前已经完成的字节数（进度基数）。</param>
    /// <param name="reporter">节流后的进度上报器。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>最终产物的字节数与本次实际从网络传输的字节数（续传时两者不相等）。</returns>
    private async Task<DownloadedFile> DownloadFileAsync(
        LocalTranslationModelFile file,
        string target,
        long doneBefore,
        ProgressReporter reporter,
        CancellationToken cancellationToken)
    {
        var part = PartPath(target);
        // 不变量：target 一定是「模型目录 + 清单里的文件名」拼出来的，必然带目录部分。
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var stagedLength = SizeOf(part);

        // 残片已经是完整长度（上次传完、进程在收尾前退出）：不必再下一遍，
        // 直接走校验与落位。否则 416 自愈会把这 1.31 GB 从头重下一遍。
        // 压缩文件的 ExpectedSize 是解压后长度，.part 是 gzip 字节，两者本来就不等，不适用这条。
        if (!file.Compressed && file.ExpectedSize > 0 && stagedLength == file.ExpectedSize)
        {
            _logger.Info($"本地模型残片已完整，直接校验落位：{file.FileName}");
            return new DownloadedFile(FinishPlain(file, part, target, cancellationToken), 0);
        }

        // 残片长度对不上 ExpectedSize 且已经过短：那是上次没传完，续传；但残片比期望还长
        // （模型换过版本 / 上次写到别的文件里）就无法续传，删掉从头来，别让 416 兜底绕一圈。
        if (!file.Compressed && file.ExpectedSize > 0 && stagedLength > file.ExpectedSize)
        {
            _logger.Warn($"本地模型残片比期望还长，删掉重下：{file.FileName}（{stagedLength} > {file.ExpectedSize}）");
            TryDelete(part);
            stagedLength = 0;
        }

        var uri = new Uri(file.Url);

        Stream source;
        long transferStart;
        long? transferTotal;
        if (_http is IDownloadFetcher fetcher)
        {
            var download = await fetcher
                .GetDownloadAsync(uri, stagedLength > 0 ? stagedLength : null, cancellationToken)
                .ConfigureAwait(false);
            source = download.Content;
            transferStart = download.RangeStart;

            // 206 只有在服务端给了 Content-Range 时才知道「原始文件总长」。没有它时
            // ContentLength 只是剩余部分，拿它当分母会让进度瞬间冲到 100%——宁可只说文件边界。
            transferTotal = download.TotalLength
                ?? (download.RangeStart == 0 ? download.ContentLength : null);
        }
        else
        {
            // 没有 Range 能力：整份重下，但仍按 ExpectedSize 逐字节上报。
            source = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            transferStart = 0;
            transferTotal = file.Compressed
                ? null
                : file.ExpectedSize > 0 ? file.ExpectedSize : null;
        }

        var resumed = transferStart > 0 && transferStart == stagedLength;
        var transferred = 0L;
        await using (source.ConfigureAwait(false))
        {
            // 服务端忽略了 Range、或残片长度对不上：截断后从头写，绝不拼接。
            var mode = resumed ? FileMode.Open : FileMode.Create;
            await using var output = new FileStream(
                part,
                mode,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            if (resumed)
            {
                output.Seek(transferStart, SeekOrigin.Begin);
                _logger.Info($"本地模型续传：{file.FileName} 从 {transferStart} 字节继续");
            }
            var expected = Math.Max(0, file.ExpectedSize);

            // 分母：优先用响应给的总长；拿不到时退到清单里的 ExpectedSize（非压缩文件字节一一对应）。
            // 再拿不到（压缩且无总长）就只报 doneBefore、把总长传 0，让界面显示「已下载 N MB」
            // 而不是一个冻住的百分比——冻住的进度条正是这次要修的故障形态。
            var denominator = transferTotal is > 0
                ? transferTotal.Value
                : expected > 0 && !file.Compressed ? expected : 0L;
            var transferDone = transferStart;
            var buffer = new byte[BufferSize];
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                transferDone += read;
                transferred += read;

                // 压缩文件按「传输字节 / 传输总长」折算到解压后的期望尺寸：
                // 进度永远以用户最终拿到的那个大小为分母，不会因为 gzip 比例而乱跳。
                if (expected > 0 && denominator > 0)
                {
                    var transferFraction = Math.Clamp((double)transferDone / denominator, 0d, 1d);
                    reporter.Report(doneBefore + (long)(expected * transferFraction), file.FileName, resumed);
                }
                else
                {
                    // 分母未知：报「已完成 + 已传输量」，总长传 0（界面按字节显示，不画百分比）。
                    reporter.ReportUnknownTotal(doneBefore + transferred, file.FileName, resumed);
                }
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var size = file.Compressed
            ? FinishCompressed(file, part, target, cancellationToken)
            : FinishPlain(file, part, target, cancellationToken);
        return new DownloadedFile(size, transferred);
    }

    /// <summary>
    /// 非压缩文件收尾：尺寸必须正好是期望值，哈希对得上才改名落位。
    /// </summary>
    private long FinishPlain(
        LocalTranslationModelFile file,
        string part,
        string target,
        CancellationToken cancellationToken)
    {
        var actual = SizeOf(part);
        if (file.ExpectedSize > 0 && actual != file.ExpectedSize)
        {
            DeleteStaged(part, TempPath(target));
            throw new InvalidDataException(
                $"文件大小不符：{file.FileName}（期望 {file.ExpectedSize}，实际 {actual}）");
        }

        try
        {
            VerifyHash(part, file, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // 校验阶段被暂停：残片内容可能是对的，保留它，下次点「下载」会短路落位而不是重下。
            throw;
        }
        catch
        {
            DeleteStaged(part, TempPath(target));
            throw;
        }

        File.Move(part, target, overwrite: true);
        return actual;
    }

    /// <summary>
    /// 压缩文件收尾：<c>.part</c> 是 gzip 原始字节，解压到 <c>.tmp</c> 边算尺寸与哈希，
    /// 全对才改名落位，然后删掉残片。
    /// </summary>
    private long FinishCompressed(
        LocalTranslationModelFile file,
        string part,
        string target,
        CancellationToken cancellationToken)
    {
        var temp = TempPath(target);
        long written = 0;
        string? actualHash = null;
        try
        {
            using (var raw = new FileStream(
                       part,
                       FileMode.Open,
                       FileAccess.Read,
                       FileShare.Read,
                       BufferSize,
                       FileOptions.SequentialScan))
            using (var gzip = new GZipStream(raw, CompressionMode.Decompress))
            using (var output = new FileStream(
                       temp,
                       FileMode.Create,
                       FileAccess.Write,
                       FileShare.None,
                       BufferSize))
            using (var hasher = string.IsNullOrEmpty(file.Sha256)
                       ? null
                       : IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = gzip.Read(buffer, 0, buffer.Length)) > 0)
                {
                    // 1.31 GB 的解压 + 哈希可能持续几十秒：暂停按钮必须在这段时间里也立刻生效。
                    cancellationToken.ThrowIfCancellationRequested();
                    output.Write(buffer, 0, read);
                    hasher?.AppendData(buffer, 0, read);
                    written += read;
                }

                // 哈希必须在 using 块里取：IncrementalHash 出了作用域就被释放，之后再读会抛。
                if (hasher is not null)
                {
                    actualHash = Convert.ToHexStringLower(hasher.GetHashAndReset());
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 收尾阶段被暂停：残片是完整的 gzip 字节，保留它，下次续传仍然成立。
            TryDelete(temp);
            throw;
        }
        catch
        {
            // 解压本身失败（gzip 截断 / 磁盘满）：残片内容不可信，删掉重下。
            DeleteStaged(part, temp);
            throw;
        }

        if (file.ExpectedSize > 0 && written != file.ExpectedSize)
        {
            DeleteStaged(part, temp);
            throw new InvalidDataException(
                $"解压后大小不符：{file.FileName}（期望 {file.ExpectedSize}，实际 {written}）");
        }

        if (actualHash is not null
            && !string.Equals(actualHash, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            DeleteStaged(part, temp);
            throw new InvalidDataException($"校验失败：{file.FileName}");
        }

        // 落位在 finally 之外：目标文件被引擎占用 / 磁盘满时失败，这里不能连累已下载完的残片。
        // 校验已经全部通过，残片内容就是正确内容，下次点「下载」会短路落位而不是重下。
        File.Move(temp, target, overwrite: true);
        TryDelete(part);
        return written;
    }

    /// <summary>按清单里的 SHA-256 校验一个已落盘文件；清单没给哈希就跳过。</summary>
    private static void VerifyHash(
        string path,
        LocalTranslationModelFile file,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(file.Sha256))
        {
            return;
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.SequentialScan);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[BufferSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            // 1.31 GB 的 SHA-256 在慢盘上要好几秒：暂停必须在这段时间里也能立刻生效。
            cancellationToken.ThrowIfCancellationRequested();
            hasher.AppendData(buffer, 0, read);
        }

        var actual = Convert.ToHexStringLower(hasher.GetHashAndReset());
        if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"校验失败：{file.FileName}");
        }
    }

    private static bool IsComplete(string path, LocalTranslationModelFile file)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return file.ExpectedSize <= 0 || SizeOf(path) == file.ExpectedSize;
    }

    private static string PartPath(string target) =>
        target + LocalTranslationModelStore.PartialSuffix;

    private static string TempPath(string target) =>
        target + LocalTranslationModelStore.VerifyTempSuffix;

    private static long SizeOf(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;

    /// <summary>删掉残片与校验临时文件；失败只可能是句柄占用，交给下一次下载覆盖。</summary>
    private static void DeleteStaged(string part, string temp)
    {
        TryDelete(part);
        TryDelete(temp);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 删不掉不影响正确性：下一次下载会用 FileMode.Create 覆盖它。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>一个文件下完后的两个数字：最终产物大小（算进度）与本次实际传输量（算"这次下了多少"）。</summary>
    /// <param name="Size">落位文件（解压后）的字节数。</param>
    /// <param name="Transferred">本次从网络读到的字节数；续传时小于 <paramref name="Size"/>。</param>
    private readonly record struct DownloadedFile(long Size, long Transferred);

    /// <summary>
    /// 进度上报的节流器：分数跨度 ≥0.5% <b>或</b>距上次 ≥250 ms 才上报。
    /// <para>
    /// 没有这层节流，1.31 GB / 128 KiB ≈ 一万次回调会把 UI 的 DispatcherQueue 灌满，
    /// 表现为「进度动了但界面卡」。有它之后最密也只有 4 次/秒。
    /// </para>
    /// </summary>
    private sealed class ProgressReporter
    {
        private readonly IProgress<LocalTranslationDownloadProgress>? _progress;
        private readonly long _total;
        private double _lastFraction = -1d;
        private long _lastTimestamp;

        public ProgressReporter(long total, IProgress<LocalTranslationDownloadProgress>? progress)
        {
            _total = Math.Max(0, total);
            _progress = progress;
        }

        public void Report(long bytesReceived, string? currentFile, bool resumed, bool force = false)
        {
            if (_progress is null)
            {
                return;
            }

            var received = _total > 0 ? Math.Clamp(bytesReceived, 0, _total) : Math.Max(0, bytesReceived);
            var fraction = _total > 0 ? (double)received / _total : 0d;
            var now = Environment.TickCount64;
            var due = force
                || _lastFraction < 0
                || fraction - _lastFraction >= ProgressFractionStep
                || now - _lastTimestamp >= (long)ProgressInterval.TotalMilliseconds;
            if (!due)
            {
                return;
            }

            _lastFraction = fraction;
            _lastTimestamp = now;
            _progress.Report(new LocalTranslationDownloadProgress(received, _total, currentFile, resumed));
        }

        /// <summary>
        /// 分母未知时上报：<c>TotalBytes</c> 传 0，界面据此显示「已下载 N MB」而不是百分比。
        /// <para>
        /// 单独一个方法是为了保留节流：没有它，chunked 响应下每 128 KiB 都会刷一次回调。
        /// </para>
        /// </summary>
        public void ReportUnknownTotal(long bytesReceived, string? currentFile, bool resumed)
        {
            if (_progress is null)
            {
                return;
            }

            var now = Environment.TickCount64;
            if (_lastTimestamp != 0 && now - _lastTimestamp < (long)ProgressInterval.TotalMilliseconds)
            {
                return;
            }

            _lastTimestamp = now;
            _progress.Report(new LocalTranslationDownloadProgress(Math.Max(0, bytesReceived), 0, currentFile, resumed));
        }
    }
}
