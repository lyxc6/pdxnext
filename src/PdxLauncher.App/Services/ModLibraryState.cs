namespace PdxLauncher.App.Services;

/// <summary>
/// Mod 库页的会话内状态快照：页面改成「离开即销毁」后，用它把搜索词、排序、来源筛选与滚动位置原样带回来。
/// 只在本次运行内有效，不写盘、不跨重启。
/// </summary>
/// <param name="GameId">记录状态时当前游戏的 id；换了游戏就不再套用旧状态。</param>
/// <param name="SearchText">搜索框内容。</param>
/// <param name="SortMode">排序方式（本地化后的显示文案）。</param>
/// <param name="SourceFilter">来源筛选（本地化后的显示文案）。</param>
/// <param name="VerticalOffset">列表滚动位置（像素）。</param>
public sealed record ModLibraryState(
    string? GameId,
    string SearchText,
    string SortMode,
    string SourceFilter,
    double VerticalOffset);
