namespace PdxLauncher.App.Services;

/// <summary>
/// 「添加模组」子页的状态快照：只记住排序方式与方向，下次进入页面时恢复。
/// <para>
/// 排序是整页的显示偏好、与具体播放集无关，所以不按游戏 / 播放集区分。
/// 存的是本地化后的显示文案（与 <see cref="ModLibraryState.SortMode"/> 同一口径），
/// 语言切换后旧文案对不上任何选项时，页面回落到默认的「名称 A-Z」。
/// </para>
/// <para>
/// <b>来源与版本筛选刻意不持久化</b>：它们会隐藏候选，跨会话留下「版本偏低」或「仅本地」
/// 会让刚订阅的 mod 看起来没出现（用户看到的是 Mod 库有、添加模组没有）。筛选只在本次进入页面内生效，
/// 每次进入都从「全部」开始。旧版本 JSON 里的 <c>SourceFilter</c> / <c>VersionFilter</c> 会被忽略。
/// </para>
/// </summary>
/// <param name="SortMode">排序方式（本地化后的显示文案）。</param>
/// <param name="SortDescending">排序方向（true 倒序）；旧版本 JSON 没有时按默认方向处理。</param>
public sealed record AddModsState(
    string SortMode,
    bool SortDescending = false);
