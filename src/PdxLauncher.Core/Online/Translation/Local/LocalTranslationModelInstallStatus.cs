namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型的安装状态。
/// <para>
/// 区分「没下过」「下了一半」「文件大小不对」「文件齐了但缺配置」与「真的能跑」，
/// 让界面与日志能说清是哪一步没成，而不是笼统地显示「未安装」。
/// </para>
/// </summary>
public enum LocalTranslationModelInstallStatus
{
    /// <summary>模型目录不存在（从没下载过，或已经删干净）。</summary>
    NotInstalled,

    /// <summary>目录在，但有文件没下全。</summary>
    MissingFiles,

    /// <summary>文件都在，但至少一个文件的字节数与清单期望值不一致。</summary>
    SizeMismatch,

    /// <summary>文件齐全且尺寸正确，但缺 Bergamot 的 <c>config.txt</c>：装了，引擎却加载不了。</summary>
    ConfigMissing,

    /// <summary>文件齐全、尺寸正确、配置也在（Index-Translate 不需要配置）。</summary>
    Installed,
}
