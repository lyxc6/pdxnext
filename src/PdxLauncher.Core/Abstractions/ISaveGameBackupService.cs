namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Saves;

/// <summary>
/// 存档备份库：备份、恢复、删除备份、安全删除真实存档、用量查询与显式清理。
/// <para>
/// 所有备份产物只落在 <c>&lt;data&gt;\save-backups\</c>；唯一会写到其外的动作是「恢复写回
/// 原存档目录」与「删除真实存档移入回收站」，二者都由用户显式触发。
/// </para>
/// </summary>
public interface ISaveGameBackupService
{
    /// <summary>备份根目录绝对路径：<c>&lt;data&gt;\save-backups</c>。</summary>
    string BackupRootDirectory { get; }

    /// <summary>
    /// 备份一个存档文件：源文件只读，绝不修改/移动。流程为流式复制到
    /// <c>&lt;backup&gt;.tmp</c>、计算 SHA-256、Move 成 <c>.bak</c>、再写 <c>.bak.json</c> sidecar。
    /// </summary>
    /// <param name="gameId">游戏 id（内部会规范化别名，如 <c>cities_v2</c> → <c>cities_skylines</c>）。</param>
    /// <param name="sourcePath">源存档绝对或相对路径。</param>
    /// <param name="meta">可选元数据，用于填充游戏版本/游戏内日期。</param>
    /// <param name="cancellationToken">取消令牌，复制过程中按缓冲块检查。</param>
    /// <returns>成功时带 <see cref="SaveBackupEntry"/>；配额超限时带当前用量。</returns>
    Task<SaveBackupResult> BackupAsync(
        string gameId,
        string sourcePath,
        SaveGameMeta? meta,
        CancellationToken cancellationToken);

    /// <summary>
    /// 恢复一个备份。默认写到同目录的 <c>&lt;原名&gt; (恢复 yyyyMMdd-HHmm).&lt;ext&gt;</c>，
    /// 绝不覆盖；<paramref name="overwrite"/> 为 <see langword="true"/> 时先把现有存档移入回收站再写回原名。
    /// 写入前校验大小与 SHA-256，任一项不符则不写任何文件。
    /// </summary>
    Task<SaveRestoreResult> RestoreAsync(
        SaveBackupEntry entry,
        bool overwrite,
        CancellationToken cancellationToken);

    /// <summary>删除备份正文与 sidecar；sidecar 缺失不阻止删除正文。</summary>
    Task<SaveDeleteResult> DeleteBackupAsync(SaveBackupEntry entry, CancellationToken cancellationToken);

    /// <summary>安全删除真实存档：不真删，移入 <c>&lt;data&gt;\save-backups\trash\&lt;gameId&gt;</c>。</summary>
    Task<SaveDeleteResult> DeleteSaveAsync(
        string gameId,
        string savePath,
        CancellationToken cancellationToken);

    /// <summary>当前备份用量（只统计 <c>.bak</c> 字节，不含 sidecar 与回收站）。</summary>
    Task<SaveBackupUsage> GetUsageAsync(CancellationToken cancellationToken);

    /// <summary>
    /// 列出某个游戏的备份，按创建时间降序。sidecar 缺失或无法解析的条目以「降级条目」返回
    /// （<see cref="SaveBackupEntry.IsDegraded"/> 为 true，只有文件名/大小/文件时间），
    /// 因为它们仍占用配额，必须可见、可删除。
    /// </summary>
    Task<IReadOnlyList<SaveBackupEntry>> ListBackupsAsync(string gameId, CancellationToken cancellationToken);

    /// <summary>
    /// 显式清理：按创建时间降序保留最新 <paramref name="keepNewest"/> 个，其余删除。
    /// 这是唯一会删除备份的入口，服务本身绝不自动清理。
    /// </summary>
    Task<SaveBackupPruneResult> PruneAsync(
        string gameId,
        int keepNewest,
        CancellationToken cancellationToken);
}
