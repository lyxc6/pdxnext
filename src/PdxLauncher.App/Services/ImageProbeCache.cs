namespace PdxLauncher.App.Services;

using System.Collections.Concurrent;

/// <summary>
/// 图片原始宽度的小缓存。
/// <para>
/// x:Bind 每次求值都会问「这张图多宽」，而 <see cref="ImagePixelSize.ReadWidth"/> 要开一次文件、
/// 读 16 KB 文件头。列表一页 30 行、翻页/刷新又各来一遍，这些同步 IO 全部落在 UI 线程上。
/// 同一路径的文件宽度不会变，记住即可。
/// </para>
/// <para>
/// 有上限但不淘汰：超过 <see cref="MaxEntries"/> 条后只是不再新增。图片路径数量有限，
/// 真到了上限说明一直在看新图，这时少缓存几条也不影响正确性。
/// </para>
/// </summary>
public static class ImageProbeCache
{
    /// <summary>最多缓存多少条路径。</summary>
    public const int MaxEntries = 512;

    private static readonly ConcurrentDictionary<string, int> Widths =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>取图片宽度；同一路径只真正读一次盘（读失败记 0，同样只失败一次）。</summary>
    /// <param name="path">图片路径。</param>
    /// <param name="probe">真正的探测函数，通常是 <see cref="ImagePixelSize.ReadWidth"/>。</param>
    /// <returns>像素宽度；探测不出来时为 0。</returns>
    public static int GetWidth(string path, Func<string, int> probe)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(probe);

        if (Widths.TryGetValue(path, out var cached))
        {
            return cached;
        }

        var width = probe(path);
        if (Widths.Count < MaxEntries)
        {
            Widths[path] = width;
        }

        return width;
    }

    /// <summary>清空缓存（换主题、原图被覆盖时调用）。</summary>
    public static void Clear() => Widths.Clear();
}
