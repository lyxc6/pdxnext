namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

/// <summary>
/// 创意工坊详情的图片落地：封面与截图都放进 <see cref="WorkshopInfoCache.CoverRoot"/> 下，
/// 已存在就直接复用，不重复下载。列表页与详情子页共用这一处规则，路径才不会各写一套。
/// <para>
/// 封面沿用缓存条目里的记账（<c>SetCover</c> / <c>TryGetCachedCover</c>），文件名是
/// <c>{来源}/{id}</c>；截图没有缓存字段，按「来源_<c>id</c>」建子目录、文件名
/// <c>shot_{URL 的 FNV-1a 32 位哈希}</c>，靠枚举同名前缀复认。
/// </para>
/// <para>
/// <b>同一个 URL 只落一份</b>：Steam 详情页的「画廊首图」经常就是封面本身，而顶部图片区
/// 与正文配图都走 <see cref="EnsureScreenshotAsync"/>。所以下载前先问一句「这个 URL 是不是
/// 已经在封面槽位里」——是就直接复用封面路径，不再往 <c>shots\</c> 里存第二份。
/// </para>
/// </summary>
public sealed class WorkshopImageCache
{
    /// <summary>截图子目录名（与封面同一根，便于整体清理/迁移）。</summary>
    private const string ShotsDirectoryName = "shots";

    private readonly IFileSystem _files;
    private readonly IAssetDownloader _assets;
    private readonly WorkshopInfoCache _info;
    private readonly CoverDiskQuota? _quota;

    /// <summary>
    /// 同一张图并发落地时只下载一次。实测触发点：详情页顶部图片墙与正文配图
    /// （<see cref="EnsureScreenshotAsync"/>）会同时请求同一 URL，两个任务抢着写同一个 <c>.part</c>。
    /// </summary>
    private readonly RequestCoalescer _coalesce = new();

    /// <summary>构造。</summary>
    /// <param name="files">文件系统抽象。</param>
    /// <param name="assets">下载器。</param>
    /// <param name="info">工坊信息缓存（提供缓存根与封面记账）。</param>
    /// <param name="quota">封面目录配额；下载成功后安排一轮清理，null 表示不清理（自检用）。</param>
    public WorkshopImageCache(
        IFileSystem files,
        IAssetDownloader assets,
        WorkshopInfoCache info,
        CoverDiskQuota? quota = null)
    {
        _files = files;
        _assets = assets;
        _info = info;
        _quota = quota;
    }

    /// <summary>本地已存在的封面路径；没有则返回 null。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    public string? GetCachedCover(WorkshopSource source, string id)
    {
        var key = WorkshopInfoCache.Key(source, id);
        if (!_info.TryGetCachedCover(key, out var path) || string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return _files.FileExists(path) ? path : null;
    }

    /// <summary>确保封面在本地；成功返回本地路径。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="url">封面地址；为空直接返回 null。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task<string?> EnsureCoverAsync(
        WorkshopSource source,
        string id,
        string? url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<string?>(null);
        }

        var key = "cover:" + WorkshopInfoCache.Key(source, id) + "|" + url;
        return _coalesce.RunAsync(key, ct => EnsureCoverCoreAsync(source, id, url, ct), cancellationToken);
    }

    private async Task<string?> EnsureCoverCoreAsync(
        WorkshopSource source,
        string id,
        string url,
        CancellationToken cancellationToken)
    {
        var cached = GetCachedCover(source, id);
        if (cached is not null)
        {
            return cached;
        }

        var relative = CoverRelativeName(source, id);
        var target = _files.Combine(_info.CoverRoot, relative);
        _files.EnsureDirectory(_files.GetDirectoryName(target) ?? _info.CoverRoot);

        var local = await _assets.DownloadAsync(url, target, cancellationToken).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(local))
        {
            return null;
        }

        var key = WorkshopInfoCache.Key(source, id);
        _info.SetCover(key, relative + Path.GetExtension(local), url);
        _quota?.RequestSweep();
        return local;
    }

    /// <summary>确保某张截图在本地；成功返回本地路径。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    /// <param name="url">截图地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <remarks>
    /// 文件名按 URL 哈希生成（不是列表序号）：同一张图在顶部图片墙和正文里指的是同一个文件，
    /// 也不会因为「上次这一序号是别的图」而显示成旧图。
    /// <para>
    /// 下载前先查「这条记录的封面是不是就是这个 URL」：是就直接用封面那份，不再存第二份。
    /// Steam 画廊首图与封面同图的情况实测占多数，这一步把 <c>shots\</c> 里的重复整份省掉。
    /// </para>
    /// </remarks>
    public Task<string?> EnsureScreenshotAsync(
        WorkshopSource source,
        string id,
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult<string?>(null);
        }

        var key = "shot:" + WorkshopInfoCache.Key(source, id) + "|" + url;
        return _coalesce.RunAsync(key, ct => EnsureScreenshotCoreAsync(source, id, url, ct), cancellationToken);
    }

    private async Task<string?> EnsureScreenshotCoreAsync(
        WorkshopSource source,
        string id,
        string url,
        CancellationToken cancellationToken)
    {
        var key = WorkshopInfoCache.Key(source, id);
        if (_info.TryGetCachedCoverForUrl(key, url, out var coverPath))
        {
            return coverPath;
        }

        var directory = ShotsDirectory(source, id);
        var stem = ShotStem(url);
        var existing = FindExisting(directory, stem);
        if (existing is not null)
        {
            return existing;
        }

        _files.EnsureDirectory(directory);
        var shot = await _assets
            .DownloadAsync(url, _files.Combine(directory, stem), cancellationToken)
            .ConfigureAwait(true);
        if (!string.IsNullOrWhiteSpace(shot))
        {
            _quota?.RequestSweep();
        }

        return shot;
    }

    /// <summary>截图落地的绝对目录。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    public string ShotsDirectory(WorkshopSource source, string id) =>
        _files.Combine(_info.CoverRoot, ShotsDirectoryName, SourceTag(source) + "_" + Sanitize(id));

    /// <summary>封面在缓存根下的相对路径（不含扩展名），与列表页的 <c>CoverCacheKey</c> 一致。</summary>
    /// <param name="source">来源。</param>
    /// <param name="id">工坊条目 id。</param>
    private static string CoverRelativeName(WorkshopSource source, string id) =>
        SourceTag(source) + "/" + id;

    /// <summary>截图文件名主干（不含扩展名）：URL 的稳定哈希，同一张图永远同一个文件名。</summary>
    /// <param name="url">图片地址。</param>
    private static string ShotStem(string url)
    {
        // FNV-1a 32 位：短、稳定、不需要引 System.Security.Cryptography。
        var hash = 2166136261u;
        foreach (var ch in url)
        {
            hash ^= ch;
            hash *= 16777619u;
        }

        return "shot_" + hash.ToString("x8", CultureInfo.InvariantCulture);
    }

    /// <summary>按「同名前缀 + 任意扩展名」找已下载的那张。</summary>
    private string? FindExisting(string directory, string stem)
    {
        if (!_files.DirectoryExists(directory))
        {
            return null;
        }

        foreach (var file in _files.EnumerateFiles(directory, stem + ".*", recursive: false))
        {
            return file;
        }

        return null;
    }

    private static string SourceTag(WorkshopSource source) =>
        source == WorkshopSource.Steam ? "steam" : "pdx";

    /// <summary>目录名里只留字母数字与下划线，避免 id 里的特殊字符把路径拆坏。</summary>
    private static string Sanitize(string id)
    {
        var builder = new StringBuilder(id.Length);
        foreach (var ch in id)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.ToString();
    }
}
