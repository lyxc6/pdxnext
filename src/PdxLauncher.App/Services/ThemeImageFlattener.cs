namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

/// <summary>
/// 把「纱层」主题图摊平成不透明副本（判定见 <see cref="WashLayerPolicy"/>）。
/// <para>
/// EU4 的 assets/app-background.png 整幅 alpha 恒为 51/255，RGB 却是一张完整油画；
/// 直接照 alpha 画到卡片底色上就是一片灰。这里解码像素、判定纱层后把 alpha 置满，
/// 重新编码成 PNG 存进 <c>data\theme-cache\flat\</c>，路径按「原图路径 + 文件大小 + 规则版本」缓存。
/// 非纱层、非位图、文件缺失、解码失败时一律返回原路径，永不抛异常。
/// </para>
/// <para>
/// 三道缓存，缺一不可（2026-10-27 之前只有第 1 道，导致同一张 5.7 MB 的 EU4 背景被重复摊平 7 次）：
/// <list type="number">
/// <item>内存 <see cref="_resolved"/>：同一进程内命中直接返回。</item>
/// <item>磁盘 <c>flat\*.png</c>：跨进程命中直接复用，不再解码/编码。</item>
/// <item>磁盘 <c>flat\*.noflat</c> 否定标记：跨进程记住「这张图不是纱层」，避免每次冷启动都白解码一遍大图。</item>
/// </list>
/// </para>
/// <para>
/// 线程约定：整个摊平过程（解码 → 逐像素 → 重编码 → 落盘）都在线程池上跑，
/// 绝不在 UI 线程执行。解码 2560×1340 的 PNG 约 13.7 MB 像素缓冲区，在 UI 线程就是秒级卡顿。
/// </para>
/// </summary>
public sealed class ThemeImageFlattener
{
    /// <summary>
    /// 摊平规则版本：改了 <see cref="WashLayerPolicy"/> 的阈值或像素改法就 +1，让旧缓存失效。
    /// <para>注意：只加缓存层（复用落盘产物 / 否定标记）不需要 +1 —— 产物内容没变，不该白白重算一次。</para>
    /// </summary>
    private const int PolicyVersion = 1;

    /// <summary>判定纱层时最多采样这么多像素，避免 4K 图逐像素统计。</summary>
    private const int HistogramSampleTarget = 65_536;

    /// <summary>否定标记的文件后缀：存在即表示「这张原图不是纱层，不用再摊平」。</summary>
    private const string NegativeMarkerSuffix = ".noflat";

    private static readonly string[] RasterExtensions = [".png", ".jpg", ".jpeg", ".bmp", ".webp"];

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<string>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();

    public ThemeImageFlattener(IFileSystem fileSystem, IAppLogger logger, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        _fileSystem = fileSystem;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>同步查询：已摊平返回摊平后的路径，否则原样返回（给 x:Bind 这类同步绑定用）。</summary>
    /// <remarks>
    /// 除了查内存，还会探一次磁盘上的摊平产物 —— 这样即使本进程还没机会调
    /// <see cref="PrepareAsync"/>（例如卡片直接重建），也能立刻拿到上次冷启动留下的成品。
    /// 探测结果会写回内存，后续同步调用不再碰磁盘。
    /// </remarks>
    public string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsRasterImage(path))
        {
            return path;
        }

        lock (_gate)
        {
            if (_resolved.TryGetValue(path, out var known))
            {
                return known;
            }
        }

        // 内存未命中：探一次磁盘，命中就记住（正面产物或否定标记都算已知结论）。
        string resolved;
        try
        {
            var flatPath = CachePathFor(path);
            if (_fileSystem.FileExists(flatPath))
            {
                resolved = flatPath;
            }
            else if (_fileSystem.FileExists(NegativeMarkerPathFor(flatPath)))
            {
                resolved = path;
            }
            else
            {
                // 还没有结论：返回原图，稍后 PrepareAsync 会补上。
                return path;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return path;
        }

        lock (_gate)
        {
            _resolved[path] = resolved;
        }

        return resolved;
    }

