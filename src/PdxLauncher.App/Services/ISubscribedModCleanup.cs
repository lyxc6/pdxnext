namespace PdxLauncher.App.Services;

/// <summary>
/// 退订 Steam 工坊条目后的本机清理结果。
/// </summary>
/// <param name="ContentDeleted">是否删除了工坊内容目录；目录本来就不存在时也为 true。</param>
/// <param name="RepositoryCount">从播放集仓储删除的 mod 行数。</param>
/// <param name="WarningText">清理不完整时给用户界面展示的原因；完整成功为 null。</param>
public sealed record SubscribedModCleanupResult(
    bool ContentDeleted,
    int RepositoryCount,
    string? WarningText)
{
    /// <summary>完全清理：内容不存在或已删除，数据库也已处理。</summary>
    public bool IsClean => ContentDeleted && WarningText is null;

    /// <summary>部分失败：保留原因，退订本身仍视为成功。</summary>
    public static SubscribedModCleanupResult Warning(string warningText, int repositoryCount = 0) =>
        new(false, repositoryCount, warningText);
}

/// <summary>
/// 已退订工坊 mod 的本机清理能力。只处理 Steam 工坊目录与播放集仓储里的对应行。
/// </summary>
public interface ISubscribedModCleanup
{
    /// <summary>
    /// 删除指定工坊条目的下载内容与 Mod 库记录。
    /// </summary>
    /// <param name="appId">游戏 Steam appid。</param>
    /// <param name="workshopId">纯数字 publishedFileId。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task<SubscribedModCleanupResult> CleanupAsync(
        int appId,
        string workshopId,
        CancellationToken cancellationToken);
}
