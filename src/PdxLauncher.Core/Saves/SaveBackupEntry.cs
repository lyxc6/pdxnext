namespace PdxLauncher.Core.Saves;

/// <summary>
/// 一条已落盘的存档备份。所有路径都是绝对路径；<see cref="OriginalPath"/> 来自写入时传入的
/// 源文件路径，sidecar 会持久化它，因此条目过期时可被识别。
/// </summary>
/// <param name="BackupPath">备份正文绝对路径（<c>.bak</c>）。</param>
/// <param name="SidecarPath">sidecar 绝对路径（<c>.bak.json</c>）。</param>
/// <param name="OriginalPath">备份时的源存档绝对路径。</param>
/// <param name="OriginalFileName">备份时的源文件名（不含目录）。</param>
/// <param name="SizeBytes">备份正文字节数，与 sidecar 和实际文件一致。</param>
/// <param name="Sha256">备份正文的小写 SHA-256（64 位十六进制）。</param>
/// <param name="GameId">规范化后的 gameId。</param>
/// <param name="GameVersion">游戏版本（来自元数据，可为空）。</param>
/// <param name="GameDate">游戏内日期（来自元数据，可为空）。</param>
/// <param name="CreatedUtc">备份创建时间（UTC）。</param>
public sealed record SaveBackupEntry(
    string BackupPath,
    string SidecarPath,
    string OriginalPath,
    string OriginalFileName,
    long SizeBytes,
    string Sha256,
    string GameId,
    string? GameVersion,
    string? GameDate,
    DateTimeOffset CreatedUtc)
{
    /// <summary>
    /// 是否为「孤儿备份」降级条目：<c>.bak</c> 正文存在，但同名 sidecar 缺失或损坏。
    /// 这种条目没有原始路径与哈希（<see cref="Sha256"/> 为空），只能删除、不能恢复；
    /// 它仍然计入配额，所以必须出现在列表里，否则用户永远无法清理它。
    /// </summary>
    public bool IsDegraded => Sha256.Length == 0;
}
