namespace PdxLauncher.App.Services;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Steam;

/// <summary>
/// 组装官方新版公告所需的上下文：平台取游戏配置，启动次数取官方启动器 userSettings.json，
/// 已拥有 DLC 取本地扫描 + Steam 授权报告。任何一步失败都只记日志，不阻断公告显示。
/// </summary>
internal sealed class NewsFeedContextProvider(
    IAppLogger logger,
    IDlcCatalog dlcCatalog,
    ISteamOwnershipService ownership)
{
    internal async Task<NewsFeedRequest> CreateAsync(
        GameInstallation game,
        bool allowOnline,
        bool forceRefresh,
        CancellationToken cancellationToken)
    {
        var platform = string.IsNullOrWhiteSpace(game.DistributionPlatform) ? "pdx" : game.DistributionPlatform;
        var launchCount = ReadLaunchCount(game.GameId);
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var dlcs = await dlcCatalog.DiscoverAsync(game, cancellationToken).ConfigureAwait(false);
            foreach (var dlc in dlcs)
            {
                if (!string.IsNullOrWhiteSpace(dlc.PopsId))
                {
                    owned.Add(dlc.PopsId);
                }

                if (!string.IsNullOrWhiteSpace(dlc.SteamId))
                {
                    owned.Add(dlc.SteamId);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warn("公告 DLC 上下文（本地清单）读取失败：" + exception.Message);
        }

        try
        {
            var report = ownership.Inspect(game, cancellationToken);
            foreach (var dlc in SteamDlcListBuilder.FromOwnership(report.Dlcs))
            {
                if (!string.IsNullOrWhiteSpace(dlc.SteamId))
                {
                    owned.Add(dlc.SteamId);
                }
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.Warn("公告 DLC 上下文（Steam 授权）读取失败：" + exception.Message);
        }

        return new NewsFeedRequest(game, platform, launchCount, owned, allowOnline, forceRefresh);
    }

    private int ReadLaunchCount(string gameId)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var path = Path.Combine(appData, "Paradox Interactive", "launcher-v2", "userSettings.json");
            if (!File.Exists(path))
            {
                return 0;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.TryGetProperty("counter", out var counter)
                && counter.ValueKind == JsonValueKind.Object
                && counter.TryGetProperty(gameId, out var value)
                && value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var result))
            {
                return result;
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            logger.Warn("公告启动次数读取失败：" + exception.Message);
        }

        return 0;
    }
}
