namespace PdxLauncher.Core.Saves;

/// <summary>
/// 存档备份的配额与清理策略。
/// <para>
/// <see cref="AutoPruneOldestWhenFull"/> 默认为 <see langword="false"/>：服务本身绝不自动删除备份。
/// 只有调用方显式开启后，<see cref="ISaveGameBackupService.BackupAsync"/> 在新备份会超出配额时，
/// 才会按创建时间从最旧开始删除，直到本次备份放得下；单份存档本身大于上限时仍返回配额超限。
/// </para>
/// </summary>
/// <param name="MaxTotalBytes">所有游戏备份正文的总字节上限。</param>
/// <param name="MaxEntriesPerGame">单个游戏的备份条目数上限。</param>
/// <param name="AutoPruneOldestWhenFull">配额不足时是否自动删除最旧的备份。</param>
public sealed record SaveBackupPolicy(
    long MaxTotalBytes,
    int MaxEntriesPerGame,
    bool AutoPruneOldestWhenFull)
{
    /// <summary>默认策略：2 GiB / 每游戏 200 条，不自动清理。</summary>
    public static SaveBackupPolicy Default { get; } = new(
        SaveBackupPaths.DefaultMaxTotalBytes,
        SaveBackupPaths.DefaultMaxEntriesPerGame,
        AutoPruneOldestWhenFull: false);
}
