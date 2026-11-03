namespace PdxLauncher.App.Services;

using System;
using System.Threading;
using System.Threading.Tasks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「把本地 mod 发布到 Steam 创意工坊」的宿主后端。
/// <para>
/// 与 <see cref="SteamWorkshopHostBackend"/> 一样，所有原生调用都交给短命子进程；
/// 区别只是发布要上传内容，耗时可能到分钟级，所以进度通过回调逐行回传。
/// </para>
/// </summary>
public sealed class ModPublishBackend
{
    private readonly SteamHostClient _client;
    private readonly IAppLogger _logger;
    private readonly string _apiFilePath;
    private readonly int _appId;

    private ModPublishBackend(SteamHostClient client, IAppLogger logger, string apiFilePath, int appId)
    {
        _client = client;
        _logger = logger;
        _apiFilePath = apiFilePath;
        _appId = appId;
    }

    /// <summary>已解析的 <c>steam_api64.dll</c>。</summary>
    public string ApiFilePath => _apiFilePath;

    /// <summary>当前是否具备发布条件。</summary>
    public bool IsAvailable => _client.IsHostAvailable && _appId > 0;

    /// <summary>按设置 / 游戏找齐依赖；缺失时返回 null 并给出中文原因。</summary>
    public static ModPublishBackend? TryCreate(
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
            logger.Warn("Steam 工坊发布不可用：" + reason);
            return null;
        }

        if (!client.IsHostAvailable)
        {
            reason = "找不到可用的 Steam 宿主程序：" + client.HostPath;
            logger.Warn("Steam 工坊发布不可用：" + reason);
            return null;
        }

        var apiPath = locator.Resolve(settingsPath, AppContext.BaseDirectory, game)
            ?? SteamClientLocator.FindSteamApiDll();
        if (string.IsNullOrWhiteSpace(apiPath))
        {
            reason = "找不到可用的 steam_api64.dll（游戏目录、设置里手填的路径与 Steam 安装目录都没有）";
            logger.Warn("Steam 工坊发布不可用：" + reason + "（game=" + (game?.GameId ?? "?") + "）");
            return null;
        }

        logger.Info("Steam 工坊发布：使用宿主 " + client.HostPath + "，dll=" + apiPath + "，appid=" +
            id.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return new ModPublishBackend(client, logger, apiPath!, id);
    }

    /// <summary>执行一次发布；进度回调可能在后台线程触发，调用方自行切回 UI 线程。</summary>
    public Task<ModPublishResult> PublishAsync(
        ModPublishRequest request,
        Action<ModPublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.AppId <= 0)
        {
            return Task.FromResult(ModPublishResult.Fail("发布请求缺少 appid"));
        }

        return _client.PublishAsync(_apiFilePath, _appId, request, progress, cancellationToken);
    }
}
