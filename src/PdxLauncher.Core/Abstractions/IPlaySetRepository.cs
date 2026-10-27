using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>launcher-v2.sqlite 的播放集读写。读取必须只读打开，写入必须备份后事务提交。</summary>
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

    Task SavePlaySetModsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        CancellationToken cancellationToken);

    Task SavePlaySetDlcsAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetDlc> dlcs,
        CancellationToken cancellationToken);

    Task<Guid> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken);

    Task SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken);

    /// <summary>重命名播放集。返回 false 表示目标不存在或写入失败。</summary>
    Task<bool> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken);

    /// <summary>删除播放集（含其 mods/dlcs 行）。活动播放集不允许删除，返回 false。</summary>
    Task<bool> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken);
}
