namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「这款游戏我订阅了哪些、各自什么状态」的统一读取入口。
/// <para>
/// 读取一次要拉起一个 <c>--steam-host workshop-subscribed</c> 子进程（约 1 秒），
/// 所以这里只做两件事：按 appid 复用状态来源，以及「60 秒缓存 → 真查 → 失败退旧快照」。
/// Mod 库页与创意工坊列表页共用这份逻辑，避免两处各写一套缓存判定。
/// </para>
/// <para>
/// 任何失败都只记日志并返回空表：界面不显示徽标，绝不把「读不到」当成「没订阅」。
/// </para>
/// </summary>
public sealed class WorkshopStateReader
{
    private readonly Func<GameInstallation, IWorkshopStateSource?>? _sourceFactory;
    private readonly WorkshopStateCache? _cache;
    private readonly IAppLogger _logger;
    private readonly Dictionary<int, IWorkshopStateSource?> _sourcesByApp = new();

    /// <summary>构造。</summary>
    /// <param name="steamStatesFactory">按游戏造状态来源；返回 null 表示这款游戏没有直连条件。</param>
    /// <param name="cache">整库状态缓存；为 null 时每次都重新查。</param>
    /// <param name="logger">日志。</param>
    public WorkshopStateReader(
        Func<GameInstallation, IWorkshopStateSource?>? steamStatesFactory,
        WorkshopStateCache? cache,
        IAppLogger logger)
    {
        _sourceFactory = steamStatesFactory;
        _cache = cache;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 取当前游戏的状态来源（写操作也走它）；不可用时为 null。
    /// 结果按 appid 记住，避免每次点按钮都重新解析 dll。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>状态来源；不可用时为 null。</returns>
    public IWorkshopStateSource? ResolveSource(GameInstallation? game)
    {
        if (game is null || _sourceFactory is null)
        {
            return null;
        }

        var appId = WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId);
        if (appId is not > 0)
        {
            return null;
        }

        if (!_sourcesByApp.TryGetValue(appId.Value, out var source))
        {
            source = _sourceFactory(game);
            _sourcesByApp[appId.Value] = source;
        }

        return source is { IsAvailable: true } ? source : null;
    }

    /// <summary>当前游戏是否接上了直连状态来源。</summary>
    /// <param name="game">当前游戏。</param>
    /// <returns>是否可用。</returns>
    public bool IsAvailable(GameInstallation? game) => ResolveSource(game) is not null;

    /// <summary>
    /// 取整库状态表（工坊 id → 状态）。缓存新鲜时直接用，过期才起子进程；
    /// 查询失败时退回过期快照，再不行返回空表。
    /// </summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态表；不可用或失败时为空表。</returns>
    public async Task<IReadOnlyDictionary<ulong, WorkshopInstallState>> ReadAllAsync(
        GameInstallation? game,
        CancellationToken cancellationToken)
    {
        var source = ResolveSource(game);
        if (source is null)
        {
            return Empty;
        }

        var appId = WorkshopSourceChooser.ResolveAppId(game!.SteamAppId, game.GameId);
        if (appId is not > 0)
        {
            return Empty;
        }

        try
        {
            if (_cache is not null && _cache.TryGet(appId.Value, out var cached))
            {
                return cached;
            }

            var result = await source.ReadSubscribedAsync(cancellationToken).ConfigureAwait(false);
            if (!result.Ok)
            {
                _logger.Warn("Steam 状态：读取工坊订阅列表失败：" + result.Error);
                return _cache is not null && _cache.TryGetStale(appId.Value, out var stale) ? stale : Empty;
            }

            var map = new Dictionary<ulong, WorkshopInstallState>(result.Items.Count);
            foreach (var item in result.Items)
            {
                map[item.PublishedFileId] = item.State;
            }

            _cache?.Set(appId.Value, map);
            return map;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Steam 状态：读取工坊订阅列表异常：" + ex.Message);
            return _cache is not null && _cache.TryGetStale(appId.Value, out var stale) ? stale : Empty;
        }
    }

    /// <summary>空状态表（共享实例，避免每次失败都分配）。</summary>
    private static IReadOnlyDictionary<ulong, WorkshopInstallState> Empty { get; } =
        new Dictionary<ulong, WorkshopInstallState>();
}
