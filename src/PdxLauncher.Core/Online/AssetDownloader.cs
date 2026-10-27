namespace PdxLauncher.Core.Online;

using System.Net.Http;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 主题素材下载器。实测要点（docs/DATA-FORMATS.md §7）：
/// 素材 URL 无扩展名且 Content-Type 不可信，因此类型必须靠 magic bytes 判定；
/// 流式写 &lt;target&gt;.part 再原子改名；失败/取消删 .part 并保留旧图。
/// </summary>
public sealed class AssetDownloader : IAssetDownloader, IDisposable
{
    /// <summary>同时进行的下载数上限（文档 §7 要求并发 ≤2）。</summary>
    public const int MaxConcurrentDownloads = 2;

    /// <summary>
    /// 单项素材的最大尝试次数（首次 + 2 次重试）。
    /// 实测到 metadata-assets 的 TLS 握手会阶段性超时：一次抖动就让 6 项素材全灭，
    /// 而素材 URL 带内容 hash、失败后要等下一轮刷新才有机会补，所以这里必须自带重试。
    /// </summary>
    public const int MaxAttempts = 3;

    /// <summary>首次重试的等待时间；第 2 次重试按 3 倍退避（500ms → 1500ms）。</summary>
    public static readonly TimeSpan DefaultRetryBaseDelay = TimeSpan.FromMilliseconds(500);

    /// <summary>缓冲区 80 KB：兼顾慢速代理下的内存与吞吐。</summary>
    private const int BufferSize = 80 * 1024;

    /// <summary>失败时 .part 的临时后缀。</summary>
    public const string PartSuffix = ".part";

    private readonly IHttpFetcher _http;
    private readonly IAppLogger _logger;
    private readonly IFileSystem _fs;
    private readonly SemaphoreSlim _slots = new(MaxConcurrentDownloads, MaxConcurrentDownloads);

    public AssetDownloader(IHttpFetcher http, IAppLogger logger, IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(fs);
        _http = http;
        _logger = logger;
        _fs = fs;
    }

    /// <summary>重试退避基数；测试把它设成 zero 以免拖慢自检。</summary>
    public TimeSpan RetryBaseDelay { get; init; } = DefaultRetryBaseDelay;

