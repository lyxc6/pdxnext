namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// CS2 专用的 <see cref="IModCatalog"/> 实现：只扫 <c>.cache\Mods\pdx_mods\</c> 与
/// <c>pdx_mods_cache.json</c>，绝不碰 <c>*.mod</c> / <c>.metadata\metadata.json</c>
/// —— 那些是 Clausewitz 引擎（EU4/CK3/Vic3/Stellaris/EU5）的 mod 描述文件，CS2 没有。
/// <para>
/// 存在的理由：<see cref="ModCatalog"/> 对 CS2 扫出来恒为空，于是「Mod 库」页与
/// 「添加模组」候选列表对 CS2 全空，而播放集页却是好的 —— 用户会以为 mod 丢了一半。
/// </para>
/// </summary>
public sealed class PdxModsCatalog : IModCatalog
{
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly PdxModsDirectory _directory;

    /// <summary>创建 CS2 mod 目录扫描器。</summary>
    public PdxModsCatalog(IFileSystem fileSystem, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);
        _fileSystem = fileSystem;
        _logger = logger;
        _directory = new PdxModsDirectory(fileSystem, logger);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        var cache = PdxModsCache.Load(_fileSystem, _logger, PlaysetConfigPaths.GetModsCachePath(game));
        var result = new List<PlaySetMod>();
        var position = 0;
        foreach (var (mod, _) in _directory.ComposeLibrary(game, cache))
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(mod with { Position = position++ });
        }

        _logger.Info(
            "CS2 mod 扫描完成：" + game.GameId + "，磁盘 + 缓存合计 " +
            result.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个。");
        return Task.FromResult<IReadOnlyList<PlaySetMod>>(result);
    }
}
