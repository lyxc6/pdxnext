using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>DLC 目录扫描（V0 用 *.dlc，V1/V1_1 用 *.dlc.json）。</summary>
public interface IDlcCatalog
{
    Task<IReadOnlyList<DlcInfo>> DiscoverAsync(GameInstallation game, CancellationToken cancellationToken);
}
