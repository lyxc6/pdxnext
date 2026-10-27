namespace PdxLauncher.Core.Configuration;

/// <summary>旧数据目录迁移结果。</summary>
/// <param name="Migrated">本次是否真正执行了复制（目标已有数据或没有旧数据时为 false）。</param>
/// <param name="FilesCopied">复制的文件数。</param>
/// <param name="BytesCopied">复制的字节数（用于日志展示）。</param>
/// <param name="Failure">失败原因；成功为 null。</param>
public sealed record DataMigrationResult(bool Migrated, int FilesCopied, long BytesCopied, string? Failure);
