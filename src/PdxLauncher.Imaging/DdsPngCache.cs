using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace PdxLauncher.Imaging;

/// <summary>
/// DDS → PNG 的磁盘缓存：按相对路径落盘，目标文件已存在时直接命中，绝不重复解码。
/// <para>
/// 成就页的 CK3 图标只有 DDS（198 条 × 两种解锁态），而 WinUI 的 <c>BitmapImage</c> 不认 DDS。
/// 这里负责「第一次访问时解码一次、之后都读 PNG」，让页面只在行容器实化时付一次解码成本；
/// 生成失败的源文件返回 <see langword="null"/>，由界面退回占位方块，不抛异常。
/// </para>
/// <para>
/// 命中缓存前会校验 PNG 是否完整（签名 / 宽高 / IEND）；缓存被写成 0 字节或截断时
/// 删除并重建一次，避免一个坏文件把图标永久钉死成空白。
/// </para>
/// <para>
/// 目标路径必须落在构造时给的缓存目录内：调用方传的是
/// <c>&lt;appid&gt;\&lt;apiName&gt;[_locked].png</c> 这样的相对路径，
/// 含 <c>..</c>、盘符或绝对路径一律拒绝，避免坏 id 把文件写到缓存目录之外。
/// </para>
/// </summary>
public sealed class DdsPngCache
{
    /// <summary>一个合法 PNG 至少要有签名 + IHDR + 一段数据 + IEND 的字节数。</summary>
    private const int MinimumPngLength = 64;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private readonly string _directory;

    /// <summary>
    /// 进程内按目标文件串行化：六个线程同时读同一行时只允许一个解码，
    /// 其余线程等它写完后直接命中，避免并发改名互相覆盖导致有人拿到 <see langword="null"/>。
    /// 键是已经过 <see cref="TryCreateTargetPath"/> 校验的绝对路径，数量上界是图标总数（CK3 约 400）。
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _targetLocks = new(StringComparer.OrdinalIgnoreCase);

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
    /// <remarks>已有缓存但校验不通过时会重建；重建仍失败则删掉坏文件并返回 <see langword="null"/>，下次访问再试。</remarks>
    public string? Ensure(string sourcePath, string relativeTargetPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath) || !TryCreateTargetPath(relativeTargetPath, out var targetPath))
        {
            return null;
        }

        // 同一目标文件在进程内只让一个线程解码；等待者拿到锁后会走「已存在且有效」的命中分支。
        lock (_targetLocks.GetOrAdd(targetPath, static _ => new object()))
        {
            return EnsureLocked(sourcePath, targetPath);
        }
    }

    private string? EnsureLocked(string sourcePath, string targetPath)
    {
        try
        {
            // 已经生成过就不再碰 DDS：这是「第二次进页面全部命中」的关键路径。
            // 但命中前必须验一下文件本身：缓存可能被外部写成 0 字节或被截断，
            // 直接返回会让这个图标永久空白，且此后每次都被当成命中。
            if (File.Exists(targetPath))
            {
                if (IsUsablePng(targetPath))
                {
                    return targetPath;
                }

                TryDeleteCachedPng(targetPath);
            }

            Interlocked.Increment(ref _decodeCount);
            if (DdsImage.TryConvertFileToPng(sourcePath, targetPath, MaxDimension) && IsUsablePng(targetPath))
            {
                return targetPath;
            }

            // 另一个进程可能刚好写完有效文件：复用它的成果，而不是删掉重来。
            if (File.Exists(targetPath))
            {
                if (IsUsablePng(targetPath))
                {
                    return targetPath;
                }

                // 重建仍失败：删掉坏文件，让下一次访问重新尝试，而不是留下永久坏缓存。
                TryDeleteCachedPng(targetPath);
            }

            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    /// <summary>
    /// 校验缓存文件是不是能交给 <c>BitmapImage</c> 的完整 PNG：
    /// 长度 ≥ 64、签名正确、IHDR 宽高都大于 0、并以 IEND 收尾。
    /// <para>只验签名不会发现「IHDR 合法但数据截断」的半截文件，所以必须同时验 IEND。</para>
    /// </summary>
    private static bool IsUsablePng(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists || file.Length < MinimumPngLength)
            {
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);

            Span<byte> header = stackalloc byte[24];
            if (stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
            {
                return false;
            }

            if (!header[..8].SequenceEqual(PngSignature))
            {
                return false;
            }

            // IHDR 必须是第一个块：长度 13，类型 "IHDR"，随后是大端宽高。
            if (BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) != 13
                || header[12] != (byte)'I'
                || header[13] != (byte)'H'
                || header[14] != (byte)'D'
                || header[15] != (byte)'R')
            {
                return false;
            }

            if (BinaryPrimitives.ReadInt32BigEndian(header[16..20]) <= 0
                || BinaryPrimitives.ReadInt32BigEndian(header[20..24]) <= 0)
            {
                return false;
            }

            // IEND：长度 0 + "IEND" + 固定 CRC 0xAE426082。
            Span<byte> tail = stackalloc byte[8];
            stream.Seek(-8, SeekOrigin.End);
            if (stream.ReadAtLeast(tail, tail.Length, throwOnEndOfStream: false) < tail.Length)
            {
                return false;
            }

            return tail[0] == (byte)'I'
                && tail[1] == (byte)'E'
                && tail[2] == (byte)'N'
                && tail[3] == (byte)'D'
                && BinaryPrimitives.ReadUInt32BigEndian(tail[4..]) == 0xAE426082;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static void TryDeleteCachedPng(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException
            or NotSupportedException or PathTooLongException)
        {
            // 删不掉也无妨：调用方还是会重新生成并覆盖它。
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
