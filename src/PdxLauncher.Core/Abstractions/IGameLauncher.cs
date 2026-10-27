using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>游戏启动器。</summary>
public interface IGameLauncher
{
    Task<ProcessStartResult> LaunchAsync(
        GameInstallation game,
        LaunchRequest request,
        CancellationToken cancellationToken);
}
