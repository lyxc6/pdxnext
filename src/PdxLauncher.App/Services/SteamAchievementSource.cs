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

    /// <summary>一次读取最多试几份 DLL（每份要多起 1-2 个宿主进程，不能无限试）。</summary>
    private const int MaxAttempts = 3;

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

        IReadOnlyList<string> candidates;
        try
        {
            candidates = CollectCandidates(appId, game);
        }
        catch (Exception ex)
        {
            _logger.Warn("读取成就失败：收集 steam_api64.dll 候选时出错：" + ex.Message);
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }

        if (candidates.Count == 0)
        {
            _logger.Warn("读取成就失败：找不到可用的 steam_api64.dll（appid " + appId + "）");
            return SteamAchievementMapping.SteamUnavailable(fetchedAt);
        }

        string? replaced = null;
        string? lastCode = null;
        var attempts = 0;
        var attemptedPaths = new List<string>();
        foreach (var candidate in candidates)
        {
            if (attempts >= MaxAttempts)
            {
                break;
            }

            if (SteamApiCandidateBlacklist.TryGetReason(candidate, out var blockedReason))
            {
                _logger.Info("跳过本次运行已标记不可用的候选：" + candidate + "（" + (blockedReason ?? "未知原因") + "）");
                continue;
            }

            attempts++;
            attemptedPaths.Add(candidate);

            if (replaced is not null)
            {
                // 上一份不行（崩溃 / Init 失败），日志里把「从谁换到谁」写清楚，玩家报问题时能直接看出走了哪个文件。
                _logger.Warn("候选 " + replaced + " 不可用，改用 " + candidate);
                replaced = null;
            }

            try
            {
                var check = await _client.ApiCheckRunAsync(candidate, appId, cancellationToken).ConfigureAwait(false);
                if (SteamHostClient.IsCrashExit(check.ExitCode))
                {
                    // 探测阶段就崩（loader 的 DllMain 里常有的访问违例）：同样拉黑、换下一个。
                    SteamApiCandidateBlacklist.MarkBlocked(
                        candidate,
                        "导出探测崩溃（0x" + unchecked((uint)check.ExitCode).ToString("X8", CultureInfo.InvariantCulture) + "）");
                    _logger.Warn(
                        "候选 " + candidate + " 在导出探测时崩溃（退出码 0x" +
                        unchecked((uint)check.ExitCode).ToString("X8", CultureInfo.InvariantCulture) +
                        "），本次运行不再使用");
                    replaced = candidate;
                    continue;
                }

                if (!check.Result.Ok)
                {
                    _logger.Info("跳过不适合作成就的 steam_api64.dll：" + candidate + "（" + (check.Result.Error ?? "未知原因") + "）");
                    continue;
                }

                var run = await _client.AchievementsRunAsync(candidate, appId, cancellationToken).ConfigureAwait(false);
                if (SteamHostClient.IsCrashExit(run.ExitCode))
                {
                    // loader 形态的 DLL 会在 DllMain / 初始化里直接崩掉子进程（0xC0000005），
                    // 这次运行别再拿它试：换下一个候选，并把路径记进进程内黑名单。
                    SteamApiCandidateBlacklist.MarkBlocked(
                        candidate,
                        "宿主崩溃（0x" + unchecked((uint)run.ExitCode).ToString("X8", CultureInfo.InvariantCulture) + "）");
                    _logger.Warn(
                        "候选 " + candidate + " 崩溃（退出码 0x" +
                        unchecked((uint)run.ExitCode).ToString("X8", CultureInfo.InvariantCulture) +
                        "），本次运行不再使用");
                    replaced = candidate;
                    continue;
                }

                var result = run.Result;
                lastCode = result.Code;
                _logger.Info("候选尝试：" + candidate + " → " + result.Code + "（appid " + appId + "）");
                if (!result.Ok && !run.TimedOut && run.ExitCode != 0)
                {
                    // 崩溃之外的「非 0 退出」（加载依赖缺失、Init 里异常退出等）同样说明这份 DLL 不适合；
                    // 换下一个候选，并记进本次运行的黑名单，避免每次刷新都再撞一遍。
                    SteamApiCandidateBlacklist.MarkBlocked(
                        candidate,
                        "宿主异常退出（0x" + unchecked((uint)run.ExitCode).ToString("X8", CultureInfo.InvariantCulture) + "）");
                    _logger.Warn(
                        "候选 " + candidate + " 的宿主异常退出（退出码 0x" +
                        unchecked((uint)run.ExitCode).ToString("X8", CultureInfo.InvariantCulture) +
                        "），本次运行不再使用");
                    replaced = candidate;
                    continue;
                }

                if (SteamAchievementCodes.IsEnvironmentFault(result.Code))
                {
                    // Steam 没启动 / 没登录 / 等回调超时：换哪份 DLL 都一样，继续试只会再多等几轮 Init。
                    if (result is { Error: { Length: > 0 } environmentError })
                    {
                        _logger.Info("Steam 成就未返回（" + result.Code + "）：" + environmentError);
                    }

                    _logger.Info("候选 " + candidate + " 返回 " + result.Code + "：环境问题，换 DLL 无意义，停止尝试");
                    return SteamAchievementMapping.FromHostResult(result, fetchedAt);
                }

                if (SteamAchievementCodes.IsCandidateFault(result.Code))
                {
                    // loader 形态的 DLL 常在 Init 里失败（initFailed），或者根本加载不起来（native）：
                    // 这份文件就是不行，记进本次运行的黑名单再换下一份，最多 MaxAttempts 份。
                    SteamApiCandidateBlacklist.MarkBlocked(candidate, "宿主返回 " + result.Code);
                    _logger.Warn(
                        "候选 " + candidate + " 返回 " + result.Code + "，换下一份候选" +
                        (result.Error is { Length: > 0 } faultError ? "（" + faultError + "）" : string.Empty));
                    replaced = candidate;
                    continue;
                }

                if (result is { Ok: false, Error: { Length: > 0 } error })
                {
                    // 「没有成就」也会走到这里（Code == NoAchievements），它不算错误，但也值得留一条日志。
                    _logger.Info("Steam 成就未返回（" + result.Code + "）：" + error);
                }

                _logger.Info("成就读取使用 steam_api64.dll：" + candidate + "（appid " + appId + "）");

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
                _logger.Warn("候选 " + candidate + " 读取成就失败：" + ex.GetType().Name + "：" + ex.Message);
            }
        }

        _logger.Warn(
            "读取成就失败：已尝试 " + attemptedPaths.Count + " 份 steam_api64.dll（appid " + appId +
            "），最后一次失败码 " + (lastCode ?? "无") +
            (attemptedPaths.Count > 0 ? "，最后路径 " + attemptedPaths[^1] : string.Empty) +
            "；已尝试路径：" + string.Join("；", attemptedPaths));

        // 保留宿主给的具体码（例如全部候选都 initFailed 时仍是 initFailed），
        // 不要退化成含糊的 steamUnavailable：玩家需要区分「Steam 没开」和「DLL 不对」。
        return SteamAchievementMapping.SteamUnavailable(fetchedAt) with { Code = lastCode };
    }

    /// <summary>
    /// 收集本次读取的候选 DLL：定位器给的有序候选 + Steam 客户端自带的那份，
    /// 过滤掉不存在 / 体积异常 / 本次运行已经崩过的；最多留 <see cref="MaxAttempts"/> 份备用。
    /// </summary>
    private List<string> CollectCandidates(int appId, GameInstallation? game)
    {
        var results = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            string full;
            try
            {
                full = _fileSystem.GetFullPath(path!);
            }
            catch (Exception)
            {
                return;
            }

            if (!seen.Add(full))
            {
                return;
            }

            if (!_locator.LooksUsable(full, out var reason))
            {
                _logger.Info("跳过不可用的 steam_api64.dll 候选：" + full + "（" + (reason ?? "未知原因") + "）");
                return;
            }

            results.Add(full);
        }

        foreach (var candidate in _locator.Candidates(_settingsApiPath(), AppContext.BaseDirectory, game))
        {
            Add(candidate);
        }

        // 游戏目录里一份都找不到时，退回 Steam 客户端自带的 dll（注册表里那边通常有）。
        try
        {
            Add(SteamClientLocator.FindSteamApiDll());
        }
        catch (Exception ex)
        {
            _logger.Warn("查找 Steam 客户端自带的 steam_api64.dll 失败（appid " + appId + "）：" + ex.Message);
        }

        return results;
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
            var localization = SteamAchievementLocalization.Load(
                _fileSystem,
                game.InstallRoot,
                language,
                _logger,
                result.Items.Count);
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
