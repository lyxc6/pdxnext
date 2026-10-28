namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Mods;

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

    private Dictionary<string, long>? _cache;

    /// <summary>构造。</summary>
    /// <param name="pdx">PDX Mods 客户端（含订阅写接口）。</param>
    /// <param name="account">账号服务（提供登录会话）。</param>
    /// <param name="fileSystem">文件系统（读写播放集映射缓存）。</param>
    /// <param name="dataRoot">数据根目录。</param>
    /// <param name="logger">日志。</param>
    public WorkshopSubscriptionService(
        PdxModsClient pdx,
        AccountService account,
        IFileSystem fileSystem,
        string dataRoot,
        IAppLogger logger)
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
            var playSet = await ResolvePlaySetAsync(gameName, cancellationToken).ConfigureAwait(true);
            if (playSet is null)
            {
                return WorkshopSubscriptionState.Unknown;
            }

            var mods = await _pdx
                .GetPlaySetModsAsync(playSet.Id, _account.Session, cancellationToken)
                .ConfigureAwait(true);

            if (!mods.IsSuccess || mods.Value is null)
            {
                return WorkshopSubscriptionState.Unknown with { ActivationSetId = playSet.Id };
            }

            var subscribed = mods.Value.Any(mod =>
                string.Equals(mod.ModId, modId, StringComparison.Ordinal));
            return new WorkshopSubscriptionState(
                IsSignedIn: true,
                IsKnown: true,
                IsSubscribed: subscribed,
                ActivationSetId: playSet.Id);
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
            var playSet = await ResolvePlaySetAsync(gameName, cancellationToken).ConfigureAwait(true);
            if (playSet is null)
            {
                return Core.Contracts.AppResult<bool>.Fail(
                    Core.Contracts.ErrorKind.Io,
                    "workshop-subscription:no-playset");
            }

            var result = subscribe
                ? await _pdx
                    .SubscribeAsync(playSet.Id, modId, version, _account.Session, cancellationToken)
                    .ConfigureAwait(true)
                : await _pdx
                    .UnsubscribeAsync(playSet.Id, modId, _account.Session, cancellationToken)
                    .ConfigureAwait(true);

            if (result.IsSuccess)
            {
                _logger.Info(
                    (subscribe ? "已订阅" : "已退订") + " PDX mod: " + modId +
                    "（播放集 " + playSet.Id.ToString(CultureInfo.InvariantCulture) + "）");
            }

            return result;
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

    /// <summary>找到这个游戏的播放集；没有就建一个并写缓存。登录缺失或接口失败返回 null。</summary>
    private async Task<PdxPlaySet?> ResolvePlaySetAsync(string gameName, CancellationToken cancellationToken)
    {
        var cache = LoadCache();
        if (cache.TryGetValue(gameName, out var cached) && cached > 0)
        {
            return new PdxPlaySet(cached, PlaySetName, gameName, null);
        }

        var sets = await _pdx
            .GetPlaySetsAsync(gameName, _account.Session, cancellationToken)
            .ConfigureAwait(true);

        if (!sets.IsSuccess || sets.Value is null)
        {
            return null;
        }

        var existing = sets.Value.FirstOrDefault();
        if (existing is not null)
        {
            cache[gameName] = existing.Id;
            SaveCache(cache);
            return existing;
        }

        var created = await _pdx
            .CreatePlaySetAsync(gameName, PlaySetName, _account.Session, cancellationToken)
            .ConfigureAwait(true);

        if (!created.IsSuccess || created.Value is null)
        {
            return null;
        }

        _logger.Info(
            "已在 PDX 账号里创建播放集 " + PlaySetName +
            "（" + created.Value.Id.ToString(CultureInfo.InvariantCulture) + "）用于订阅");
        cache[gameName] = created.Value.Id;
        SaveCache(cache);
        return created.Value;
    }

    private Dictionary<string, long> LoadCache()
    {
        if (_cache is not null)
        {
            return _cache;
        }

        var cache = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (_fileSystem.FileExists(_cachePath))
            {
                var json = _fileSystem.ReadAllText(_cachePath);
                if (JsonNode.Parse(json) is JsonObject root && root["games"] is JsonObject games)
                {
                    foreach (var pair in games)
                    {
                        if (pair.Value is JsonValue value && value.TryGetValue<long>(out var setId) && setId > 0)
                        {
                            cache[pair.Key] = setId;
                        }
                    }
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // 缓存坏了不影响功能：重新解析播放集即可。
            _logger.Warn("读取播放集缓存失败，忽略: " + ex.Message);
        }

        _cache = cache;
        return cache;
    }

    private void SaveCache(Dictionary<string, long> cache)
    {
        try
        {
            var games = new JsonObject();
            foreach (var pair in cache)
            {
                games[pair.Key] = pair.Value;
            }

            var root = new JsonObject { ["games"] = games };
            _fileSystem.WriteAllTextAtomic(_cachePath, root.ToJsonString());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warn("写入播放集缓存失败: " + ex.Message);
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
