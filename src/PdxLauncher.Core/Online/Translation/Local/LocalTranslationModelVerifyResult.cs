namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 校验本地模型的结果。
/// <para>
/// <see cref="State"/> 总是存在性+尺寸检查的结果；<see cref="HashMismatches"/> 只在
/// <c>deep: true</c> 时才有内容。
/// </para>
/// </summary>
/// <param name="State">基础安装状态。</param>
/// <param name="HashMismatches">深度校验发现的问题；浅校验时为空集合。</param>
public sealed record LocalTranslationModelVerifyResult(
    LocalTranslationModelState State,
    IReadOnlyList<LocalTranslationModelHashMismatch> HashMismatches)
{
    /// <summary>文件齐、尺寸对、配置在，且（深度校验时）哈希全部吻合。</summary>
    public bool IsValid => State.IsRunnable && HashMismatches.Count == 0;
}
