namespace PdxLauncher.App.Services;

using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

/// <summary>
/// 图片路径（string?）→ ImageSource 的 XAML 辅助函数。
/// 返回的 BitmapImage 由框架在后台线程解码，不会阻塞 UI 线程；
/// 需要"先占位再替换"的场景请改用 <see cref="BitmapLoader"/>。
/// <para>
/// 若该路径已被 <see cref="ThemeImageFlattener"/> 摊平（EU4 的纱层背景），这里返回摊平副本；
/// 摊平是异步且只做一次的，卡片应在重建前先调 <c>AppServices.PrepareCardCoversAsync</c>。
/// </para>
/// <para>
/// 列表/详情页的图必须传 <paramref name="displayWidthDip"/>：按显示尺寸解码，
/// 一张 1900px 的封面画在 48px 格子里时只解 60px，省下的是一张图 14MB 的差距。
/// 解码结果进一个 32MB 预算的共享 LRU，翻页来回滚动不会重复解码。
/// </para>
/// </summary>
public static class XamlImage
{
    /// <summary>共享解码缓存的预算。单张 1024px 图约 4MB，够放七八张，再多就淘汰最旧的。</summary>
    private const long CacheBudgetBytes = 32L * 1024 * 1024;

    private static readonly DecodeCache Cache = new(CacheBudgetBytes);

    /// <summary>路径为空、文件不存在或创建失败时返回 null（XAML 侧显示占位）。</summary>
    /// <param name="path">图片路径。</param>
    /// <param name="displayWidthDip">控件上实际占用的逻辑像素宽度；传 0 表示按原图解码。</param>
    public static ImageSource? FromPath(string? path, double displayWidthDip)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            if (!AppServices.IsInitialized || !AppServices.FileSystem.FileExists(path))
            {
                return null;
            }

            // 已摊平过的纱层背景（EU4 那张整幅 alpha=20% 的油画）优先用摊平副本。
            var resolved = AppServices.ThemeImages.Resolve(path) ?? path;
            if (!string.Equals(resolved, path, StringComparison.OrdinalIgnoreCase)
                && AppServices.FileSystem.FileExists(resolved))
            {
                path = resolved;
            }

            // DDS（EU5 的 DLC/mod 缩略图）WinUI 解不了：换成转好的 PNG；还没转好就返回 null 显示占位。
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase))
            {
                var png = AppServices.ImageArt.Resolve(path);
                if (string.IsNullOrWhiteSpace(png) || !AppServices.FileSystem.FileExists(png))
                {
                    return null;
                }

                path = png;
            }

            int decodeWidth = ImageDecodeSize.Compute(displayWidthDip, DisplayScale, ImagePixelSize.ReadWidth(path));
            return Cache.GetOrCreate(path, decodeWidth);
        }
        catch (UriFormatException ex)
        {
            LogWarn("图片路径非法: " + path + " (" + ex.Message + ")");
            return null;
        }
        catch (ArgumentException ex)
        {
            LogWarn("图片路径非法: " + path + " (" + ex.Message + ")");
            return null;
        }
    }

    /// <summary>清空解码缓存（切换游戏主题时调用）。</summary>
    public static void ClearCache() => Cache.Clear();

    /// <summary>屏幕缩放；窗口还没建好或拿不到 XamlRoot 时按 100% 处理。</summary>
    private static double DisplayScale
    {
        get
        {
            if (App.MainWindowInstance?.Content is FrameworkElement root)
            {
                double scale = root.XamlRoot?.RasterizationScale ?? 1.0;
                return scale > 0 ? scale : 1.0;
            }

            return 1.0;
        }
    }

    private static BitmapImage Create(string path, int decodeWidth)
    {
        var image = new BitmapImage { UriSource = new Uri(path) };
        if (decodeWidth > 0)
        {
            image.DecodePixelWidth = decodeWidth;
        }

        return image;
    }

    private static void LogWarn(string message)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Warn(message);
        }
    }

    /// <summary>
    /// 按「路径 + 解码宽度」索引的 LRU：命中挪到队首，超预算从队尾淘汰。
    /// 至少保留一条，避免单张超预算的大图被立刻踢掉、下一帧又重新解码。
    /// </summary>
    private sealed class DecodeCache
    {
        private readonly Dictionary<string, LinkedListNode<Entry>> _index = new(StringComparer.OrdinalIgnoreCase);
        private readonly LinkedList<Entry> _lru = new();
        private readonly long _budgetBytes;
        private long _bytes;

        public DecodeCache(long budgetBytes)
        {
            _budgetBytes = budgetBytes;
        }

        public BitmapImage GetOrCreate(string path, int decodeWidth)
        {
            string key = path + "|" + decodeWidth.ToString(CultureInfo.InvariantCulture);
            if (_index.TryGetValue(key, out var existing))
            {
                _lru.Remove(existing);
                _lru.AddFirst(existing);
                return existing.Value.Image;
            }

            var image = Create(path, decodeWidth);
            var entry = new Entry(key, image, EstimateBytes(decodeWidth));
            var node = _lru.AddFirst(entry);
            _index[key] = node;
            _bytes += entry.Bytes;
            Trim();
            return image;
        }

        public void Clear()
        {
            _index.Clear();
            _lru.Clear();
            _bytes = 0;
        }

        /// <summary>解码后占多少内存：ARGB 每像素 4 字节，宽度未知时按 4MB 记一笔。</summary>
        private static long EstimateBytes(int decodeWidth)
        {
            return decodeWidth > 0 ? 4L * decodeWidth * decodeWidth : 4L * 1024 * 1024;
        }

        private void Trim()
        {
            while (_bytes > _budgetBytes && _lru.Count > 1)
            {
                var last = _lru.Last;
                if (last is null)
                {
                    return;
                }

                _lru.RemoveLast();
                _index.Remove(last.Value.Key);
                _bytes -= last.Value.Bytes;
            }
        }

        private sealed record Entry(string Key, BitmapImage Image, long Bytes);
    }
}
