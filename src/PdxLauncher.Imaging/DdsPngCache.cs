namespace PdxLauncher.Imaging;

/// <summary>
/// DDS → PNG 的磁盘缓存：按相对路径落盘，目标文件已存在时直接命中，绝不重复解码。
/// <para>
/// 成就页的 CK3 图标只有 DDS（198 条 × 两种解锁态），而 WinUI 的 <c>BitmapImage</c> 不认 DDS。
/// 这里负责「第一次访问时解码一次、之后都读 PNG」，让页面只在行容器实化时付一次解码成本；
/// 生成失败的源文件返回 <see langword="null"/>，由界面退回占位方块，不抛异常。
/// </para>
/// <para>
/// 目标路径必须落在构造时给的缓存目录内：调用方传的是
/// <c>&lt;appid&gt;\&lt;apiName&gt;[_locked].png</c> 这样的相对路径，
/// 含 <c>..</c>、盘符或绝对路径一律拒绝，避免坏 id 把文件写到缓存目录之外。
/// </para>
/// </summary>
public sealed class DdsPngCache
{
    private readonly string _directory;

    private int _decodeCount;

    /// <summary>构造。</summary>
    /// <param name="directory">缓存根目录，通常是 <c>AppServices.DataRoot\achievement-icons</c>。</param>
    /// <param name="maxDimension">大于 0 时把长边缩到该值（只缩不放）；0 保持原尺寸。</param>
    public DdsPngCache(string directory, int maxDimension = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        ArgumentOutOfRangeException.ThrowIfNegative(maxDimension);

        _directory = directory;
        MaxDimension = maxDimension;
    }

    /// <summary>缓存根目录。</summary>
    public string DirectoryPath => _directory;

    /// <summary>长边上限（像素）；0 表示保持原尺寸。</summary>
    public int MaxDimension { get; }

    /// <summary>发生过的解码尝试次数（命中缓存不增加）；自检与性能取样用。</summary>
    public int DecodeCount => Volatile.Read(ref _decodeCount);

    /// <summary>
    /// 确保 <paramref name="sourcePath"/> 对应的 PNG 存在并返回其绝对路径。
    /// </summary>
    /// <param name="sourcePath">DDS 源文件绝对路径。</param>
    /// <param name="relativeTargetPath">缓存根目录下的相对路径，例如 <c>1142830\ach_x_locked.png</c>。</param>
    /// <returns>可交给 <c>BitmapImage</c> 的 PNG 路径；源文件损坏 / 解码失败 / 路径非法时返回 <see langword="null"/>。</returns>
    public string? Ensure(string sourcePath, string relativeTargetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !TryCreateTargetPath(relativeTargetPath, out var targetPath))
        {
            return null;
        }

        try
        {
            // 已经生成过就不再碰 DDS：这是「第二次进页面全部命中」的关键路径。
            if (File.Exists(targetPath))
            {
                return targetPath;
            }

            Interlocked.Increment(ref _decodeCount);
            return DdsImage.TryConvertFileToPng(sourcePath, targetPath, MaxDimension) ? targetPath : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private bool TryCreateTargetPath(string relativeTargetPath, out string targetPath)
    {
        targetPath = string.Empty;
        if (string.IsNullOrWhiteSpace(relativeTargetPath) || relativeTargetPath.Length > 240)
        {
            return false;
        }

        if (!relativeTargetPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            if (Path.IsPathRooted(relativeTargetPath)
                || relativeTargetPath.IndexOf(':') >= 0)
            {
                return false;
            }

            foreach (var segment in relativeTargetPath.Split(['/', '\\']))
            {
                if (segment.Length == 0
                    || string.Equals(segment, ".", StringComparison.Ordinal)
                    || string.Equals(segment, "..", StringComparison.Ordinal)
                    || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return false;
                }
            }

            var root = Path.GetFullPath(_directory);
            var candidate = Path.GetFullPath(Path.Combine(root, relativeTargetPath));
            var rootWithSeparator = Path.EndsInDirectorySeparator(root) ? root : root + Path.DirectorySeparatorChar;
            if (!candidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            targetPath = candidate;
            return true;
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }
}
