namespace PdxLauncher.Core.Saves;

/// <summary>备份侧写文件（<c>.bak.json</c>）的内容，schemaVersion 固定为 1。</summary>
internal sealed record SaveBackupSidecar(
    int SchemaVersion,
    string OriginalPath,
    string OriginalFileName,
    long SizeBytes,
    string Sha256,
    string GameId,
    string? GameVersion,
    string? GameDate,
    string? SaveName,
    DateTimeOffset CreatedUtc);
