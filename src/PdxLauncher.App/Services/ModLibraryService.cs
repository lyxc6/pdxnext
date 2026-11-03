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
    private readonly Func<GameInstallation, IWorkshopStateSource?>? _steamStatesFactory;
    private readonly WorkshopStateCache? _steamCache;
    private readonly Dictionary<int, IWorkshopStateSource?> _steamSourcesByApp = new();

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
        _steamStatesFactory = steamStatesFactory;
        _steamCache = steamCache;
    }

    /// <summary>当前游戏是否接上了 Steam 工坊状态来源（装载后有效）。</summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>是否有可用的直连状态来源。</returns>
    public bool HasSteamStates(GameInstallation? game) => ResolveStateSource(game)?.IsAvailable == true;

    /// <summary>
    /// 取当前游戏的 Steam 工坊状态来源（供视图模型做下载 / 禁用这类写操作）；
    /// 没接上时为 null。结果按 appid 记住，避免每次点按钮都重新解析 dll。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>状态来源；不可用时为 null。</returns>
    public IWorkshopStateSource? ResolveStateSource(GameInstallation? game)
    {
        if (game is null || _steamStatesFactory is null)
        {
            return null;
        }

        var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
        if (appId is not > 0)
        {
            return null;
        }

        if (!_steamSourcesByApp.TryGetValue(appId.Value, out var source))
        {
            source = _steamStatesFactory(game);
            _steamSourcesByApp[appId.Value] = source;
        }

        return source is { IsAvailable: true } ? source : null;
    }

    /// <summary>读取 Mod 库快照；未选中游戏时返回空快照。</summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>快照。</returns>
    public async Task<ModLibrarySnapshot> LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return ModLibrarySnapshot.Empty;
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

        // 数据库里的 steamId 可能为空（老库、或官方启动器没写这一列），而磁盘上 mod 的
        // .mod 描述文件 / metadata.json 一般带着创意工坊 id：用扫描结果给已登记的行补齐，
        // 播放集页与 Mod 库页的「在 Steam / 浏览器中打开」才不会平白少一半。
        var scannedSteamIds = new Dictionary<Guid, string>();
        foreach (var mod in catalog)
        {
            if (!string.IsNullOrWhiteSpace(mod.SteamId))
            {
                scannedSteamIds[mod.ModId] = mod.SteamId;
            }
        }

        if (scannedSteamIds.Count > 0)
        {
            for (var index = 0; index < merged.Count; index++)
            {
                var entry = merged[index];
                if (string.IsNullOrWhiteSpace(entry.SteamId)
                    && scannedSteamIds.TryGetValue(entry.ModId, out var scannedSteamId))
                {
                    merged[index] = entry with { SteamId = scannedSteamId };
                }
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
                SteamId: mod.SteamId));
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
        if (_steamStatesFactory is null || merged.Count == 0)
        {
            return;
        }

        var source = ResolveStateSource(game);
        if (source is null)
        {
            return;
        }

        var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
        if (appId is not > 0)
        {
            return;
        }

        IReadOnlyDictionary<ulong, WorkshopInstallState> states;
        try
        {
            if (_steamCache is not null && _steamCache.TryGet(appId.Value, out var cached))
            {
                states = cached;
            }
            else
            {
                var result = await source.ReadSubscribedAsync(cancellationToken).ConfigureAwait(false);
                if (!result.Ok)
                {
                    _logger.Warn("Mod 库：读取 Steam 工坊订阅列表失败：" + result.Error);
                    if (_steamCache is null || !_steamCache.TryGetStale(appId.Value, out var stale))
                    {
                        return;
                    }

                    states = stale;
                }
                else
                {
                    var map = new Dictionary<ulong, WorkshopInstallState>(result.Items.Count);
                    foreach (var item in result.Items)
                    {
                        map[item.PublishedFileId] = item.State;
                    }

                    _steamCache?.Set(appId.Value, map);
                    states = map;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库：注入 Steam 工坊状态失败：" + ex.Message);
            return;
        }

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
