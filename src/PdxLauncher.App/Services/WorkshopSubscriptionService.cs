namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;
using PdxLauncher.Core.PdxSdk;
using PdxLauncher.Core.Playsets.PlaysetConfig;

/// <summary>
/// 创意工坊详情页的 PDX 订阅：把「某个 mod 是否已订阅 / 订阅 / 退订」翻译成平台接口调用。
/// <para>
/// 平台侧订阅是按<b>播放集</b>（API 里叫 activation set，字段 <c>activationSetId</c>）登记的，
/// 所以每次订阅前都要先确定用哪个播放集：优先复用缓存里这个游戏上次用过的，
/// 没有就在账号里建一个名字固定的播放集并记下来（<c>data/pdx-playset.json</c>）。
/// </para>
/// <para>
/// 注意：这会在用户的 Paradox 账号里留下一个与官方启动器并列的播放集，属于可感知的副作用，
/// 界面上要说明；这里只负责实现，文案由调用方给。
/// </para>
/// </summary>
public sealed class WorkshopSubscriptionService
{
    /// <summary>播放集映射缓存文件名（放在数据根下）。</summary>
    public const string CacheFileName = "pdx-playset.json";

    /// <summary>需要新建播放集时使用的名字。</summary>
    public const string PlaySetName = "PdxNext";

    private readonly PdxModsClient _pdx;
    private readonly AccountService _account;
    private readonly IFileSystem _fileSystem;
    private readonly string _cachePath;
    private readonly IAppLogger _logger;
    private readonly Func<GameInstallation, PdxSdkHost?> _hostFactory;
    private readonly Dictionary<string, PdxSdkHost> _hosts = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>构造。</summary>
    /// <param name="pdx">PDX Mods 客户端（含订阅写接口）。</param>
    /// <param name="account">账号服务（提供登录会话）。</param>
    /// <param name="fileSystem">文件系统（读写播放集映射缓存）。</param>
    /// <param name="dataRoot">数据根目录。</param>
    /// <param name="logger">日志。</param>
    /// <param name="hostFactory">
    /// 按游戏造 <see cref="PdxSdkHost"/> 的工厂；默认用官方 SDK。自检可传 null 造出独立假件。
    /// </param>
    public WorkshopSubscriptionService(
        PdxModsClient pdx,
        AccountService account,
        IFileSystem fileSystem,
        string dataRoot,
        IAppLogger logger,
        Func<GameInstallation, PdxSdkHost?>? hostFactory = null)
    {
        ArgumentNullException.ThrowIfNull(pdx);
        ArgumentNullException.ThrowIfNull(account);
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        ArgumentNullException.ThrowIfNull(logger);

        _pdx = pdx;
        _account = account;
        _fileSystem = fileSystem;
        _cachePath = Path.Combine(dataRoot, CacheFileName);
        _logger = logger;
        _hostFactory = hostFactory ?? CreateDefaultHost;
    }

    /// <summary>当前是否已登录（未登录时只能看，不能订阅）。</summary>
    public bool IsSignedIn => PdxModsClient.IsSignedIn(_account.Session);

