namespace PdxLauncher.App.Services;

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Steam;

/// <summary>
/// 成就数据源的真实实现：拉起「Steam 宿主子进程」读成就，再映射成页面用的快照。
/// <para>
/// 本类**不**加载任何原生库、**不**调 <c>SteamAPI_Init</c>：原生调用全部发生在
/// <c>--steam-host achievements</c> 的子进程里（见 <see cref="SteamHostProtocol.AchievementsVerb"/>）。
/// 这里只做四件事：判断 appid、找 <c>steam_api64.dll</c>、解析宿主那行 JSON、
/// 用游戏自己的 <c>localization/&lt;lang&gt;/*.yml</c> 覆盖显示名/描述。
/// </para>
/// <para>
/// 结果映射遵循契约：未知 appid → <see cref="AchievementAvailability.NotSupported"/>；
/// Steam 没启动 / Init 失败 / 超时 → <see cref="AchievementAvailability.SteamUnavailable"/>；
/// 成功但 0 条 → <see cref="AchievementAvailability.Empty"/>。三者绝不互相混用。
/// </para>
/// </summary>
public sealed class SteamAchievementSource : IAchievementSource
{
    private readonly SteamHostClient _client;
    private readonly SteamApiLocator _locator;
    private readonly IFileSystem _fileSystem;
    private readonly IAppLogger _logger;
    private readonly Func<string?> _settingsApiPath;
    private readonly Func<GameInstallation?> _currentGame;
    private readonly Func<string?> _uiLanguage;

    /// <summary>构造。</summary>
    /// <param name="client">宿主客户端。</param>
    /// <param name="locator"><c>steam_api64.dll</c> 定位器。</param>
    /// <param name="fileSystem">文件系统（读游戏本地化）。</param>
    /// <param name="logger">日志。</param>
    /// <param name="settingsApiPath">取「用户手填的 DLL 路径」；每次调用时现取，设置改了立即生效。</param>
    /// <param name="currentGame">取当前游戏（用于在游戏目录里找 DLL 和本地化）；每次调用时现取。</param>
    /// <param name="uiLanguage">取启动器界面语言（语言回退链的第二级）；为空时读 <see cref="CultureInfo.DefaultThreadCurrentUICulture"/>。</param>
    public SteamAchievementSource(
        SteamHostClient client,
        SteamApiLocator locator,
        IFileSystem fileSystem,
        IAppLogger logger,
        Func<string?> settingsApiPath,
        Func<GameInstallation?> currentGame,
        Func<string?>? uiLanguage = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settingsApiPath = settingsApiPath ?? throw new ArgumentNullException(nameof(settingsApiPath));
        _currentGame = currentGame ?? throw new ArgumentNullException(nameof(currentGame));
        _uiLanguage = uiLanguage ?? (() => CultureInfo.DefaultThreadCurrentUICulture?.Name);
    }

    /// <inheritdoc />
    public async Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken)
    {
        var fetchedAt = DateTimeOffset.UtcNow;

        if (!GameSteamAppIds.TryGet(gameId, out var appId) || appId <= 0)
        {
            return SteamAchievementMapping.NotSupported(fetchedAt);
        }

        if (!_client.IsHostAvailable)
        {
            _logger.Warn("读取成就失败：找不到 Steam 宿主程序：" + _client.HostPath);
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }

        // 只取一次：定位 DLL 和读本地化必须是同一款游戏，避免 await 期间切游戏造成错配。
        var game = _currentGame();

        string? apiPath;
        try
        {
            apiPath = _locator.Resolve(_settingsApiPath(), AppContext.BaseDirectory, game)
                ?? SteamClientLocator.FindSteamApiDll();
        }
        catch (Exception ex)
        {
            _logger.Warn("读取成就失败：定位 steam_api64.dll 时出错：" + ex.Message);
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }

        if (string.IsNullOrWhiteSpace(apiPath))
        {
            _logger.Warn("读取成就失败：找不到可用的 steam_api64.dll（appid " + appId + "）");
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }

        try
        {
            var result = await _client.AchievementsAsync(apiPath!, appId, cancellationToken).ConfigureAwait(false);
            if (result is { Ok: false, Error: { Length: > 0 } error })
            {
                // 「没有成就」也会走到这里（Code == NoAchievements），它不算错误，但也值得留一条日志。
                _logger.Info("Steam 成就未返回（" + result.Code + "）：" + error);
            }

            var localization = result is { Ok: true } ? LoadLocalization(game, result, appId) : null;
            return SteamAchievementMapping.FromHostResult(result, fetchedAt, localization);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 切游戏 / 离开页面导致的取消由协调器吞掉，这里必须原样抛出，
            // 否则旧请求会被当成「Steam 不可用」写进新一轮的界面状态。
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("读取成就失败（appid " + appId + "）：" + ex.GetType().Name + "：" + ex.Message);
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }
    }

    /// <summary>读游戏本地化；任何异常都只记日志并返回 null，绝不影响成就本身是否可用。</summary>
    private IReadOnlyDictionary<string, string>? LoadLocalization(
        GameInstallation? game,
        SteamHostAchievementsResult result,
        int appId)
    {
        if (game is null || string.IsNullOrWhiteSpace(game.InstallRoot))
        {
            return null;
        }

        try
        {
            var language = SteamAchievementLocalization.ResolveLanguage(_fileSystem, game.GameDataDir, _uiLanguage());
            var localization = SteamAchievementLocalization.Load(_fileSystem, game.InstallRoot, language, _logger);
            if (localization.Count > 0)
            {
                var total = result.Items.Count;
                var hits = 0;
                foreach (var item in result.Items)
                {
                    if (SteamAchievementLocalization.Name(localization, item.Name) is not null)
                    {
                        hits++;
                    }
                }

                _logger.Info("成就本地化：语言 " + language + "，命中 " + hits + "/" + total + "（appid " + appId + "）");
            }

            return localization;
        }
        catch (Exception ex)
        {
            _logger.Warn("读取成就本地化失败，保留 Steam 显示名（appid " + appId + "）：" + ex.GetType().Name + "：" + ex.Message);
            return null;
        }
    }
}
