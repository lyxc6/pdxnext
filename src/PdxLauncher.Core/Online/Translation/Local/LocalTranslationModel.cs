namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 一个可下载的本地翻译模型条目。
/// </summary>
/// <param name="Kind">模型种类。</param>
/// <param name="DisplayName">界面上显示的名字。</param>
/// <param name="TotalBytes">下载完成后的总字节数（含全部词表与配置文件）。</param>
/// <param name="Sha256">主模型文件的 SHA-256（十六进制，小写）；为空表示不校验。</param>
/// <param name="Files">组成该模型的各个文件。</param>
public sealed record LocalTranslationModel(
    LocalTranslationModelKind Kind,
    string DisplayName,
    long TotalBytes,
    string? Sha256,
    IReadOnlyList<LocalTranslationModelFile> Files);
