namespace PdxLauncher.App.ViewModels;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 从存档页进入「备份」子页的导航参数。游戏在进入时固定下来，
/// 避免子页停留期间后台切换当前游戏导致列表与标题不一致。
/// </summary>
/// <param name="Game">要管理备份的游戏。</param>
/// <param name="ReturnTag">返回按钮的目标导航 Tag。</param>
public sealed record SaveBackupsNavigationRequest(GameInstallation Game, string? ReturnTag);
