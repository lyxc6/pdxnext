namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 按游戏的存储格式把 <see cref="IModCatalog"/> 路由到具体实现：
/// <see cref="LoadFileFormat.PlaysetConfigV1"/>（CS2）走 <c>pdx_mods\</c> 扫描，
/// 其余（Clausewitz 系）仍走 <c>*.mod</c> / <c>.metadata\metadata.json</c> 扫描。
/// <para>
/// 与 <see cref="PlaySetRepositoryRouter"/> 同一个思路：格式判定只发生在这里一处，
/// <see cref="ModLibraryService"/> 与各 ViewModel 都不需要知道 CS2 的目录布局。
/// </para>
/// </summary>
public sealed class ModCatalogRouter : IModCatalog, IScanCacheInvalidator
{
    private readonly IModCatalog _clausewitz;
    private readonly IModCatalog _pdxMods;

    /// <summary>创建路由。</summary>
    /// <param name="clausewitz">Clausewitz 产线扫描器（EU4/CK3/Vic3/Stellaris/EU5）。</param>
    /// <param name="pdxMods">CS2 的 <c>pdx_mods</c> 扫描器。</param>
    public ModCatalogRouter(IModCatalog clausewitz, IModCatalog pdxMods)
    {
        ArgumentNullException.ThrowIfNull(clausewitz);
        ArgumentNullException.ThrowIfNull(pdxMods);
        _clausewitz = clausewitz;
        _pdxMods = pdxMods;
    }

    /// <summary>按格式挑实现。</summary>
    public IModCatalog For(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return game.Format == LoadFileFormat.PlaysetConfigV1 ? _pdxMods : _clausewitz;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(
        GameInstallation game,
        CancellationToken cancellationToken) =>
        For(game).DiscoverAllAsync(game, cancellationToken);

    /// <inheritdoc />
    /// <remarks>路由到具体实现；两个实现各自持有一份扫描缓存，这里不合并它们的缓存。</remarks>
    public void InvalidateCache(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        if (For(game) is IScanCacheInvalidator invalidatable)
        {
            invalidatable.InvalidateCache(game);
        }
    }

    /// <inheritdoc />
    public void ClearCache()
    {
        if (_clausewitz is IScanCacheInvalidator clausewitz)
        {
            clausewitz.ClearCache();
        }

        if (_pdxMods is IScanCacheInvalidator pdxMods)
        {
            pdxMods.ClearCache();
        }
    }
}
