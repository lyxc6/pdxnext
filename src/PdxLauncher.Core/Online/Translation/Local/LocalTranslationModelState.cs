namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 某个本地模型此刻的安装状态快照。
/// <para>
/// <see cref="IsInstalled"/> 与 <see cref="LocalTranslationModelStore.IsInstalled"/> 同一口径
/// （文件齐全、尺寸正确；缺 <c>config.txt</c> 也仍算 true，保持历史语义），
/// 而 <see cref="IsRunnable"/> 才是「引擎真的能加载」。
/// </para>
/// </summary>
/// <param name="Kind">模型种类。</param>
/// <param name="Status">状态。</param>
/// <param name="Directory">模型目录（绝对路径）。</param>
/// <param name="MissingFiles">缺失的文件名（相对模型目录）；没有缺失时为空集合。</param>
/// <param name="SizeMismatches">尺寸不符的文件；没有时为空集合。</param>
/// <param name="Reason">给日志/诊断用的一句话说明；已安装时为 null。</param>
public sealed record LocalTranslationModelState(
    LocalTranslationModelKind Kind,
    LocalTranslationModelInstallStatus Status,
    string Directory,
    IReadOnlyList<string> MissingFiles,
    IReadOnlyList<LocalTranslationModelSizeMismatch> SizeMismatches,
    string? Reason)
{
    /// <summary>
    /// 文件是否齐全且尺寸正确；与 <see cref="LocalTranslationModelStore.IsInstalled"/> 同义。
    /// <para>缺 <c>config.txt</c> 不影响本属性，但会影响 <see cref="IsRunnable"/>。</para>
    /// </summary>
    public bool IsInstalled => Status is
        LocalTranslationModelInstallStatus.Installed or LocalTranslationModelInstallStatus.ConfigMissing;

    /// <summary>引擎真的能加载：文件齐、尺寸对、配置也在。</summary>
    public bool IsRunnable => Status == LocalTranslationModelInstallStatus.Installed;
}
