namespace PdxLauncher.App.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「Steam 工坊直连订阅」的宿主实现：每次操作拉起一个短命子进程（<c>--steam-host</c>），
/// 原生 <c>ISteamUGC::SubscribeItem</c> 只在子进程里调。
/// <para>
/// 旧实现（<c>SteamWorkshopBridge</c>）在启动器进程内长期持有 Steamworks 会话，副作用是：
/// Steam 会把启动器当成「vic3 正在运行」，并在用户点「退出游戏」时把启动器杀掉。
/// 换成宿主后，登记只存在于几秒的子进程上，代价是每次操作多一次进程启动。
/// </para>
/// </summary>
public sealed class SteamWorkshopHostBackend : IWorkshopSubscribeBackend, IWorkshopStateSource
{
    /// <summary>Steam 重启窗口内首次 IPC 失败后的重试等待。</summary>
    private static readonly TimeSpan SteamUnavailableRetryDelay = TimeSpan.FromSeconds(2);

    private readonly SteamHostClient _client;
    private readonly IAppLogger _logger;
    private readonly string _apiFilePath;
    private readonly int _appId;

    /// <summary>构造。</summary>
    /// <param name="client">宿主客户端。</param>
    /// <param name="logger">日志。</param>
    /// <param name="apiFilePath">要交给宿主的 <c>steam_api64.dll</c>。</param>
    /// <param name="appId">目标游戏的 Steam appid。</param>
    public SteamWorkshopHostBackend(SteamHostClient client, IAppLogger logger, string apiFilePath, int appId)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _apiFilePath = apiFilePath ?? throw new ArgumentNullException(nameof(apiFilePath));
        _appId = appId;
    }

    /// <summary>已解析的 <c>steam_api64.dll</c> 路径。</summary>
    public string ApiFilePath => _apiFilePath;

    /// <inheritdoc />
    public bool IsAvailable => _client.IsHostAvailable && _appId > 0;

    /// <inheritdoc />
    public string? UnavailableReason { get; private init; }

    /// <summary>
    /// 按当前设置 / 游戏找齐依赖后建一个后端；缺 appid、找不到 DLL 或宿主不可用时返回 null
    /// 并给出中文原因（界面按「在 Steam 中打开」降级）。
    /// </summary>
    public static SteamWorkshopHostBackend? TryCreate(
        SteamHostClient client,
        IAppLogger logger,
        IFileSystem fileSystem,
        SteamApiLocator locator,
        string? settingsPath,
        GameInstallation? game,
        int? appId,
        out string? reason)
    {
        reason = null;
        if (appId is not { } id || id <= 0)
        {
            reason = "不知道这款游戏的 Steam appid";
            return null;
        }

        if (!client.IsHostAvailable)
        {
            reason = "找不到可用的 Steam 宿主程序：" + client.HostPath;
            return null;
        }

        var apiPath = locator.Resolve(settingsPath, AppContext.BaseDirectory, game)
            ?? SteamClientLocator.FindSteamApiDll();
        if (string.IsNullOrWhiteSpace(apiPath))
        {
            reason = "找不到可用的 steam_api64.dll（游戏目录、设置里手填的路径与 Steam 安装目录都没有）";
            return null;
        }

        logger.Info("Steam 工坊直连：使用宿主 " + client.HostPath + "，dll=" + apiPath + "，appid=" +
            id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new SteamWorkshopHostBackend(client, logger, apiPath!, id) { UnavailableReason = null };
    }

    /// <inheritdoc />
    public async Task<WorkshopSubscribeResult> SetSubscribedAsync(ulong publishedFileId, bool subscribe, CancellationToken cancellationToken)
    {
        var verb = subscribe ? SteamHostProtocol.WorkshopSubscribeVerb : SteamHostProtocol.WorkshopUnsubscribeVerb;
        var action = subscribe ? "订阅" : "取消订阅";
        var result = await _client.WorkshopAsync(verb, _apiFilePath, _appId, publishedFileId, cancellationToken)
            .ConfigureAwait(false);

        // Steam 重启 / 切换账号的窗口里，进程还在但 IPC 管道会短暂失效。
        // 订阅和退订都是幂等的，这时再拉起一次宿主通常就能成功；最多只重试一次。
        if (!result.Ok && SteamHostFailureClassifier.ShouldRetry(result.Error, SteamClientProcess.IsRunning()))
        {
            _logger.Warn("Steam 工坊" + action + "首次失败（Steam 客户端可能正在重启）：" + result.Error);
            await Task.Delay(SteamUnavailableRetryDelay, cancellationToken).ConfigureAwait(false);
            result = await _client.WorkshopAsync(verb, _apiFilePath, _appId, publishedFileId, cancellationToken)
                .ConfigureAwait(false);
        }

        if (!result.Ok)
        {
            // 原始错误进日志（可能带 tier0 断言），界面只给可读原因。
            _logger.Warn("Steam 工坊" + action + "失败：" + result.Error);
            return WorkshopSubscribeResult.Fail(DescribeForUser(result.Error));
        }

        return WorkshopSubscribeResult.Success;
    }

    /// <summary>失败原因给用户看的版本：Steam 不可用统一换成资源文案，其余保持原样。</summary>
    private static string DescribeForUser(string? error) =>
        SteamHostFailureClassifier.IsSteamClientUnavailable(error)
            ? Localizer.Instance.T(Keys.WorkshopDetailSteamUnavailable)
            : (error ?? "Steam 没有确认这次操作");

    /// <summary>
    /// 读订阅状态。会同步拉起宿主进程（约 1 秒），调用方必须放到线程池上，别在 UI 线程直接调。
    /// </summary>
    public WorkshopInstallState ReadState(ulong publishedFileId)
    {
        var result = _client.WorkshopAsync(SteamHostProtocol.WorkshopStateVerb, _apiFilePath, _appId, publishedFileId, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        if (!result.Ok || result.State is null)
        {
            _logger.Warn("读取 Steam 工坊订阅状态失败：" + result.Error);
            return WorkshopInstallState.Unknown;
        }

        return result.State;
    }

    /// <inheritdoc />
    public async Task<SteamHostSubscribedResult> ReadSubscribedAsync(CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return SteamHostSubscribedResult.Fail(UnavailableReason ?? "Steam 直连状态不可用");
        }

        var result = await _client.ReadSubscribedAsync(_apiFilePath, _appId, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            _logger.Warn("读取 Steam 工坊订阅列表失败：" + result.Error);
        }
        else
        {
            _logger.Info("Steam 工坊订阅列表：" + result.Items.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) +
                " 项" + (result.Truncated ? "（已截断）" : string.Empty));
        }

        return result;
    }

    /// <inheritdoc />
    public Task<WorkshopBatchItem> TriggerDownloadAsync(ulong publishedFileId, CancellationToken cancellationToken) =>
        RunBatchAsync(WorkshopBatchOps.Download, publishedFileId, cancellationToken);

    /// <inheritdoc />
    public Task<WorkshopBatchItem> SetDisabledLocallyAsync(ulong publishedFileId, bool disabled, CancellationToken cancellationToken) =>
        RunBatchAsync(disabled ? WorkshopBatchOps.Disable : WorkshopBatchOps.Enable, publishedFileId, cancellationToken);

    private async Task<WorkshopBatchItem> RunBatchAsync(string op, ulong publishedFileId, CancellationToken cancellationToken)
    {
        if (!IsAvailable)
        {
            return new WorkshopBatchItem(publishedFileId, false, null, UnavailableReason ?? "Steam 直连状态不可用");
        }

        var request = new WorkshopBatchRequest(op, new[] { publishedFileId });
        var result = await _client.BatchAsync(_apiFilePath, _appId, request, cancellationToken).ConfigureAwait(false);
        if (!result.Ok && result.Items.Count == 0)
        {
            _logger.Warn("Steam 工坊批量动作（" + op + "）失败：" + result.Error);
            return new WorkshopBatchItem(publishedFileId, false, null, result.Error ?? "Steam 没有确认这次操作");
        }

        var item = result.Items.FirstOrDefault(entry => entry.PublishedFileId == publishedFileId)
            ?? new WorkshopBatchItem(publishedFileId, false, null, "Steam 未返回该条目的结果");
        if (!item.Ok)
        {
            _logger.Warn("Steam 工坊批量动作（" + op + "）失败：" + item.Error);
        }

        return item;
    }
}
