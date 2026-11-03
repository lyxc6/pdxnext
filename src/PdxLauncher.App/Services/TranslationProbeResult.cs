namespace PdxLauncher.App.Services;

/// <summary>
/// 单个翻译引擎的探测结果（设置页「测试翻译」）。
/// </summary>
/// <param name="Key">
/// 引擎对应的<see cref="TranslationEngineModes">模式键</see>，界面用它取本地化标题。
/// </param>
/// <param name="Ok">是否真的翻出了非空译文。</param>
/// <param name="Detail">成功时是译文本身；失败时是失败原因（形如 <c>mymemory: 未返回译文</c>）。</param>
public readonly record struct TranslationProbeResult(string Key, bool Ok, string Detail);
