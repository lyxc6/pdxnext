namespace PdxLauncher.Core.Online.Translation.Local;

/// <summary>
/// 本地模型下载结果。
/// </summary>
/// <param name="Success">是否全部成功。</param>
/// <param name="Message">给用户看的一句话说明。</param>
/// <param name="DownloadedBytes">本次实际下载的字节数。</param>
public sealed record LocalTranslationModelDownloadResult(bool Success, string Message, long DownloadedBytes);
