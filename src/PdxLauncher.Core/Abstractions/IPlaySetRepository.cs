using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>launcher-v2.sqlite 的播放集读写。读取必须只读打开，写入必须备份后事务提交。</summary>
/// <remarks>
/// 写方法一律返回 <see cref="AppResult{T}"/>：仓储层不再吞写失败，也不返回中文字面量。
/// <see cref="AppResult{T}.ErrorMessage"/> 是错误码（<c>playSet-not-found:&lt;guid&gt;</c> 这类），由展示层翻译。
/// </remarks>
public interface IPlaySetRepository
{
    Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken);

    Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken);

    /// <summary>读取 Mod 库的全部行：mods 表的展示信息 + 每个 mod 被哪些播放集引用。</summary>
    Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(
        GameInstallation game,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken);

    /// <summary>
    /// 写播放集的 mod 行。失败时返回 <see cref="ErrorKind.Io"/> / <see cref="ErrorKind.NotFound"/>。
    /// <para>
    /// SQLite 实现会顺带把「磁盘上有内容但 <c>mods</c> 表里还没有行」的 mod 补登记进官方库（同一个事务、同一次备份），
    /// 否则 <c>playsets_mods</c> 会留下悬空外键，刚订阅的工坊 mod 在游戏里读不出来。
    /// </para>
    /// </summary>
    /// <summary>
    /// 就地修正一条 mod 的路径（只改路径列，不动播放集顺序与启停）。
    /// <para>
    /// 用于「官方库里的行丢了 dirPath/archivePath」或「本地 mod 目录名改成 ASCII」这类修复：
    /// 加载文件是按行里的路径写的，路径错了就等于游戏里没有这个 mod。
    /// </para>
    /// </summary>
    Task<AppResult<bool>> UpdateModPathsAsync(
        GameInstallation game,
        Guid modId,
        string? dirPath,
        string? archivePath,
        CancellationToken cancellationToken);

    /// <summary>
    /// 「工坊 mod 转本地 mod」的正向库写入：单事务里插入一条新的本地 <c>mods</c> 行
    /// （显式 <c>source='local'</c>、<c>steamId=NULL</c>），再把所有引用 <paramref name="oldModId"/> 的
    /// <c>playsets_mods.modId</c> 改成 <paramref name="localMod"/> 的 ModId。
    /// <para>
    /// <c>enabled</c> / <c>position</c> 原样保留；旧 mods 行不删，供失败时反向补偿。
    /// EU5（playsets.json）按 <paramref name="oldDirPath"/> / <paramref name="oldArchivePath"/> 重写
    /// <c>orderedListMods.path</c>。<see cref="ModOrigins.Normalize(string?, string?)"/> 判定来源，
    /// 不能只看 steamId；写入一律经 Router 的 WriteAsync 并检查返回值。
    /// </para>
    /// </summary>
    Task<AppResult<bool>> AdoptLocalModAsync(
        GameInstallation game,
        Guid oldModId,
        string? oldDirPath,
        string? oldArchivePath,
        PlaySetMod localMod,
        CancellationToken cancellationToken);

    /// <summary>
    /// <see cref="AdoptLocalModAsync"/> 的逆操作，供「自有库行写入失败」等部分成功场景补偿：
    /// 单事务里先把 <c>playsets_mods.modId</c> 从 new 改回 old，确认引用恢复后再删新 <c>mods</c> 行。
    /// <b>顺序不可颠倒</b>，也绝不调用会删 <c>playsets_mods</c> 行的 DeleteMods。
    /// <para>
    /// EU5 把 <c>orderedListMods.path</c> 写回旧值；因为签名里只有旧路径，
    /// <paramref name="newDirPath"/> / <paramref name="newArchivePath"/> 由转换调用方传入，
    /// 用来在 playsets.json 里定位刚改写的条目（sqlite 实现忽略这两个参数）。
    /// </para>
    /// </summary>
    Task<AppResult<bool>> RevertLocalModAdoptionAsync(
        GameInstallation game,
        Guid newModId,
        Guid oldModId,
        string? oldDirPath,
        string? oldArchivePath,
        CancellationToken cancellationToken,
        string? newDirPath = null,
        string? newArchivePath = null);

    Task<AppResult<bool>> SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken);

    /// <summary>写播放集的 DLC 行。失败语义同 <see cref="SavePlaySetModsAsync"/>。</summary>
    Task<AppResult<bool>> SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken);

    /// <summary>
    /// 新建播放集并返回其 id。写入失败时返回 <see cref="ErrorKind.Io"/> 且 <see cref="AppResult{T}.Value"/> 为 <see cref="Guid.Empty"/>；
    /// 重名返回 <see cref="ErrorKind.Conflict"/>。
    /// </summary>
    Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken);

    /// <summary>把播放集设为活动集。目标不存在 → <see cref="ErrorKind.NotFound"/>，写失败 → <see cref="ErrorKind.Io"/>。</summary>
    Task<AppResult<bool>> SetActivePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken);

    /// <summary>重命名播放集。目标不存在 → <see cref="ErrorKind.NotFound"/>，重名 → <see cref="ErrorKind.Conflict"/>，写失败 → <see cref="ErrorKind.Io"/>。</summary>
    Task<AppResult<bool>> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>删除播放集（含其 mods/dlcs 行）。活动播放集不允许删除 → <see cref="ErrorKind.Conflict"/>。</summary>
    Task<AppResult<bool>> DeletePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken);
}
