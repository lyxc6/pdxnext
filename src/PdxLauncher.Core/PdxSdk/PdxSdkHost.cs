namespace PdxLauncher.Core.PdxSdk;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets.PlaysetConfig;
using PdxConfig = PDX.SDK.Contracts.Configuration.Config;
using PdxContext = PDX.SDK.Context;
using PdxEcosystem = PDX.SDK.Contracts.Enums.Ecosystem;
using PdxEnvironment = PDX.SDK.Contracts.Enums.BackendEnvironment;
using PdxLanguage = PDX.SDK.Contracts.Enums.Language;
using PdxLogLevel = PDX.SDK.Contracts.Enums.LogLevel;
using PdxPlatform = PDX.SDK.Contracts.Enums.Platform;
using PdxResult = PDX.SDK.Contracts.Result;

/// <summary>
/// 官方 <c>PDX.SDK</c> 的薄封装：负责建 <see cref="PdxConfig"/>、复用现有会话、暴露
/// PDX Mods 的订阅/退订/播放集读取。
/// <para>
/// 只在这里接触 SDK 类型，上层（<c>WorkshopSubscriptionService</c> 等）看到的是本项目自己的
/// <see cref="PdxSdkSubscriptionResult"/> / <see cref="PdxSdkPlaySetState"/>，避免 SDK 内部类型
/// 泄漏到 App 层。SDK 是 <c>net48</c> 程序集，调用前请确保不在 UI 线程上（它内部有同步
/// 上下文依赖）。
/// </para>
/// </summary>
public sealed class PdxSdkHost
{
    private readonly PdxSdkOptions _options;
    private readonly IAppLogger _logger;
    private readonly string _userAgent;
    private readonly GameInstallation? _game;
    private readonly object _sync = new();
    private PDX.SDK.Contracts.IContext? _context;
    private bool _startupDone;
    private bool _loggedIn;

