using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>已安装游戏目录的发现服务。</summary>
public interface IGameCatalog
{
    Task<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken);

    Task<GameInstallation?> DiscoverOneAsync(string gameId, CancellationToken cancellationToken);
}