    /// <summary>
    /// 准备一张图：纱层返回摊平副本路径，其它情况返回原路径。
    /// 同一路径并发调用只会解码一次。
    /// </summary>
    public async Task<string> PrepareAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsRasterImage(path))
        {
            return path ?? string.Empty;
        }

        Task<string> work;
        lock (_gate)
        {
            if (_resolved.TryGetValue(path, out var done))
            {
                return done;
            }

            if (!_inFlight.TryGetValue(path, out var running))
            {
                running = Task.Run(() => ResolveOrFlattenAsync(path, cancellationToken), cancellationToken);
                _inFlight[path] = running;
            }

            work = running;
        }

        string result;
        try
        {
            // 不用回 UI 线程：摊平全程是纯计算 + 磁盘写，落回 UI 只会把大图解码压在渲染线程上。
            result = await work.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // ResolveOrFlattenAsync 自己会兜异常，这里只是双保险。
            _logger.Warn("背景图摊平意外失败: " + path + " (" + ex.Message + ")");
            result = path;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(path);
                _resolved[path] = _resolved.TryGetValue(path, out var settled) ? settled : path;
            }
        }

        return result;
    }

    private static bool IsRasterImage(string path)
    {
        foreach (var extension in RasterExtensions)
        {
            if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 先看磁盘上的既有结论（摊平产物 / 否定标记），都没有才真正解码。
    /// <para>缓存路径由「原图路径 + 文件大小 + <see cref="PolicyVersion"/>」算出，是稳定的 ——
    /// 所以进程重启后能直接认定上次的产物，不必重算。</para>
    /// </summary>
    private async Task<string> ResolveOrFlattenAsync(string path, CancellationToken cancellationToken)
    {
        if (!_fileSystem.FileExists(path))
        {
            return path;
        }

        var flatPath = CachePathFor(path);
        if (_fileSystem.FileExists(flatPath))
        {
            lock (_gate)
            {
                _resolved[path] = flatPath;
            }

            return flatPath;
        }

        if (_fileSystem.FileExists(NegativeMarkerPathFor(flatPath)))
        {
            // 上次已经判定过「不是纱层」，直接用原图，省掉一次全图解码。
            lock (_gate)
            {
                _resolved[path] = path;
            }

            return path;
        }

        return await FlattenCoreAsync(path, flatPath, cancellationToken).ConfigureAwait(false);
    }

    private async Task<string> FlattenCoreAsync(string path, string flatPath, CancellationToken cancellationToken)
    {
        try
        {
            var bytes = await Task.Run(() => _fileSystem.ReadAllBytes(path), cancellationToken)
                .ConfigureAwait(false);
            if (bytes.Length == 0)
            {
                return path;
            }

            using var stream = await ToStreamAsync(bytes).ConfigureAwait(false);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0)
            {
                return path;
            }

            var pixels = await DecodePixelsAsync(decoder).ConfigureAwait(false);
            var expected = (long)width * height * 4;
            if (pixels.Length != expected)
            {
                _logger.Warn(
                    "背景图像素长度不符，跳过摊平: " + path + "（期望 " + expected + "，实际 " + pixels.Length + "）");
                return path;
            }

            var stride = Math.Max(1, (int)((long)width * height / HistogramSampleTarget));
            var histogram = WashLayerPolicy.BuildAlphaHistogram(pixels, stride);
            var sampledPixels = ((width * (long)height) + stride - 1) / stride;
            if (!WashLayerPolicy.IsWashLayer(histogram, sampledPixels))
            {
                // 记一个否定标记：下次进程启动不必再为大图解码一遍才发现「它不是纱层」。
                WriteNegativeMarker(flatPath);
                return path;
            }

            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = WashLayerPolicy.FlattenedAlpha;
            }

            var flatBytes = await EncodePngAsync(pixels, width, height, cancellationToken).ConfigureAwait(false);
            _fileSystem.EnsureDirectory(_cacheDirectory);
            _fileSystem.WriteAllBytesAtomic(flatPath, flatBytes);

            // 有了正面产物就把否定标记清掉，避免两者并存时判断顺序带来歧义。
            DeleteNegativeMarker(flatPath);

            lock (_gate)
            {
                _resolved[path] = flatPath;
            }

            _logger.Info("主题背景是纱层，已摊平为不透明图: " + path + " → " + flatPath);
            return flatPath;
        }
        catch (OperationCanceledException)
        {
            return path;
        }
        catch (Exception ex)
        {
            _logger.Warn("背景图摊平失败: " + path + " (" + ex.Message + ")");
            return path;
        }
    }

    private static string NegativeMarkerPathFor(string flatPath) => flatPath + NegativeMarkerSuffix;

    private void WriteNegativeMarker(string flatPath)
    {
        try
        {
            _fileSystem.EnsureDirectory(_cacheDirectory);
            _fileSystem.WriteAllTextAtomic(NegativeMarkerPathFor(flatPath), PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 否定标记只是省一次解码的优化，写不进去不影响正确性。
            _logger.Warn("写入摊平否定标记失败，已忽略: " + flatPath + " (" + ex.Message + ")");
        }
    }

    private void DeleteNegativeMarker(string flatPath)
    {
        try
        {
            var marker = NegativeMarkerPathFor(flatPath);
            if (_fileSystem.FileExists(marker))
            {
                _fileSystem.Delete(marker);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _logger.Warn("删除摊平否定标记失败，已忽略: " + flatPath + " (" + ex.Message + ")");
        }
    }

    private string CachePathFor(string path)
    {
        long size;
        try
        {
            size = _fileSystem.GetFileSize(path);
        }
        catch (IOException)
        {
            size = 0;
        }

        var stamp = path + "|" + size + "|flat-v" + PolicyVersion;
        var hashBytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(stamp));
        var hash = Convert.ToHexStringLower(hashBytes, 0, 4);
        var name = Path.GetFileNameWithoutExtension(path);
        var safeName = string.Join('_', name.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(_cacheDirectory, safeName + "-" + hash + ".png");
    }

    private static async Task<IRandomAccessStream> ToStreamAsync(byte[] bytes)
    {
        var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }
        stream.Seek(0);
        return stream;
    }

    private static async Task<byte[]> DecodePixelsAsync(BitmapDecoder decoder)
    {
        var provider = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            new BitmapTransform(),
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return provider.DetachPixelData();
    }

    private static async Task<byte[]> EncodePngAsync(
        byte[] bgraPixels,
        uint width,
        uint height,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, output);
        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Straight,
            width,
            height,
            96,
            96,
            bgraPixels);
        await encoder.FlushAsync();

        var length = (uint)output.Size;
        output.Seek(0);
        var buffer = new Windows.Storage.Streams.Buffer(length);
        await output.ReadAsync(buffer, length, InputStreamOptions.None);

        var data = new byte[buffer.Length];
        using var reader = DataReader.FromBuffer(buffer);
        reader.ReadBytes(data);
        return data;
    }
}
