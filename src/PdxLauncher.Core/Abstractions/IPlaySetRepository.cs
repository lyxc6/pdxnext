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

    /// <summary>写播放集的 mod 行。失败时返回 <see cref="ErrorKind.Io"/> / <see cref="ErrorKind.NotFound"/>。</summary>
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
