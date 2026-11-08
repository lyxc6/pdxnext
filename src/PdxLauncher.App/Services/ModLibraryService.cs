namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;

/// <summary>
/// 组装「Mod 库」页的数据：mods 表里的全部 mod（含大小、订阅时间、播放集归属），
/// 再合并磁盘上扫描到、但数据库还没登记的本地 mod（<see cref="ModLibraryEntry.IsUnregistered"/> 为 true）。
/// <para>
/// 装载时顺带整库查一次 Steam 侧状态（订阅 / 已安装 / 需要更新 / 下载中 / 本地禁用），
/// 注入到每一行。查不到就留空，界面不显示徽标——调用方不需要关心 Steam 在不在。
/// </para>
/// </summary>
public sealed class ModLibraryService
{
    private readonly IPlaySetRepository _playSets;
    private readonly IModCatalog _mods;
    private readonly IAppLogger _logger;
    private readonly WorkshopStateReader? _steamStates;

    /// <summary>构造（不带 Steam 状态：Mod 库不显示工坊徽标）。</summary>
    /// <param name="playSets">播放集仓储。</param>
    /// <param name="mods">磁盘 mod 扫描。</param>
    /// <param name="logger">日志。</param>
    public ModLibraryService(IPlaySetRepository playSets, IModCatalog mods, IAppLogger logger)
        : this(playSets, mods, logger, null)
    {
    }

    /// <summary>构造。</summary>
    /// <param name="playSets">播放集仓储。</param>
    /// <param name="mods">磁盘 mod 扫描。</param>
    /// <param name="logger">日志。</param>
    /// <param name="steamStatesFactory">
    /// 按游戏造 Steam 工坊状态来源；返回 null 表示这款游戏没有直连条件。
    /// 每次装载时按当前游戏解析一次，切换游戏后自动跟着换。
    /// </param>
    /// <param name="steamCache">Steam 状态的短 TTL 缓存；为 null 时每次装载都重新查。</param>
    public ModLibraryService(
        IPlaySetRepository playSets,
        IModCatalog mods,
        IAppLogger logger,
        Func<GameInstallation, IWorkshopStateSource?>? steamStatesFactory,
        WorkshopStateCache? steamCache = null)
    {
        _playSets = playSets ?? throw new ArgumentNullException(nameof(playSets));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // 没给工厂就是「这一页不看 Steam 状态」（纯本地模式的构造）。
        _steamStates = steamStatesFactory is null
            ? null
            : new WorkshopStateReader(steamStatesFactory, steamCache, _logger);
    }

    /// <summary>当前游戏是否接上了 Steam 工坊状态来源（装载后有效）。</summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>是否有可用的直连状态来源。</returns>
    public bool HasSteamStates(GameInstallation? game) => _steamStates?.IsAvailable(game) == true;

    /// <summary>
    /// 取当前游戏的 Steam 工坊状态来源（供视图模型做下载 / 禁用这类写操作）；
    /// 没接上时为 null。结果按 appid 记住，避免每次点按钮都重新解析 dll。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>状态来源；不可用时为 null。</returns>
    public IWorkshopStateSource? ResolveStateSource(GameInstallation? game) => _steamStates?.ResolveSource(game);

    /// <summary>读取 Mod 库快照；未选中游戏时返回空快照。</summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>快照。</returns>
    public async Task<ModLibrarySnapshot> LoadAsync(
        GameInstallation? game,
        CancellationToken cancellationToken,
        bool forceRefresh = false)
    {
        if (game is null)
        {
            return ModLibrarySnapshot.Empty;
        }

        if (forceRefresh && _mods is IScanCacheInvalidator invalidatable)
        {
            invalidatable.InvalidateCache(game);
        }

        var entries = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        var playSets = await _playSets.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);

        var merged = new List<ModLibraryEntry>(entries);
        var known = new HashSet<Guid>(entries.Select(entry => entry.ModId));

        // 磁盘扫描失败不应该让整个 Mod 库空白：只影响「本地 mod」这一段。
        IReadOnlyList<PlaySetMod> catalog;
        try
        {
            catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库：磁盘 mod 扫描失败，仅显示数据库中的 mod：" + ex.Message);
            catalog = Array.Empty<PlaySetMod>();
        }

