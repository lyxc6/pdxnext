namespace PdxLauncher.App.Services;

/// <summary>
/// 「添加模组」子页的状态快照：记住排序方式、来源筛选与版本兼容性筛选，下次进入页面时恢复。
/// <para>
/// 这三项都是整页的显示偏好、与具体播放集无关，所以不按游戏 / 播放集区分。
/// 存的是本地化后的显示文案（与 <see cref="ModLibraryState.SortMode"/> 同一口径），
/// 语言切换后旧文案对不上任何选项时，页面回落到默认的「名称 A-Z」/「全部」。
/// </para>
/// <para>
/// 新增两项筛选字段都给了默认值：旧版本 JSON 只记了 <c>SortMode</c>，反序列化后这两项为空串，
/// 页面按「全部」处理，不会让老状态读不回来。
/// </para>
/// </summary>
/// <param name="SortMode">排序方式（本地化后的显示文案）。</param>
/// <param name="SourceFilter">来源筛选（本地化后的显示文案）。</param>
/// <param name="VersionFilter">版本兼容性筛选（本地化后的显示文案）。</param>
/// <param name="SortDescending">排序方向（true 倒序）；旧版本 JSON 没有时按默认方向处理。</param>
public sealed record AddModsState(
    string SortMode,
    string SourceFilter = "",
    string VersionFilter = "",
    bool SortDescending = false);
