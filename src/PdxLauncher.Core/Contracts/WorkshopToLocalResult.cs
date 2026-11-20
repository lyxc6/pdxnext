namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 「工坊 mod 转本地 mod」成功后的产物：新分配的本地 ModId 与磁盘落点。
/// <para>失败信息一律走 <see cref="AppResult{T}.ErrorMessage"/> 的错误码，不放中文字面量。</para>
/// </summary>
/// <param name="NewModId">新分配的本地 mod Id（落库用的显式 Guid，不由路径派生重算）。</param>
/// <param name="LocalDirPath">落地后的内容目录绝对路径；归档型 mod 为空串。</param>
/// <param name="ArchivePath">落地后的 zip 绝对路径；目录型 mod 为 null。</param>
public sealed record WorkshopToLocalResult(
    Guid NewModId,
    string LocalDirPath,
    string? ArchivePath);
