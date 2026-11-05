namespace PdxLauncher.Core.Achievements;

using System.Text.Json;
using System.Text.Json.Serialization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就快照的磁盘缓存：<c>&lt;data&gt;\achievement-cache\&lt;appid&gt;.json</c>。
/// <para>
/// 进页面先把上次的快照渲染出来，再去后台跑 Steam 宿主子进程；这样冷启动不必等
/// 1 秒级的回调。写入走 <see cref="IFileSystem.WriteAllTextAtomic"/>，刷新失败时旧缓存仍然完整。
/// </para>
/// <para>
/// 读取是「尽最大努力」：文件缺失、坏 JSON、版本不符、类型错误都返回 <c>null</c>，
/// 由调用方退回空列表 + 重新取数；绝不把损坏的缓存抛给页面。
/// </para>
/// </summary>
public sealed class AchievementSnapshotCache
{
    /// <summary>缓存格式版本；结构变化时加一，旧文件会被当成未命中。</summary>
    public const int SchemaVersion = 1;

    private const string FileExtension = ".json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IFileSystem _fileSystem;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统抽象（写操作为 tmp+rename 原子写）。</param>
    /// <param name="directory">缓存目录，通常是 <c>AppServices.DataRoot\achievement-cache</c>。</param>
    public AchievementSnapshotCache(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = directory;
    }

    /// <summary>缓存根目录。</summary>
    public string DirectoryPath { get; }

    /// <summary>某个游戏对应的缓存文件路径；key 非法时返回 <c>null</c>（此时不读也不写）。</summary>
    public string? PathFor(string cacheKey)
    {
        if (!IsSafeKey(cacheKey))
        {
            return null;
        }

        return Path.Combine(DirectoryPath, cacheKey + FileExtension);
    }

    /// <summary>
    /// 读取缓存快照。文件不存在 / 内容损坏 / 版本不符都返回 <see langword="null"/>。
    /// </summary>
    public AchievementSnapshot? TryLoad(string cacheKey)
    {
        var path = PathFor(cacheKey);
        if (path is null)
        {
            return null;
        }

        try
        {
            if (!_fileSystem.FileExists(path))
            {
                return null;
            }

            var document = JsonSerializer.Deserialize<CacheDocument>(_fileSystem.ReadAllText(path), JsonOptions);
            if (document is null
                || document.Version != SchemaVersion
                || document.Snapshot is null
                || document.Snapshot.FetchedAtUtc == default)
            {
                return null;
            }

            return document.Snapshot;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            // 坏缓存等于没缓存：保留文件让下次刷新覆盖，页面继续走正常取数。
            return null;
        }
    }

    /// <summary>
    /// 原子写回快照。失败只返回 false（磁盘满 / 无权限），由调用方记日志，不影响本次渲染。
    /// </summary>
    public bool TrySave(string cacheKey, AchievementSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var path = PathFor(cacheKey);
        if (path is null)
        {
            return false;
        }

        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                _fileSystem.EnsureDirectory(directory);
            }

            var document = new CacheDocument
            {
                Version = SchemaVersion,
                SavedAtUtc = DateTimeOffset.UtcNow,
                Snapshot = snapshot,
            };
            _fileSystem.WriteAllTextAtomic(path, JsonSerializer.Serialize(document, JsonOptions));
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>缓存键就是 appid（或 gameId），只允许单段文件名，避免 <c>..</c> 跑出缓存目录。</summary>
    private static bool IsSafeKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length > 64)
        {
            return false;
        }

        foreach (var c in key)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('.' or '_' or '-'))
            {
                return false;
            }
        }

        return key is not "." and not "..";
    }

    private sealed record CacheDocument
    {
        public int Version { get; init; }

        public DateTimeOffset SavedAtUtc { get; init; }

        public AchievementSnapshot? Snapshot { get; init; }
    }
}
