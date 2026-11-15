namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型下载进度。
/// <para>
/// 唯一真源是下载器：界面只做「百分数 + 已下载 / 总量」的呈现，不再自己按文件数估算。
/// </para>
/// </summary>
/// <param name="BytesReceived">已落盘的字节数（按目标尺寸折算，跨文件累计）。</param>
/// <param name="TotalBytes">模型总字节数（清单里的声明值）；0 表示未知。</param>
/// <param name="CurrentFileName">正在下载的文件名；全部完成时为 null。</param>
/// <param name="Resumed">本次是否从磁盘上的 <c>.part</c> 续传。</param>
public sealed record LocalTranslationDownloadProgress(
    long BytesReceived,
    long TotalBytes,
    string? CurrentFileName = null,
    bool Resumed = false)
{
    /// <summary>0 到 1 的比例；总长未知时为 0。</summary>
    public double Fraction => TotalBytes > 0
        ? Math.Clamp((double)BytesReceived / TotalBytes, 0d, 1d)
        : 0d;

    /// <summary>百分比（0 到 100，四舍五入）；界面文案直接用它。</summary>
    public int Percent => (int)Math.Round(Fraction * 100d);
}