    /// <summary>构造（不初始化 SDK，首次调用方法时才建 <c>Context</c>）。</summary>
    /// <param name="options">路径、账号与游戏信息。</param>
    /// <param name="logger">本项目日志。</param>
    /// <param name="appVersion">本启动器版本，写进 User-Agent。</param>
    public PdxSdkHost(
        PdxSdkOptions options,
        IAppLogger logger,
        string? appVersion = null,
        GameInstallation? game = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.PlatformGameId);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.ModsRootPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DiskIORoot);

        _options = options;
        _logger = logger;
        _userAgent = "PdxNext/" + (string.IsNullOrWhiteSpace(appVersion) ? "dev" : appVersion.Trim());
        _game = game;
    }

    /// <summary>SDK 是否已经有可用的登录态（<c>Account.Startup()</c> 成功）。</summary>
    public bool IsLoggedIn
    {
        get
        {
            lock (_sync)
            {
                return _loggedIn;
            }
        }
    }

    /// <summary>当前使用的 SDK 状态目录。</summary>
    public string DiskIORoot => _options.DiskIORoot;

    /// <summary>
    /// 按 Skyve 的 <c>WorkshopService.Initialize()</c> 构造 <see cref="PdxConfig"/>。
    /// 独立成静态方法便于自检：不初始化 SDK 也能断言路径、头、Ecosystem 等关键字段。
    /// </summary>
    /// <param name="options">建 host 的输入。</param>
    /// <param name="logger">本项目日志（转成 SDK 的 logger）。</param>
    /// <param name="appVersion">版本号（用于 User-Agent）。</param>
    public static PdxConfig BuildConfig(PdxSdkOptions options, IAppLogger logger, string? appVersion = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        var config = new PdxConfig
        {
            Language = PdxLanguage.en,
            GameVersion = options.GameVersion ?? string.Empty,
            Logger = new PdxSdkLoggerAdapter(logger),
            DiskIORoot = options.DiskIORoot,
            Environment = PdxEnvironment.Live,
            Ecosystem = PdxEcosystem.Steam,
            UserIdType = "steam",
            LogLevel = PdxLogLevel.L2_Warning,
            DefaultHeaders = new Dictionary<string, string>
            {
                ["User-Agent"] = "PdxNext/" + (string.IsNullOrWhiteSpace(appVersion) ? "dev" : appVersion.Trim()),
            },
            // 本启动器不做 Paradox 遥测/年龄检查；合法文档由我们自己的界面负责。
            StandardTelemetryEnabled = false,
            SkipLegalChecksAndEnableTelemetry = false,
        };
        config.Mods.UsePatching = true;
        config.Mods.RootPath = options.ModsRootPath;
        return config;
    }

    /// <summary>
    /// 确保 SDK 已初始化并尝试复用会话。可重复调用；失败会抛异常（调用方转成用户可读提示）。
    /// 必须在后台线程调用。
    /// </summary>
    public async Task EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            if (_context is not null && _startupDone)
            {
                return;
            }
        }

        // 先把会话镜像写进 SDK 的 database.json，再建 Context：Startup() 会优先读它。
        if (_options.Session is { } session)
        {
            try
            {
                PdxSdkSessionBridge.WriteSession(_options.DiskIORoot, session, _options.PlatformGameId);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.Warn("写入 PDX.SDK 会话缓存失败（将退回重新登录）: " + ex.Message);
            }
        }

        var config = BuildConfig(_options, _logger, _userAgent["PdxNext/".Length..]);
        var context = await PdxContext
            .Create(PdxPlatform.Windows, _options.PlatformGameId, config)
            .ConfigureAwait(false);

        var startup = await context.Account.Startup().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            _context = context;
            _startupDone = true;
            _loggedIn = startup?.IsLoggedIn == true;
        }

        _logger.Info(
            "PDX.SDK 已初始化（namespace=" + _options.PlatformGameId +
            "，root=" + _options.DiskIORoot +
            "，登录=" + (_loggedIn ? "是" : "否") + "）");
    }

    /// <summary>用邮箱密码登录（仅在会话复用失败时作为兜底；不落盘，交给调用方保存）。</summary>
    /// <param name="email">邮箱。</param>
    /// <param name="password">密码。</param>
    public async Task<bool> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var context = GetContextOrThrow();

        var result = await context.Account
            .Login(new PDX.SDK.Contracts.Credential.EmailAndPasswordCredential(email, password))
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var ok = result?.Success == true;
        lock (_sync)
        {
            _loggedIn = ok;
        }

        if (!ok)
        {
            _logger.Warn("PDX.SDK 登录失败: " + Describe(result));
        }

        return ok;
    }

    /// <summary>登出并清掉 SDK 的会话缓存。</summary>
    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        if (_context is { } context)
        {
            try
            {
                await context.Account.Logout().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.Warn("PDX.SDK 登出失败: " + ex.Message);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        PdxSdkSessionBridge.Remove(_options.DiskIORoot);
        lock (_sync)
        {
            _loggedIn = false;
        }
    }

    /// <summary>
    /// 读当前激活播放集（服务端整数 id + 本地是否有对应缓存）。没有激活播放集时返回
    /// <see cref="PdxSdkPlaySetState.NotActive"/>，而不是抛异常。
    /// </summary>
    public async Task<PdxSdkPlaySetState> GetActivePlaySetAsync(CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var context = GetContextOrThrow();

        if (_game is not null && TryReadGameActivePlaySetName(out var activeName))
        {
            var resolved = await ResolveSdkPlaySetByNameAsync(context, activeName, cancellationToken).ConfigureAwait(false);
            if (resolved is not null)
            {
                return resolved;
            }
        }

        var result = await context.Mods.GetActivePlayset().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
        {
            return PdxSdkPlaySetState.NotActive;
        }

        if (!result.Success)
        {
            // “没有激活播放集”是 SDK 的本地错误，界面要提示用户先激活；其它错误按失败处理。
            return result.Error?.Details?.Contains("no active playset", StringComparison.OrdinalIgnoreCase) == true
                ? PdxSdkPlaySetState.NotActive
                : PdxSdkPlaySetState.Failed(Describe(result));
        }

        return result.PlaysetId > 0
            ? PdxSdkPlaySetState.Active(result.PlaysetId)
            : PdxSdkPlaySetState.NotActive;
    }

    /// <summary>
    /// 读某个播放集里是否已有这个 mod（服务端视角）。用于详情页订阅状态，不触发下载。
    /// </summary>
    /// <param name="playSetId">服务端播放集 id。</param>
    /// <param name="modId">平台 mod id。</param>
    public async Task<bool?> IsModInPlaySetAsync(int playSetId, string modId, CancellationToken cancellationToken = default)
    {
        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var context = GetContextOrThrow();

        var result = await context.Mods
            .ListModsInPlayset(playSetId, limit: 500, page: 1, includeOnline: false)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null || !result.Success || result.Mods is null)
        {
            _logger.Warn("读取 PDX 播放集 mod 列表失败: " + Describe(result));
            return null;
        }

        return result.Mods.Any(mod => ModIdMatches(mod, modId));
    }

    /// <summary>
    /// 订阅一个 PDX mod：服务端登记 + 下载落盘 + 写入播放集并启用（与 Skyve 的
    /// <c>ModsSubscriptionService.Subscribe</c> 一致）。
    /// </summary>
    /// <param name="playSetId">服务端播放集 id。</param>
    /// <param name="modId">平台 mod id（纯数字）。</param>
    /// <param name="version">要固定的版本；null 跟随最新。</param>
    public async Task<PdxSdkSubscriptionResult> SubscribeAsync(
        int playSetId,
        string modId,
        int? version,
        CancellationToken cancellationToken = default)
    {
        if (playSetId <= 1)
        {
            return PdxSdkSubscriptionResult.Fail("pdx-sdk:playset-invalid");
        }

        if (!int.TryParse(modId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var numericId) || numericId <= 0)
        {
            return PdxSdkSubscriptionResult.Fail("pdx-sdk:mod-id-invalid");
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var context = GetContextOrThrow();

        var result = await context.Mods
            .Subscribe(numericId, playSetId, version is > 0 ? version.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null)
        {
            return PdxSdkSubscriptionResult.Fail("pdx-sdk:no-result");
        }

        var subscribed = result.ModsSubscribedStatus is { Count: > 0 }
            ? result.ModsSubscribedStatus.All(status => status.IsSubscribed)
            : result.Success;

        if (!result.Success || !subscribed)
        {
            _logger.Warn("PDX.SDK 订阅失败: " + Describe(result));
            return PdxSdkSubscriptionResult.Fail(Describe(result));
        }

        _logger.Info("PDX.SDK 已订阅 mod " + modId + "（播放集 " + playSetId + "）");
        return PdxSdkSubscriptionResult.Success(result.PlaysetId);
    }

    /// <summary>退订一个 PDX mod（服务端移除 + 本地清理由 SDK 完成）。</summary>
    /// <param name="playSetId">服务端播放集 id。</param>
    /// <param name="modId">平台 mod id（纯数字）。</param>
    public async Task<PdxSdkSubscriptionResult> UnsubscribeAsync(
        int playSetId,
        string modId,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(modId, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var numericId) || numericId <= 0)
        {
            return PdxSdkSubscriptionResult.Fail("pdx-sdk:mod-id-invalid");
        }

        await EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        var context = GetContextOrThrow();

        var result = await context.Mods.Unsubscribe(numericId, playSetId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (result is null || !result.Success)
        {
            _logger.Warn("PDX.SDK 退订失败: " + Describe(result));
            return PdxSdkSubscriptionResult.Fail(Describe(result));
        }

        _logger.Info("PDX.SDK 已退订 mod " + modId + "（播放集 " + playSetId + "）");
        return PdxSdkSubscriptionResult.Success(playSetId);
    }

    /// <summary>关闭 SDK（进程退出前调用）。</summary>
    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        PDX.SDK.Contracts.IContext? context;
        lock (_sync)
        {
            context = _context;
            _context = null;
            _startupDone = false;
            _loggedIn = false;
        }

        if (context is null)
        {
            return;
        }

        try
        {
            await context.Shutdown().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.Warn("关闭 PDX.SDK 失败: " + ex.Message);
        }

        cancellationToken.ThrowIfCancellationRequested();
    }

    private PDX.SDK.Contracts.IContext GetContextOrThrow()
    {
        lock (_sync)
        {
            return _context ?? throw new InvalidOperationException("PDX.SDK 尚未初始化");
        }
    }

    /// <summary>读游戏 <c>playset_config.json</c> 里激活播放集的名字；没有或读不到返回 false。</summary>
    private bool TryReadGameActivePlaySetName(out string name)
    {
        name = string.Empty;
        try
        {
            var path = PlaysetConfigPaths.GetConfigPath(_game!);
            if (!File.Exists(path))
            {
                return false;
            }

            var document = PlaysetConfigDocument.Parse(File.ReadAllText(path));
            if (document.ActivePlaySetId is not { } activeId
                || document.FindPlaySet(activeId) is not { } playSet)
            {
                return false;
            }

            name = PlaysetConfigDocument.GetName(playSet);
            return !string.IsNullOrWhiteSpace(name);
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or IOException or UnauthorizedAccessException)
        {
            _logger.Warn("读取游戏播放集配置失败: " + ex.Message);
            return false;
        }
    }

    /// <summary>
    /// 按名字在服务端播放集里找同名集（没有就新建），并让 SDK 的激活项指向它，
    /// 使订阅落回用户正在用的那个播放集。
    /// </summary>
    private async Task<PdxSdkPlaySetState?> ResolveSdkPlaySetByNameAsync(
        PDX.SDK.Contracts.IContext context,
        string name,
        CancellationToken cancellationToken)
    {
        var all = await context.Mods.ListAllPlaysets().ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        if (all is not { Success: true } || all.AllPlaysets is null)
        {
            _logger.Warn("读取 PDX 服务端播放集列表失败: " + Describe(all));
            return null;
        }

        var match = all.AllPlaysets.FirstOrDefault(set =>
            string.Equals(set.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var created = await context.Mods.CreatePlayset(name).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (created is not { Success: true } || created.PlaysetId <= 0)
            {
                _logger.Warn("在 PDX 服务端创建播放集失败（" + name + "）: " + Describe(created));
                return null;
            }

            _logger.Info(
                "已在 PDX 服务端创建同名播放集 " + name + "（" +
                created.PlaysetId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）");
            match = new PDX.SDK.Contracts.Service.Mods.Models.Playset
            {
                PlaysetId = created.PlaysetId,
                Name = name,
            };
        }

        var activate = await context.Mods.ActivatePlayset(match.PlaysetId).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (activate is not { Success: true })
        {
            _logger.Warn("激活 PDX 播放集失败（" + match.PlaysetId + "）: " + Describe(activate));
            return null;
        }

        return PdxSdkPlaySetState.Active(match.PlaysetId);
    }

    private static bool ModIdMatches(PDX.SDK.Contracts.Service.Mods.Models.IPlaysetMod mod, string modId)
    {
        // IPlaysetMod 没有直接的 Id 字段：已订阅条目实现里用 LocalData 或 DisplayName 区分，
        // 统一从运行时类型读 int Id（PlaysetSubscribedMod / PlaysetUnmanagedMod 都带）。
        if (mod is null)
        {
            return false;
        }

        var idProperty = mod.GetType().GetProperty("Id");
        if (idProperty?.GetValue(mod) is int id && id > 0)
        {
            return id.ToString(System.Globalization.CultureInfo.InvariantCulture) == modId;
        }

        return string.Equals(mod.DisplayName, modId, StringComparison.Ordinal);
    }

    private static string Describe(PdxResult? result)
    {
        if (result is null)
        {
            return "pdx-sdk:no-result";
        }

        if (result.Success)
        {
            return "ok";
        }

        return result.Error is null
            ? "pdx-sdk:failed"
            : result.Error.ToString();
    }
}

/// <summary>当前激活播放集的读取结果。</summary>
/// <param name="Status">状态。</param>
/// <param name="PlaySetId">服务端播放集 id；非 <see cref="PdxSdkPlaySetStatus.Active"/> 时为 0。</param>
/// <param name="ErrorText">失败原因。</param>
public sealed record PdxSdkPlaySetState(PdxSdkPlaySetStatus Status, int PlaySetId, string? ErrorText)
{
    /// <summary>有激活播放集。</summary>
    public static PdxSdkPlaySetState Active(int playSetId) => new(PdxSdkPlaySetStatus.Active, playSetId, null);

    /// <summary>没有激活播放集。</summary>
    public static PdxSdkPlaySetState NotActive { get; } = new(PdxSdkPlaySetStatus.NotActive, 0, null);

    /// <summary>读取失败。</summary>
    public static PdxSdkPlaySetState Failed(string errorText) => new(PdxSdkPlaySetStatus.Failed, 0, errorText);
}

/// <summary>激活播放集的状态。</summary>
public enum PdxSdkPlaySetStatus
{
    /// <summary>有激活播放集。</summary>
    Active,

    /// <summary>没有激活播放集（用户需要先在播放集页激活一个）。</summary>
    NotActive,

    /// <summary>读取失败。</summary>
    Failed,
}

/// <summary>订阅/退订的结果。</summary>
/// <param name="Ok">是否成功。</param>
/// <param name="PlaySetId">实际作用的播放集 id。</param>
/// <param name="ErrorText">失败原因。</param>
public sealed record PdxSdkSubscriptionResult(bool Ok, int PlaySetId, string? ErrorText)
{
    /// <summary>成功。</summary>
    public static PdxSdkSubscriptionResult Success(int playSetId) => new(true, playSetId, null);

    /// <summary>失败。</summary>
    public static PdxSdkSubscriptionResult Fail(string errorText) => new(false, 0, errorText);
}
