namespace PdxLauncher.Core.Online;

/// <summary>
/// 主题缓存的可展示诊断信息。只放「持久化在索引里的事实」，不放路径/来源：
/// 当前背景文件与来源由设置页从 ThemeSpec 取，文件修改时间用文件系统读取。
/// </summary>
/// <param name="LastCheckedUtc">上次全部素材就绪的联网检查时间。</param>
/// <param name="RetryAfterUtc">失败后的最早重试时间；null 表示没有待重试的失败。</param>
/// <param name="LastError">上次失败的中文原因（含「元数据无该游戏」）。</param>
/// <param name="LastErrorUtc">上次失败发生的时间。</param>
/// <param name="MetadataMissing">上次检查时 metadata 里没有该游戏。</param>
public sealed record ThemeCacheDiagnostics(
    DateTimeOffset? LastCheckedUtc,
    DateTimeOffset? RetryAfterUtc,
    string? LastError,
    DateTimeOffset? LastErrorUtc,
    bool MetadataMissing);