    /// <summary>
    /// 查询订阅状态：只读，任何失败都折算成「未知」而不是抛给界面。
    /// </summary>
    /// <param name="gameName">平台游戏 id。</param>
    /// <param name="modId">平台 mod id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<WorkshopSubscriptionState> GetStateAsync(
        string gameName,
        string modId,
        CancellationToken cancellationToken)
    {
        if (!IsSignedIn)
        {
            return WorkshopSubscriptionState.SignInRequired;
        }

        try
        {
            var host = ResolveHost(gameName);
            if (host is null)
            {
                return WorkshopSubscriptionState.Unknown;
            }

            var playSet = await host.GetActivePlaySetAsync(cancellationToken).ConfigureAwait(true);
            if (playSet.Status != PdxSdkPlaySetStatus.Active)
            {
                _logger.Info("PDX 订阅状态查询：没有激活播放集（" + playSet.Status + "）");
                return WorkshopSubscriptionState.Unknown;
            }

            // SDK 自己维护 mods_subscribed / playsets_metadata；这里直接问它“在不在当前播放集”。
            var subscribed = await host
                .IsModInPlaySetAsync(playSet.PlaySetId, modId, cancellationToken)
                .ConfigureAwait(true);

            if (subscribed is null)
            {
                return WorkshopSubscriptionState.Unknown with { ActivationSetId = playSet.PlaySetId };
            }

            return new WorkshopSubscriptionState(
                IsSignedIn: true,
                IsKnown: true,
                IsSubscribed: subscribed.Value,
                ActivationSetId: playSet.PlaySetId);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("查询 PDX 订阅状态失败: " + ex.Message);
            return WorkshopSubscriptionState.Unknown;
        }
    }

    /// <summary>订阅或退订（由界面做二次确认后调用）。</summary>
    /// <param name="gameName">平台游戏 id。</param>
    /// <param name="modId">平台 mod id。</param>
    /// <param name="version">要固定订阅的版本；null 跟随最新。</param>
    /// <param name="subscribe">true 订阅，false 退订。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task<Core.Contracts.AppResult<bool>> SetSubscribedAsync(
        string gameName,
        string modId,
        int? version,
        bool subscribe,
        CancellationToken cancellationToken)
    {
        if (!IsSignedIn)
        {
            return Core.Contracts.AppResult<bool>.Fail(
                Core.Contracts.ErrorKind.Unauthorized,
                "workshop-subscription:sign-in-required");
        }

        try
        {
            var host = ResolveHost(gameName);
            if (host is null)
            {
                return Core.Contracts.AppResult<bool>.Fail(
                    Core.Contracts.ErrorKind.Io,
                    "workshop-subscription:sdk-unavailable");
            }

            var playSet = await host.GetActivePlaySetAsync(cancellationToken).ConfigureAwait(true);
            if (playSet.Status == PdxSdkPlaySetStatus.NotActive)
            {
                return Core.Contracts.AppResult<bool>.Fail(
                    Core.Contracts.ErrorKind.Invalid,
                    "workshop-subscription:no-active-playset");
            }

            if (playSet.Status != PdxSdkPlaySetStatus.Active)
            {
                return Core.Contracts.AppResult<bool>.Fail(
                    Core.Contracts.ErrorKind.Io,
                    playSet.ErrorText ?? "workshop-subscription:playset-read-failed");
            }

            var result = subscribe
                ? await host.SubscribeAsync(playSet.PlaySetId, modId, version, cancellationToken).ConfigureAwait(true)
                : await host.UnsubscribeAsync(playSet.PlaySetId, modId, cancellationToken).ConfigureAwait(true);

            if (result.Ok)
            {
                _logger.Info(
                    (subscribe ? "已订阅" : "已退订") + " PDX mod: " + modId +
                    "（SDK 播放集 " + playSet.PlaySetId.ToString(CultureInfo.InvariantCulture) + "）");
                return Core.Contracts.AppResult<bool>.Ok(true);
            }

            return Core.Contracts.AppResult<bool>.Fail(
                Core.Contracts.ErrorKind.Io,
                result.ErrorText ?? "workshop-subscription:sdk-failed");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("PDX 订阅操作失败: " + ex.Message);
            return Core.Contracts.AppResult<bool>.Fail(
                Core.Contracts.ErrorKind.Io,
                "workshop-subscription:exception");
        }
    }

    /// <summary>
    /// 找当前游戏对应的 <see cref="PdxSdkHost"/>：同一游戏复用同一个实例，
    /// 因为 SDK 的 <c>Context</c> 初始化一次后要长期持有。
    /// </summary>
    private PdxSdkHost? ResolveHost(string gameName)
    {
        if (_hosts.TryGetValue(gameName, out var existing))
        {
            return existing;
        }

        var game = AppServices.CurrentGame;
        if (game is null || !string.Equals(game.GameId, gameName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = _hostFactory(game);
        if (host is not null)
        {
            _hosts[gameName] = host;
        }

        return host;
    }

    /// <summary>默认工厂：用当前会话 + 游戏的 <c>.cache\Mods</c> 造官方 SDK host。</summary>
    private PdxSdkHost? CreateDefaultHost(GameInstallation game)
    {
        try
        {
            var session = _account.Session;
            if (session is null)
            {
                return null;
            }

            var options = new PdxSdkOptions(
                PlatformGameId: game.GameId,
                ModsRootPath: PlaysetConfigPaths.GetDirectory(game),
                DiskIORoot: Path.Combine(Path.GetDirectoryName(_cachePath) ?? PdxDataRoot.Directory, "pdxsdk", session.UserId),
                UserId: session.UserId,
                Session: session,
                GameVersion: game.Version);

            return new PdxSdkHost(options, _logger, appVersion: null, game: game);
        }
        catch (Exception ex)
        {
            _logger.Warn("初始化 PDX.SDK 失败: " + ex.Message);
            return null;
        }
    }
}

/// <summary>某个 PDX mod 的订阅状态快照。</summary>
/// <param name="IsSignedIn">是否已登录（未登录时 <paramref name="IsKnown"/> 恒为 false）。</param>
/// <param name="IsKnown">是否查到了确定结果（false = 未知，界面不该据此禁用订阅）。</param>
/// <param name="IsSubscribed">是否已订阅。</param>
/// <param name="ActivationSetId">使用的播放集 id（未知时为 0）。</param>
public sealed record WorkshopSubscriptionState(
    bool IsSignedIn,
    bool IsKnown,
    bool IsSubscribed,
    long ActivationSetId)
{
    /// <summary>未登录。</summary>
    public static WorkshopSubscriptionState SignInRequired { get; } = new(false, false, false, 0);

    /// <summary>已登录但查不到（接口失败/无播放集）。</summary>
    public static WorkshopSubscriptionState Unknown { get; } = new(true, false, false, 0);
}