        // 磁盘扫描是版本信息的「最新」来源：官方库里的 requiredVersion 是上次应用元数据时抄进去的，
        // 作者后来改了 metadata.json、而用户没再跑过官方启动器时，库里会停在旧值（实测有 1.13.* 对 1.15.*）。
        // metadata.json 的 version（作者版本）也一样：Vic3 有近一半 mod 没写 supported_game_version，
        // 但这一项常常有值，官方库把它放在 metadataVersion。这里统一用磁盘值覆盖库里值；
        // 磁盘没写的（比如 mod 已卸载、只剩库里的记录）保留库里值。
        // steamId 沿用原逻辑：库里空才用扫描值补。
        var scannedMods = new Dictionary<Guid, PlaySetMod>();
        foreach (var mod in catalog)
        {
            scannedMods[mod.ModId] = mod;
        }

        for (var index = 0; index < merged.Count; index++)
        {
            var entry = merged[index];
            if (!scannedMods.TryGetValue(entry.ModId, out var scanned))
            {
                continue;
            }

            var steamId = string.IsNullOrWhiteSpace(entry.SteamId) ? scanned.SteamId : entry.SteamId;
            var requiredVersion = string.IsNullOrWhiteSpace(scanned.RequiredVersion)
                ? entry.RequiredVersion
                : scanned.RequiredVersion;
            var modVersion = string.IsNullOrWhiteSpace(scanned.ModVersion)
                ? entry.ModVersion
                : scanned.ModVersion;

            if (!string.Equals(entry.SteamId, steamId, StringComparison.Ordinal)
                || !string.Equals(entry.RequiredVersion, requiredVersion, StringComparison.Ordinal)
                || !string.Equals(entry.ModVersion, modVersion, StringComparison.Ordinal))
            {
                merged[index] = entry with
                {
                    SteamId = steamId,
                    RequiredVersion = requiredVersion,
                    ModVersion = modVersion,
                };
            }
        }

        foreach (var mod in catalog)
        {
            if (!known.Add(mod.ModId))
            {
                continue;
            }

            // 磁盘上有、官方库里没有：不一定是「本地 mod」——刚订阅的工坊 mod 在官方启动器扫描前就是这样。
            // 拿得到创意工坊编号就按工坊 mod 展示（详情页/Steam/浏览器三个按钮才亮），否则才当本地 mod。
            var source = SteamWorkshop.GetWorkshopId(mod.SteamId, mod.GameRegistryId) is null
                ? ModWorkshopRef.SourceLocal
                : ModWorkshopRef.SourceSteam;

            merged.Add(new ModLibraryEntry(
                mod.ModId,
                mod.Name,
                mod.GameRegistryId,
                mod.DirPath,
                mod.ArchivePath,
                mod.ThumbnailPath,
                mod.RequiredVersion,
                mod.Status,
                source,
                SizeBytes: null,
                SubscribedOn: null,
                Array.Empty<Guid>(),
                IsUnregistered: true,
                SteamId: mod.SteamId,
                ModVersion: mod.ModVersion));
        }

        await InjectSteamStatesAsync(game, merged, cancellationToken).ConfigureAwait(false);

        merged.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.ModId.CompareTo(right.ModId);
        });

        return new ModLibrarySnapshot(merged, playSets);
    }

    /// <summary>
    /// 把 Steam 侧状态注入到各行。整库一次查询（走缓存），任何失败都只记日志、不打断装载。
    /// </summary>
    private async Task InjectSteamStatesAsync(
        GameInstallation game,
        List<ModLibraryEntry> merged,
        CancellationToken cancellationToken)
    {
        if (_steamStates is null || merged.Count == 0)
        {
            return;
        }

        var states = await _steamStates.ReadAllAsync(game, cancellationToken).ConfigureAwait(false);
        if (states.Count == 0)
        {
            return;
        }

        for (var index = 0; index < merged.Count; index++)
        {
            var entry = merged[index];
            if (SteamWorkshop.GetWorkshopId(entry.SteamId, entry.GameRegistryId) is not { } workshopId
                || !ulong.TryParse(workshopId, out var fileId))
            {
                continue;
            }

            if (states.TryGetValue(fileId, out var state))
            {
                merged[index] = entry with { SteamState = state };
            }
        }
    }
}
