namespace PdxLauncher.App.ViewModels;

/// <summary>
/// 「添加模组」子页的入参：目标播放集 + 返回目标。
/// 页面自己不持导航职责，返回时把选择结果交回给发起方（播放集页）。
/// </summary>
/// <param name="PlaySetId">要把 mod 加进去的播放集 Id。</param>
/// <param name="PlaySetName">播放集名称（页面标题下方的说明用）。</param>
/// <param name="GameId">当前游戏 Id；用于校验页面打开期间游戏是否被换掉。</param>
/// <param name="ReturnTag">返回时要导航到的标签（通常是播放集页）。</param>
public sealed record AddModsNavigationRequest(Guid PlaySetId, string PlaySetName, string? GameId, string? ReturnTag);
