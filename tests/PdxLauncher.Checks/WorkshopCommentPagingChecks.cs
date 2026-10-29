namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Online;

/// <summary>
/// 详情缓存还原自检：命中缓存后详情必须带着作者 SteamID（评论翻页要用）、标签、浏览数。
/// <para>
/// 这条路径曾经漏字段，导致「第二次打开同一个 Steam mod 评论翻不了页」——
/// 所以这里直接断言还原结果，而不是只看能不能读回缓存。
/// </para>
/// </summary>
public static class WorkshopCommentPagingChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckRestoreKeepsCreator();
        CheckRestoreFallsBackToAuthor();
        CheckRestoreWithoutAuthor();
        return ChecksContext.Failures - before;
    }

    /// <summary>新缓存（详情里存了作者 id）：还原时原样带出来。</summary>
    private static void CheckRestoreKeepsCreator()
    {
        var entry = new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "3798537678",
            Name = "Renovatio",
            DetailText = "正文",
            CreatorSteamId = "76561198145098971",
            Author = "76561198145098971",
            Tags = ["Gameplay", "Overhaul"],
            Views = 12345,
            LifetimeSubscriptions = 6789,
            SizeBytes = 2048,
            FetchedUtc = DateTimeOffset.UtcNow,
            UpdatedUtc = DateTimeOffset.UtcNow,
        };

        var detail = SteamWorkshopDetailCache.Restore(entry, entry.Id, "兜底标题");

        ChecksContext.Equal("缓存还原: 作者 id", "76561198145098971", detail.CreatorSteamId);
        ChecksContext.Equal("缓存还原: 标题用缓存里的", "Renovatio", detail.Title);
        ChecksContext.Equal("缓存还原: 正文", "正文", detail.Description);
        ChecksContext.Equal("缓存还原: 标签数量", 2, detail.TagList.Count);
        ChecksContext.Equal("缓存还原: 浏览数", 12345L, detail.Views);
        ChecksContext.Equal("缓存还原: 订阅数", 6789L, detail.LifetimeSubscriptions);
        ChecksContext.Equal("缓存还原: 体积", 2048L, detail.FileSizeBytes);
        ChecksContext.Equal("缓存还原: 作者主页", "https://steamcommunity.com/profiles/76561198145098971", detail.CreatorProfileUrl);
    }

    /// <summary>老缓存文件里没有 CreatorSteamId 这个键，退回列表元数据的 Author（Steam 行存的就是 SteamID64）。</summary>
    private static void CheckRestoreFallsBackToAuthor()
    {
        var entry = new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "3579037811",
            Name = "Remove Skills Cap",
            DetailText = "正文",
            Author = "76561198000000001",
        };

        var detail = SteamWorkshopDetailCache.Restore(entry, entry.Id, "兜底标题");

        ChecksContext.Equal("缓存还原: 旧缓存靠 Author 补作者 id", "76561198000000001", detail.CreatorSteamId);
        ChecksContext.Equal("缓存还原: 旧缓存没有标签", 0, detail.TagList.Count);
        ChecksContext.Equal("缓存还原: 旧缓存浏览数为 0", 0L, detail.Views);
    }

    /// <summary>两个都没有：作者 id 为空（界面据此隐藏分页条，不摆死按钮）。</summary>
    private static void CheckRestoreWithoutAuthor()
    {
        var entry = new WorkshopInfoCacheEntry
        {
            Source = "steam",
            Id = "2291024373",
            DetailText = "正文",
            Author = "   ",
        };

        var detail = SteamWorkshopDetailCache.Restore(entry, entry.Id, "兜底标题");

        ChecksContext.Equal("缓存还原: 没有作者时为空", true, string.IsNullOrWhiteSpace(detail.CreatorSteamId));
        ChecksContext.Equal("缓存还原: 没有标题时用兜底", "兜底标题", detail.Title);
    }
}
