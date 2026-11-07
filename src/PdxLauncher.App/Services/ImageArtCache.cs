namespace PdxLauncher.App.Services;

using System.Security.Cryptography;
using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Imaging;
/// <summary>
/// DDS 缩略图转 PNG 的显示缓存（WinUI 原生解不了 DDS）。
/// <para>
/// 命中规则：<c>&lt;exe&gt;\data\image-cache\&lt;hash&gt;.png</c>，hash = SHA-256(原图路径 + 文件大小 + 修改时间 + 规则版本) 前 16 hex。
/// 只有 <c>.dds</c> 会被处理——其它格式（png/jpg/webp）WinUI 自己能解，多转一道只会多占磁盘。
/// 解码走 <see cref="DdsImage"/>（纯托管），Paradox 缩略图的 alpha 通道是噪声，
/// 转 PNG 前统一 <see cref="DdsImage.ForceOpaque"/> 置为不透明。
/// </para>
/// <para>
/// 线程模型：<see cref="Resolve"/> 同步查内存字典（给 x:Bind 用）；
/// <see cref="EnsureAsync"/> 负责解码并落盘，同一路径并发只解码一次；失败只记 Warn 并返回 null。
/// </para>
/// </summary>
public sealed class ImageArtCache
{
    /// <summary>缓存规则版本：改了转码方式（例如换成别的 alpha 策略）就 +1，让旧缓存自然失效。</summary>
    private const int PolicyVersion = 1;

    /// <summary>超过这个大小的 DDS 不转（缩略图只有几百 KB，超过的基本是误判或非缩略图资源）。</summary>
    private const long MaxSourceBytes = 64L * 1024 * 1024;

    private const string SourceExtension = ".dds";

    /// <summary>缩略图解码上限：长边超过这个值就抽稀（显示尺寸远小于此）。</summary>
    private const int MaxDecodeDimension = 1024;

    /// <summary>解码并发上限：DDS 解码是 CPU 活，开太多只会抢 UI 线程的时间片。</summary>
    private const int MaxConcurrency = 2;

    /// <summary>
    /// 首屏最多同步等这么多张转完；剩下的丢后台慢慢转。
    /// 一屏列表大约 8–10 行，12 张足够铺满首屏，再多等只会拖慢打开速度。
    /// </summary>
    public const int EagerPrewarmCount = 12;

    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly string _cacheDirectory;
    private readonly Dictionary<string, string> _resolved = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Task<string?>> _inFlight = new(StringComparer.OrdinalIgnoreCase);
    private readonly Lock _gate = new();
    private readonly SemaphoreSlim _slots = new(MaxConcurrency, MaxConcurrency);

