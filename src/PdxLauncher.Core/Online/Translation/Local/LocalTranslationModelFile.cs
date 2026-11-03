namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型里的单个文件。
/// </summary>
/// <param name="FileName">落盘文件名（相对模型目录）。</param>
/// <param name="Url">下载地址。</param>
/// <param name="Sha256">解压后内容的 SHA-256；为空表示不校验。</param>
/// <param name="ExpectedSize">解压后的字节数；0 表示未知。</param>
/// <param name="Compressed">是否需要先做 gzip 解压。</param>
public sealed record LocalTranslationModelFile(
    string FileName,
    string Url,
    string? Sha256 = null,
    long ExpectedSize = 0,
    bool Compressed = false);
