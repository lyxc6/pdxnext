namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 工坊筛选目录的磁盘缓存：<c>&lt;data&gt;\workshop-filters\&lt;appid&gt;.json</c>。
/// <para>
/// 结构照 <see cref="PdxLauncher.Core.Achievements.AchievementSnapshotCache"/>：
/// 原子写、版本号、坏文件按未命中处理。文件里存的是私有 DTO 而不是领域 record，
/// 避免 <c>IReadOnlyList</c> 这类接口属性在反序列化时出意外。
/// </para>
/// </summary>
public sealed class WorkshopFilterCatalogCache
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
    };

    private readonly IFileSystem _fileSystem;

    /// <summary>构造。</summary>
    /// <param name="fileSystem">文件系统抽象（写操作为 tmp+rename 原子写）。</param>
    /// <param name="directory">缓存目录，通常是 <c>AppServices.WorkshopFilterRoot</c>。</param>
    public WorkshopFilterCatalogCache(IFileSystem fileSystem, string directory)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = directory;
    }

    /// <summary>缓存根目录。</summary>
    public string DirectoryPath { get; }

    /// <summary>某个 app 的缓存文件路径；appid 非法时返回 <c>null</c>。</summary>
    /// <param name="appId">Steam appid。</param>
    public string? PathFor(int appId) => appId > 0
        ? Path.Combine(DirectoryPath, appId.ToString(CultureInfo.InvariantCulture) + FileExtension)
        : null;

    /// <summary>
    /// 读取快照。文件不存在 / 内容损坏 / 版本不符 / appid 不匹配都返回 <see langword="null"/>。
    /// </summary>
    /// <param name="appId">Steam appid。</param>
    public WorkshopFilterCatalogSnapshot? TryLoad(int appId)
    {
        var path = PathFor(appId);
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
                || document.AppId != appId
                || document.FetchedAtUtc == default
                || document.Groups is not { Count: > 0 })
            {
                return null;
            }

            var groups = document.Groups
                .OfType<CacheGroup>()
                .Where(static group => group.Tags is { Count: > 0 })
                .Select(static group => new SteamWorkshopFilterGroup(
                    group.Key ?? string.Empty,
                    group.Title ?? string.Empty,
                    group.Tags!
                        .OfType<CacheTag>()
                        .Where(static tag => !string.IsNullOrWhiteSpace(tag.Tag))
                        .Select(static tag => new SteamWorkshopTag(
                            tag.Tag!,
                            string.IsNullOrWhiteSpace(tag.DisplayName) ? tag.Tag! : tag.DisplayName!))
                        .ToList(),
                    group.IsDlc))
                .Where(static group => group.Tags.Count > 0)
                .ToList();

            if (groups.Count == 0)
            {
                return null;
            }

            return new WorkshopFilterCatalogSnapshot(
                document.AppId,
                document.GameVersion ?? string.Empty,
                document.FetchedAtUtc,
                groups);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException
            or NotSupportedException or ArgumentException)
        {
            // 坏缓存等于没缓存：保留文件让下次刷新覆盖。
            return null;
        }
    }

    /// <summary>原子写回快照；失败只返回 false（磁盘满 / 无权限），不影响本次渲染。</summary>
    /// <param name="snapshot">快照。</param>
    public bool TrySave(WorkshopFilterCatalogSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.AppId <= 0 || snapshot.Groups.Count == 0)
        {
            return false;
        }

        var path = PathFor(snapshot.AppId);
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
                AppId = snapshot.AppId,
                GameVersion = snapshot.GameVersion,
                FetchedAtUtc = snapshot.FetchedAtUtc,
                Groups = snapshot.Groups
                    .Select(static group => new CacheGroup
                    {
                        Key = group.Key,
                        Title = group.Title,
                        IsDlc = group.IsDlc,
                        Tags = group.Tags
                            .Select(static tag => new CacheTag
                            {
                                Tag = tag.Tag,
                                DisplayName = tag.DisplayName,
                            })
                            .ToList(),
                    })
                    .ToList(),
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

    private sealed class CacheDocument
    {
        public int Version { get; set; }

        public DateTimeOffset SavedAtUtc { get; set; }

        public int AppId { get; set; }

        public string? GameVersion { get; set; }

        public DateTimeOffset FetchedAtUtc { get; set; }

        public List<CacheGroup>? Groups { get; set; }
    }

    private sealed class CacheGroup
    {
        public string? Key { get; set; }

        public string? Title { get; set; }

        public bool IsDlc { get; set; }

        public List<CacheTag>? Tags { get; set; }
    }

    private sealed class CacheTag
    {
        public string? Tag { get; set; }

        public string? DisplayName { get; set; }
    }
}