    public ImageArtCache(IFileSystem fileSystem, IAppLogger logger, string cacheDirectory)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);

        _fileSystem = fileSystem;
        _logger = logger;
        _cacheDirectory = cacheDirectory;
    }

    /// <summary>缓存目录（设置页统计/清理用）。</summary>
    public string CacheDirectory => _cacheDirectory;

    /// <summary>
    /// 同步查询已转好的 PNG 路径（给 x:Bind 这类同步绑定用）。
    /// 非 DDS 原样返回；DDS 但还没转好时返回 null（UI 显示占位，等 <see cref="PrewarmAsync"/> 之后再刷新）。
    /// </summary>
    public string? Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return path;
        }

        if (!IsDds(path))
        {
            return path;
        }

        lock (_gate)
        {
            if (_resolved.TryGetValue(path, out var ready))
            {
                return ready;
            }
        }

        // 进程重启后的第二次打开：只看缓存文件在不在，不重新解码。
        if (TryGetCachedPath(path, out var cached) && _fileSystem.FileExists(cached))
        {
            lock (_gate)
            {
                _resolved[path] = cached;
            }

            return cached;
        }

        return null;
    }

    /// <summary>
    /// 批量预热：列表页在构造行之前调用，保证 x:Bind 取 <see cref="Resolve"/> 时已经有 PNG。
    /// <para>
    /// 只同步等前 <see cref="EagerPrewarmCount"/> 张（刚好铺满首屏），其余交给后台按顺序转。
    /// 以前是把整页（几十上百张）一起解出来再建行，首屏要等所有缩略图转完才出来。
    /// 非 DDS、文件不存在、超过大小上限、解码失败都只是跳过，永不抛异常。
    /// </para>
    /// </summary>
    public async Task PrewarmAsync(IEnumerable<string?> paths, CancellationToken cancellationToken)
    {
        var pending = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && IsDds(path) && seen.Add(path))
            {
                pending.Add(path);
            }
        }

        if (pending.Count == 0)
        {
            return;
        }

        var eager = Math.Min(pending.Count, EagerPrewarmCount);
        var tasks = new List<Task<string?>>(eager);
        for (var index = 0; index < eager; index++)
        {
            tasks.Add(EnsureAsync(pending[index], cancellationToken));
        }

        // 预热只有文件 IO 与解码，没有 UI 属性赋值：不再回 UI 线程排队。
        await Task.WhenAll(tasks).ConfigureAwait(false);

        if (pending.Count > eager)
        {
            PrewarmRemainder(pending, eager, cancellationToken);
        }
    }

    /// <summary>
    /// 首屏之外的缩略图在后台补转：不阻塞列表出现，失败只记 Warn，取消时安静退出。
    /// 每张走 <see cref="EnsureAsync"/>，同一路径并发只解码一次，重复调用无副作用。
    /// </summary>
    private void PrewarmRemainder(List<string> pending, int start, CancellationToken cancellationToken)
    {
        var rest = pending.GetRange(start, pending.Count - start);

        // 后台任务自带完整 try/catch：这里的异常没人 await，不能让它把进程带崩。
        _ = Task.Run(
            async () =>
            {
                try
                {
                    foreach (var path in rest)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await EnsureAsync(path, cancellationToken).ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    // 用户已经离开列表页，剩下的不用转了。
                }
                catch (Exception exception)
                {
                    _logger.Warn("缩略图后台预热失败：" + exception.Message);
                }
            },
            CancellationToken.None);
    }

    /// <summary>确保一张 DDS 有可显示的 PNG；成功返回 PNG 路径，跳过或失败返回 null。</summary>
    public Task<string?> EnsureAsync(string? path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsDds(path))
        {
            return Task.FromResult<string?>(null);
        }

        Task<string?> work;
        lock (_gate)
        {
            if (_resolved.TryGetValue(path, out var done))
            {
                return Task.FromResult<string?>(done);
            }

            if (!_inFlight.TryGetValue(path, out var running))
            {
                running = ConvertCoreAsync(path, cancellationToken);
                _inFlight[path] = running;
            }

            work = running;
        }

        return work;
    }

    /// <summary>清空内存字典（设置页「清空图片缓存」之后调用，避免继续引用已删的 PNG）。</summary>
    public void Reset()
    {
        lock (_gate)
        {
            _resolved.Clear();
        }
    }

    private static bool IsDds(string path) => path.EndsWith(SourceExtension, StringComparison.OrdinalIgnoreCase);

    private async Task<string?> ConvertCoreAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!_fileSystem.FileExists(path))
            {
                return null;
            }

            if (TryGetCachedPath(path, out var cached) && _fileSystem.FileExists(cached))
            {
                Remember(path, cached);
                return cached;
            }

            long length = 0;
            try
            {
                length = _fileSystem.GetFileSize(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("DDS 缩略图读不到大小，跳过转码: " + path + " (" + ex.Message + ")");
                return null;
            }

            if (length <= 0 || length > MaxSourceBytes)
            {
                _logger.Warn("DDS 缩略图大小异常，跳过转码: " + path + "（" + length + " 字节）");
                return null;
            }

            await _slots.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                // 解码、编码、建目录、落盘全部留在同一个线程池任务里：这几步都是纯文件 IO，
                // 以前最后两步跑在 UI 线程上，一页 30 张缩略图就能把消息泵按住好几秒。
                // Remember 只往 _resolved 写一条（lock _gate），线程安全，不需要回 UI 线程。
                var png = await Task.Run(
                    () =>
                    {
                        var decoded = DdsImage.DecodeFile(path, MaxDecodeDimension);
                        if (!decoded.IsSuccess)
                        {
                            _logger.Warn("DDS 缩略图解码失败，跳过: " + path + "（" + decoded.Error + "）");
                            return null;
                        }

                        var bytes = DdsImage.EncodePng(DdsImage.ForceOpaque(decoded.Pixels));
                        if (bytes.Length == 0)
                        {
                            return null;
                        }

                        _fileSystem.EnsureDirectory(_cacheDirectory);
                        _fileSystem.WriteAllBytesAtomic(cached, bytes);
                        Remember(path, cached);
                        return bytes;
                    },
                    cancellationToken).ConfigureAwait(false);

                if (png is null || png.Length == 0)
                {
                    return null;
                }

                _logger.Info("DDS 缩略图已转 PNG: " + path + " → " + cached);
                return cached;
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            _logger.Warn("DDS 缩略图转码失败，跳过: " + path + " (" + ex.Message + ")");
            return null;
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(path);
            }
        }
    }

    private void Remember(string path, string cached)
    {
        lock (_gate)
        {
            _resolved[path] = cached;
        }
    }

    /// <summary>
    /// 缓存文件名只由「路径 + 文件大小 + 规则版本」决定：原图被替换且大小变了会落到新文件名，
    /// 不会读到旧图；大小不变的原地覆盖属于极端情况，靠 <see cref="PolicyVersion"/> 兜底。
    /// </summary>
    private bool TryGetCachedPath(string path, out string cached)
    {
        cached = string.Empty;
        try
        {
            var key = string.Concat(
                path.ToLowerInvariant(),
                "|",
                _fileSystem.GetFileSize(path).ToString(System.Globalization.CultureInfo.InvariantCulture),
                "|",
                PolicyVersion.ToString(System.Globalization.CultureInfo.InvariantCulture));
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
            cached = Path.Combine(_cacheDirectory, Convert.ToHexStringLower(hash.AsSpan(0, 8)) + ".png");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
