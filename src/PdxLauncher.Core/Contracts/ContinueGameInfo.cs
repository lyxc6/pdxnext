namespace PdxLauncher.Core.Contracts;

/// <summary>continue_game.json 的内容（由游戏写入，启动器只读）。</summary>
public sealed record ContinueGameInfo(
    string? Title,
    string? Description,
    string? Date,
    string? RawGameVersion);
