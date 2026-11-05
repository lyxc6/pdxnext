namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 一个文件的哈希不符（或读取失败）。
/// </summary>
/// <param name="FileName">落盘文件名（相对模型目录）。</param>
/// <param name="ExpectedSha256">清单里的 SHA-256（小写十六进制）。</param>
/// <param name="ActualSha256">实际算出的 SHA-256；读取失败时为 null。</param>
/// <param name="Error">读取/计算失败的原因；哈希不一致时为 null。</param>
public sealed record LocalTranslationModelHashMismatch(
    string FileName,
    string ExpectedSha256,
    string? ActualSha256,
    string? Error)
{
    /// <summary>是不是「读不了文件」而不是「内容不对」。</summary>
    public bool IsReadError => ActualSha256 is null;
}
