using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>continue_game.json 读取器（只读，游戏负责写）。</summary>
public interface IContinueGameReader
{
    Task<ContinueGameInfo?> ReadAsync(GameInstallation game, CancellationToken cancellationToken);
}
