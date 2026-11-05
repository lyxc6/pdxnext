namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 一个文件的尺寸差异（期望值来自模型清单）。
/// </summary>
/// <param name="FileName">落盘文件名（相对模型目录）。</param>
/// <param name="ExpectedBytes">清单里的期望字节数。</param>
/// <param name="ActualBytes">磁盘上的实际字节数。</param>
public sealed record LocalTranslationModelSizeMismatch(
    string FileName,
    long ExpectedBytes,
    long ActualBytes)
{
    /// <summary>实际比期望多出（正数）或少掉（负数）的字节数。</summary>
    public long DeltaBytes => ActualBytes - ExpectedBytes;

    /// <summary>一句话描述，供日志与自检使用（不直接上界面，界面文案走 i18n）。</summary>
    /// <returns>形如 <c>a.bin：期望 16 字节，实际 15 字节</c> 的说明。</returns>
    public string Describe() => FileName + "：期望 " + ExpectedBytes + " 字节，实际 " + ActualBytes + " 字节";
}
