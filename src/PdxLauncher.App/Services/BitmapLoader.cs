namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using PdxLauncher.Core.Abstractions;
using Windows.Storage.Streams;

/// <summary>
/// 从本地文件异步加载图片（位图走 <see cref="BitmapImage"/>，.svg 走 <see cref="SvgImageSource"/>）。
/// 文件读取放在后台线程（Task.Run），解码走 SetSourceAsync，全程不阻塞 UI 线程；
/// 调用方可以先用占位底色，加载完成后再替换。
/// </summary>
public sealed class BitmapLoader
{
    private const int MaxCacheEntries = 64;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly ThemeImageFlattener? _flattener;
    private readonly Dictionary<string, ImageSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BitmapLoader(IFileSystem fileSystem, IAppLogger logger, ThemeImageFlattener? flattener = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _flattener = flattener;
    }

    /// <summary>加载图片；路径为空/文件缺失/解码失败时返回 null。不抛异常。</summary>
    public async Task<ImageSource?> LoadAsync(
        string? path,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!_fileSystem.FileExists(path))
            {
                _logger.Warn("图片不存在: " + path);
                return null;
            }

            // 纱层主题图先摊平成不透明副本（EU4 背景整幅 alpha=20%，直接画就是一片灰）。
            if (_flattener is not null)
            {
                var flattened = await _flattener.PrepareAsync(path, cancellationToken).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(flattened)
                    && !string.Equals(flattened, path, StringComparison.OrdinalIgnoreCase)
                    && _fileSystem.FileExists(flattened))
                {
                    path = flattened;
                }
            }

            var cacheKey = path + "|" + decodePixelWidth;
            if (_cache.TryGetValue(cacheKey, out var cached))
            {
                return cached;
            }

            var bytes = await Task.Run(() => _fileSystem.ReadAllBytes(path), cancellationToken).ConfigureAwait(true);
            if (bytes.Length == 0)
            {
                _logger.Warn("图片是空文件: " + path);
                return null;
            }

            var image = await CreateImageAsync(bytes, path, decodePixelWidth, cancellationToken)
                .ConfigureAwait(true);
            if (image is null)
            {
                return null;
            }

            if (_cache.Count >= MaxCacheEntries)
            {
                _cache.Clear();
            }

            _cache[cacheKey] = image;
            return image;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _logger.Warn("图片加载失败: " + path + " (" + ex.Message + ")");
            return null;
        }
    }

    /// <summary>清空缓存（切换游戏主题时调用，避免旧主题图片占内存）。</summary>
    public void ClearCache() => _cache.Clear();

    private static async Task<ImageSource?> CreateImageAsync(
        byte[] bytes,
        string path,
        int decodePixelWidth,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new InMemoryRandomAccessStream();
        using (var writer = new DataWriter(stream.GetOutputStreamAt(0)))
        {
            writer.WriteBytes(bytes);
            await writer.StoreAsync();
            await writer.FlushAsync();
            writer.DetachStream();
        }

        stream.Seek(0);

        // CK3 的字标是 logo.svg，BitmapImage 不支持 SVG，必须走 SvgImageSource。
        if (path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            var svg = new SvgImageSource();
            await svg.SetSourceAsync(stream);
            return svg;
        }

        var bitmap = new BitmapImage();
        if (decodePixelWidth > 0)
        {
            bitmap.DecodePixelWidth = decodePixelWidth;
        }

        await bitmap.SetSourceAsync(stream);
        return bitmap;
    }
}
