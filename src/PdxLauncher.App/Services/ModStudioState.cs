namespace PdxLauncher.App.Services;

/// <summary>
/// Mod 创建页的会话内状态快照：选中的 mod、当前面板、三个面板各自的滚动位置，以及表单草稿。
/// </summary>
/// <param name="GameId">记录状态时当前游戏的 id；换了游戏就不再套用旧状态。</param>
/// <param name="SelectedPath">选中 mod 的目录（用它而不是行对象，重新加载后还能对上）。</param>
/// <param name="Pane">当前面板：detail / create / upload。</param>
/// <param name="DetailOffset">详情面板滚动位置。</param>
/// <param name="CreateOffset">新建面板滚动位置。</param>
/// <param name="UploadOffset">上传面板滚动位置。</param>
/// <param name="Draft">两块表单的草稿。</param>
public sealed record ModStudioState(
    string? GameId,
    string? SelectedPath,
    string Pane,
    double DetailOffset,
    double CreateOffset,
    double UploadOffset,
    ModStudioDraft Draft);
