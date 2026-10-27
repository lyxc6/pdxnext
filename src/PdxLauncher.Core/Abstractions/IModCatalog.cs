using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>mod 目录扫描（mods 表 + .mod 描述符 / .metadata/metadata.json）。</summary>
public interface IModCatalog
{
    Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken);
}
