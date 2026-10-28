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
/// 封面沿用缓存条目里的记账（<c>SetCover</c> / <c>TryGetCachedCover</c>），
/// 截图没有缓存字段，按「来源_<c>id</c>」建子目录、文件名 <c>shot_NN</c>，靠枚举同名前缀复认。
/// </para>
/// </summary>
public sealed class WorkshopImageCache
{
    /// <summary>截图子目录名（与封面同一根，便于整体清理/迁移）。</summary>
    private const string ShotsDirectoryName = "shots";

    private readonly IFileSystem _files;
    private readonly IAssetDownloader _assets;
    private readonly WorkshopInfoCache _info;

    /// <summary>构造。</summary>
    /// <param name="files">文件系统抽象。</param>
    /// <param name="assets">下载器。</param>
    /// <param name="info">工坊信息缓存（提供缓存根与封面记账）。</param>
    public WorkshopImageCache(IFileSystem files, IAssetDownloader assets, WorkshopInfoCache info)
    {
        _files = files;
        _assets = assets;
        _info = info;
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
    public async Task<string?> EnsureCoverAsync(
        WorkshopSource source,
        string id,
        string? url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

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
    /// </remarks>
    public async Task<string?> EnsureScreenshotAsync(
        WorkshopSource source,
        string id,
        string url,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var directory = ShotsDirectory(source, id);
        var stem = ShotStem(url);
        var existing = FindExisting(directory, stem);
        if (existing is not null)
        {
            return existing;
        }

        _files.EnsureDirectory(directory);
        return await _assets
            .DownloadAsync(url, _files.Combine(directory, stem), cancellationToken)
            .ConfigureAwait(true);
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
