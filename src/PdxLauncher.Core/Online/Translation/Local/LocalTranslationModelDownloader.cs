namespace PdxLauncher.Core.Online.Translation.Local;

using System.IO.Compression;
using System.Security.Cryptography;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 本地翻译模型的下载与安装。
/// <para>
/// 走 <see cref="IHttpFetcher"/>，因此自动受 <see cref="OnlineHosts"/> 白名单约束；
/// 下载先落 <c>.part</c> 临时文件，写完再改名，避免半截文件被当成完整模型。
/// </para>
/// </summary>
public sealed class LocalTranslationModelDownloader
{
    private const int BufferSize = 128 * 1024;
    private const string PartialSuffix = ".part";

    private readonly IHttpFetcher _http;
    private readonly IAppLogger _logger;
    private readonly LocalTranslationModelCatalog _catalog;
    private readonly LocalTranslationModelStore _store;

    /// <summary>创建下载器。</summary>
    /// <param name="http">HTTP 抓取器。</param>
    /// <param name="logger">日志。</param>
    /// <param name="catalog">模型清单。</param>
    /// <param name="store">模型仓库。</param>
    public LocalTranslationModelDownloader(
        IHttpFetcher http,
        IAppLogger logger,
        LocalTranslationModelCatalog catalog,
        LocalTranslationModelStore store)
    {
        _http = http;
        _logger = logger;
        _catalog = catalog;
        _store = store;
    }

    /// <summary>
    /// 下载并安装某个模型。已装好的文件会跳过。
    /// </summary>
    /// <param name="kind">模型种类。</param>
    /// <param name="progress">进度回调（0 到 1）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<LocalTranslationModelDownloadResult> DownloadAsync(
        LocalTranslationModelKind kind,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var model = _catalog.Get(kind);
        if (model is null)
        {
            return new LocalTranslationModelDownloadResult(false, "未知的本地模型。", 0);
        }

        var directory = LocalTranslationModelStore.GetModelDirectory(kind);
        Directory.CreateDirectory(directory);

        long downloaded = 0;
        var total = Math.Max(1, model.TotalBytes);
        long done = 0;

        foreach (var file in model.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var target = Path.Combine(directory, file.FileName);
            if (IsComplete(target, file))
            {
                done += file.ExpectedSize > 0 ? file.ExpectedSize : 0;
                progress?.Report(Math.Clamp((double)done / total, 0, 1));
                continue;
            }

            try
            {
                downloaded += await DownloadFileAsync(file, target, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.Error($"本地模型下载失败：{file.FileName}", ex);
                return new LocalTranslationModelDownloadResult(false, $"下载 {file.FileName} 失败：{ex.Message}", downloaded);
            }

            done += file.ExpectedSize > 0 ? file.ExpectedSize : 0;
            progress?.Report(Math.Clamp((double)done / total, 0, 1));
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

        _logger.Info($"本地模型已就绪：{model.DisplayName}");
        return new LocalTranslationModelDownloadResult(true, $"{model.DisplayName} 已下载完成。", downloaded);
    }

    private static bool IsComplete(string path, LocalTranslationModelFile file)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        if (file.ExpectedSize > 0 && new FileInfo(path).Length != file.ExpectedSize)
        {
            return false;
        }

        return true;
    }

    private async Task<long> DownloadFileAsync(
        LocalTranslationModelFile file,
        string target,
        CancellationToken cancellationToken)
    {
        var part = target + PartialSuffix;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        var uri = new Uri(file.Url);
        await using var source = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);

        long written = 0;
        await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
        {
            using var input = file.Compressed
                ? new GZipStream(source, CompressionMode.Decompress, leaveOpen: true)
                : source;
            var hasher = string.IsNullOrEmpty(file.Sha256) ? null : SHA256.Create();
            try
            {
                var buffer = new byte[BufferSize];
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    hasher?.TransformBlock(buffer, 0, read, null, 0);
                    written += read;
                }

                if (hasher is not null)
                {
                    hasher.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
                    var actual = Convert.ToHexString(hasher.Hash!).ToLowerInvariant();
                    if (!string.Equals(actual, file.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException($"校验失败：{file.FileName}");
                    }
                }
            }
            finally
            {
                hasher?.Dispose();
            }
        }

        if (file.ExpectedSize > 0 && written != file.ExpectedSize)
        {
            File.Delete(part);
            throw new InvalidDataException($"文件大小不符：{file.FileName}");
        }

        File.Move(part, target, overwrite: true);
        return written;
    }
}
