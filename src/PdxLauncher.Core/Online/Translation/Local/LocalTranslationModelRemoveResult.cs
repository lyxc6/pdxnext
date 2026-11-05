namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 删除本地模型的结果。
/// <para>
/// 调用方不能只看有没有抛异常：删除失败不抛，而是通过 <see cref="Status"/> 与
/// <see cref="Residue"/> 如实报告，避免「目录还在却报成功」。
/// </para>
/// </summary>
/// <param name="Kind">模型种类。</param>
/// <param name="Status">结果三态（另有 NotFound）。</param>
/// <param name="Directory">被删除（或尝试删除）的模型目录。</param>
/// <param name="RemovedFiles">本次真正删掉的文件数。</param>
/// <param name="Residue">
/// 仍存在的条目，相对模型目录、用 <c>/</c> 分隔；空目录以 <c>/</c> 结尾，
/// <c>.</c> 表示模型目录本身还在但已无文件。
/// </param>
/// <param name="Error">失败/残留的原因说明（已记入日志）；无错误时为 null。</param>
public sealed record LocalTranslationModelRemoveResult(
    LocalTranslationModelKind Kind,
    LocalTranslationModelRemoveStatus Status,
    string Directory,
    int RemovedFiles,
    IReadOnlyList<string> Residue,
    string? Error)
{
    /// <summary>目录已经不在（删除成功或本来就没装）；不代表模型「可用」。</summary>
    public bool IsClean => Status is
        LocalTranslationModelRemoveStatus.Removed or LocalTranslationModelRemoveStatus.NotFound;
}
