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
/// </summary>
public sealed class ThemeImageFlattener
{
    /// <summary>摊平规则版本：改了 <see cref="WashLayerPolicy"/> 的阈值就 +1，让旧缓存失效。</summary>
    private const int PolicyVersion = 1;

    /// <summary>判定纱层时最多采样这么多像素，避免 4K 图逐像素统计。</summary>
    private const int HistogramSampleTarget = 65_536;

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
    public string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        lock (_gate)
        {
            return _resolved.TryGetValue(path, out var flat) ? flat : path;
        }
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
                running = FlattenCoreAsync(path, cancellationToken);
                _inFlight[path] = running;
            }

            work = running;
        }

        string result;
        try
        {
            result = await work.ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            // FlattenCoreAsync 自己会兜异常，这里只是双保险。
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

    private async Task<string> FlattenCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                return path;
            }

            var bytes = await Task.Run(() => _fileSystem.ReadAllBytes(path), cancellationToken)
                .ConfigureAwait(true);
            if (bytes.Length == 0)
            {
                return path;
            }

            using var stream = await ToStreamAsync(bytes).ConfigureAwait(true);
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var width = decoder.PixelWidth;
            var height = decoder.PixelHeight;
            if (width == 0 || height == 0)
            {
                return path;
            }

            var pixels = await DecodePixelsAsync(decoder).ConfigureAwait(true);
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
                return path;
            }

            for (var i = 3; i < pixels.Length; i += 4)
            {
                pixels[i] = WashLayerPolicy.FlattenedAlpha;
            }

            var flatBytes = await EncodePngAsync(pixels, width, height, cancellationToken).ConfigureAwait(true);
            var flatPath = CachePathFor(path);
            _fileSystem.EnsureDirectory(_cacheDirectory);
            _fileSystem.WriteAllBytesAtomic(flatPath, flatBytes);

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
