namespace PdxLauncher.App.Services;

/// <summary>
/// 「添加模组」子页的状态快照：只记排序方式，下次进入页面时恢复。
/// <para>
/// 排序方式是整页的显示偏好、与具体播放集无关，所以不按游戏 / 播放集区分。
/// 存的是本地化后的显示文案（与 <see cref="ModLibraryState.SortMode"/> 同一口径），
/// 语言切换后旧文案对不上任何选项时，页面回落到默认的「名称 A-Z」。
/// </para>
/// </summary>
/// <param name="SortMode">排序方式（本地化后的显示文案）。</param>
public sealed record AddModsState(string SortMode);