    public async Task<string?> DownloadAsync(
        string url,
        string targetPathWithoutExtension,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPathWithoutExtension);

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            _logger.Warn("素材 URL 非法，拒绝下载: " + url);
            return null;
        }

        if (!OnlineHosts.IsAllowed(uri))
        {
            _logger.Warn("素材域名不在白名单内，拒绝下载: " + uri.Host + " (" + url + ")");
            return null;
        }

        var partPath = targetPathWithoutExtension + PartSuffix;
        await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await DownloadCoreAsync(uri, targetPathWithoutExtension, partPath, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (TransientDownloadException ex)
                {
                    if (attempt >= MaxAttempts)
                    {
                        _logger.Warn(ex.Message + "（已重试 " + (MaxAttempts - 1) + " 次）(" + uri + ")");
                        return null;
                    }

                    _logger.Warn(
                        "素材下载重试 " + attempt + "/" + (MaxAttempts - 1) + ": " + ex.Message + " (" + uri + ")");
                    await Task.Delay(RetryDelay(attempt), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _slots.Release();
        }
    }

    /// <summary>第 n 次失败后的等待时间：500ms、1500ms（3 倍退避）。</summary>
    private TimeSpan RetryDelay(int attempt) =>
        attempt <= 1 ? RetryBaseDelay : TimeSpan.FromMilliseconds(RetryBaseDelay.TotalMilliseconds * 3);

    private async Task<string?> DownloadCoreAsync(
        Uri uri,
        string targetPathWithoutExtension,
        string partPath,
        CancellationToken cancellationToken)
    {
        try
        {
            var directory = _fs.GetDirectoryName(targetPathWithoutExtension);
            if (!string.IsNullOrEmpty(directory))
            {
                _fs.EnsureDirectory(directory);
            }

            // 上一次中断可能残留 .part，先清掉，避免误判为本次内容。
            TryDeletePart(partPath);

            long total = 0;
            var head = new byte[AssetFileTypeDetector.HeadBytes];
            var headLength = 0;
            var oversize = false;

            await using (var source = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false))
            await using (var destination = new FileStream(
                             partPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None,
                             BufferSize,
                             useAsync: true))
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    if (headLength < head.Length)
                    {
                        var copy = Math.Min(read, head.Length - headLength);
                        Buffer.BlockCopy(buffer, 0, head, headLength, copy);
                        headLength += copy;
                    }

                    total += read;

                    // 只置标志位：此时 .part 仍被本方法的 FileStream 独占打开（FileShare.None），
                    // 在使用中删除必然失败并留下垃圾文件，必须等 using 作用域结束后再删。
                    if (total > AssetFileTypeDetector.MaximumFileBytes)
                    {
                        oversize = true;
                        break;
                    }

                    await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }

                await destination.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            if (oversize)
            {
                _logger.Warn("素材超过 40 MB 上限，中止下载: " + uri);
                TryDeletePart(partPath);
                return null;
            }

            if (total < AssetFileTypeDetector.MinimumFileBytes)
            {
                _logger.Warn(
                    "素材小于 " + (AssetFileTypeDetector.MinimumFileBytes / 1024) + " KB，视为坏文件: " + total + " B " + uri);
                TryDeletePart(partPath);
                return null;
            }

            var type = AssetFileTypeDetector.Detect(head.AsSpan(0, headLength));
            var extension = AssetFileTypeDetector.GetExtension(type);
            if (type == AssetFileType.Unknown)
            {
                _logger.Warn("素材 magic bytes 无法识别，拒绝落盘: " + uri);
                TryDeletePart(partPath);
                return null;
            }

            var finalPath = targetPathWithoutExtension + extension;
            try
            {
                // 原子改名：失败/取消时旧文件保持原样（File.Move 覆盖是原子的）。
                File.Move(partPath, finalPath, overwrite: true);
            }
            catch (IOException ex)
            {
                _logger.Warn("素材改名失败: " + ex.Message);
                TryDeletePart(partPath);
                return null;
            }

            _logger.Info("素材下载完成: " + finalPath + " (" + total + " B, " + type + ")");
            return finalPath;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            TryDeletePart(partPath);
            throw;
        }
        catch (OperationCanceledException ex)
        {
            // 非调用方取消（多为 HttpClient.Timeout / ConnectTimeout）按瞬时失败处理：
            // 交给 DownloadAsync 重试，重试用尽才保留旧图。
            TryDeletePart(partPath);
            throw new TransientDownloadException("素材下载超时: " + ex.Message, ex);
        }
        catch (HttpRequestException ex)
        {
            TryDeletePart(partPath);
            throw new TransientDownloadException("素材下载失败: " + ex.Message, ex);
        }
        catch (IOException ex)
        {
            _logger.Warn("素材写入失败: " + ex.Message + " (" + uri + ")");
            TryDeletePart(partPath);
            return null;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("素材写入无权限: " + ex.Message + " (" + uri + ")");
            TryDeletePart(partPath);
            return null;
        }
    }

    private void TryDeletePart(string partPath)
    {
        try
        {
            if (File.Exists(partPath))
            {
                File.Delete(partPath);
            }
        }
        catch (IOException ex)
        {
            _logger.Warn("清理 .part 失败: " + ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.Warn("清理 .part 无权限: " + ex.Message);
        }
    }

    public void Dispose() => _slots.Dispose();

    /// <summary>
    /// 可重试的下载失败（超时 / 连接错误）。只在 AssetDownloader 内部流转，
    /// DownloadAsync 重试用尽后转成「返回 null + 一条 warn」，绝不外抛给主题解析。
    /// </summary>
    private sealed class TransientDownloadException : Exception
    {
        public TransientDownloadException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
