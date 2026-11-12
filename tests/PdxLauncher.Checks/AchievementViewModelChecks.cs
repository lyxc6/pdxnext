namespace PdxLauncher.Checks;

using System.Globalization;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Achievements;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就 ViewModel 的「缓存先渲染、后台失败不回滚」自检：用真实磁盘缓存 + fake IAchievementSource，
/// 钉住两件事——刷新返回 SteamUnavailable / NotSupported 时列表不能被清空，
/// 「上次更新」也不能被刷成现在（否则用户会以为失败那次也更新成功了）。
/// VM 通过 csproj 链接进检查工程，LocalizedObservableObject 用同名最小替身。
/// </summary>
public static class AchievementViewModelChecks
{
    private const string AppId = "529340";
    private static readonly DateTimeOffset CachedFetchedAt = new(2024, 1, 2, 3, 4, 5, TimeSpan.Zero);

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-achvm-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new AchievementSnapshotCache(fs, Path.Combine(tempRoot, "cache"));
            var game = Game(Path.Combine(tempRoot, "vic3"));
            var cached = new AchievementSnapshot
            {
                Availability = AchievementAvailability.Available,
                FetchedAtUtc = CachedFetchedAt,
                Entries =
                [
                    new AchievementEntry("OLD_ACH", "旧成就", "来自缓存", false, true, CachedFetchedAt, 12.5, null),
                ],
            };
            if (!ChecksContext.Check("成就 VM: 预置缓存成功", cache.TrySave(AppId, cached)))
            {
                return 1;
            }

            var cachePath = cache.PathFor(AppId);
            ChecksContext.Check("成就 VM: 缓存文件存在", cachePath is not null && File.Exists(cachePath), cachePath ?? "null");
            var cacheTextBefore = cachePath is null ? string.Empty : File.ReadAllText(cachePath);

            CheckFailureKeepsCache(fs, cache, game, AchievementAvailability.SteamUnavailable, "SteamUnavailable", cacheTextBefore);
            CheckFailureKeepsCache(fs, cache, game, AchievementAvailability.NotSupported, "NotSupported", cacheTextBefore);
            CheckNoCacheFailureShowsReason(fs, tempRoot, game);
        }
        finally
        {
            try
            {
                if (fs.DirectoryExists(tempRoot))
                {
                    fs.Delete(tempRoot);
                }
            }
            catch (IOException)
            {
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckFailureKeepsCache(
        IFileSystem fs,
        AchievementSnapshotCache cache,
        GameInstallation game,
        AchievementAvailability failure,
        string label,
        string cacheTextBefore)
    {
        var pending = new TaskCompletionSource<AchievementSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = new ScriptedSource(pending.Task);
        var viewModel = new AchievementsViewModel(source, cache);

        var load = viewModel.LoadAsync(game, CancellationToken.None);

        // 缓存必须在等待数据源之前就渲染出来（首屏不等子进程）。
        ChecksContext.Check(
            "成就 VM(" + label + "): 等待刷新时先渲染缓存",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "OLD_ACH",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("成就 VM(" + label + "): 缓存渲染期间 IsBusy", viewModel.IsBusy);
        var lastUpdatedBefore = viewModel.LastUpdatedText;
        ChecksContext.Check("成就 VM(" + label + "): 缓存自带上次更新时间", viewModel.HasLastUpdated && lastUpdatedBefore.Length > 0, lastUpdatedBefore);

        pending.SetResult(new AchievementSnapshot
        {
            Availability = failure,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Entries = [],
        });
        load.GetAwaiter().GetResult();

        ChecksContext.Check(
            "成就 VM(" + label + "): 刷新失败不清空缓存列表",
            viewModel.Achievements.Count == 1 && viewModel.Achievements[0].Id == "OLD_ACH",
            "rows=" + viewModel.Achievements.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            "成就 VM(" + label + "): 刷新失败不把可用性改成失败",
            viewModel.Availability == AchievementAvailability.Available,
            viewModel.Availability.ToString());
        ChecksContext.Equal("成就 VM(" + label + "): 上次更新时间不刷新", lastUpdatedBefore, viewModel.LastUpdatedText);
        ChecksContext.Check("成就 VM(" + label + "): 失败后 IsBusy 关闭", !viewModel.IsBusy);
        ChecksContext.Check("成就 VM(" + label + "): 仍算有快照", viewModel.HasSnapshot);

        var cacheTextAfter = cache.PathFor(AppId) is { } path && File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        ChecksContext.Check(
            "成就 VM(" + label + "): 失败不重写缓存文件",
            string.Equals(cacheTextBefore, cacheTextAfter, StringComparison.Ordinal));
        ChecksContext.Check("成就 VM(" + label + "): 数据源只调用一次", source.Calls == 1, source.Calls.ToString(CultureInfo.InvariantCulture));
    }

    private static void CheckNoCacheFailureShowsReason(IFileSystem fs, string tempRoot, GameInstallation game)
    {
        var emptyCache = new AchievementSnapshotCache(fs, Path.Combine(tempRoot, "empty-cache"));
        var pending = new TaskCompletionSource<AchievementSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        var viewModel = new AchievementsViewModel(new ScriptedSource(pending.Task), emptyCache);

        var load = viewModel.LoadAsync(game, CancellationToken.None);
        ChecksContext.Check("成就 VM(无缓存): 刷新中统计显示破折号", viewModel.CountsText == "—", viewModel.CountsText);
        ChecksContext.Check("成就 VM(无缓存): 刷新中不显示缓存快照", !viewModel.HasSnapshot && !viewModel.HasLastUpdated);

        pending.SetResult(new AchievementSnapshot
        {
            Availability = AchievementAvailability.SteamUnavailable,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Entries = [],
        });
        load.GetAwaiter().GetResult();

        ChecksContext.Check(
            "成就 VM(无缓存): 失败后显示 Steam 不可用",
            viewModel.Availability == AchievementAvailability.SteamUnavailable,
            viewModel.Availability.ToString());
        ChecksContext.Check("成就 VM(无缓存): 失败后显示空状态原因", viewModel.ShowEmptyState && viewModel.EmptyStateMessage.Length > 0);
    }

    private static GameInstallation Game(string installRoot) => new(
        GameId: "victoria3",
        DisplayName: "Victoria 3",
        Version: "test",
        InstallRoot: installRoot,
        LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: Path.Combine(installRoot, "game"),
        DlcPath: Path.Combine(installRoot, "dlc"),
        ModDir: Path.Combine(installRoot, "mod"),
        ExePath: Path.Combine(installRoot, "binaries", "victoria3.exe"),
        ExeArgs: Array.Empty<string>(),
        Alternatives: Array.Empty<AlternativeExecutable>(),
        Theme: new ThemeSpec(
            Background: null,
            Logo: null,
            AppIcon: null,
            AppTaskbarIcon: null,
            ButtonBackground: null,
            ButtonFont: null,
            ButtonTextColor: "#FFFFFF",
            ButtonHoverTextColor: "#FFFFFF",
            FontSize: 14,
            FontWeight: 400,
            TextShadow: null,
            Source: ThemeAssetSource.BuiltInPlaceholder),
        BrowserDlcUrl: null,
        BrowserModUrl: null,
        SteamAppId: 529340,
        Support: GameSupport.LaunchOnly);

    /// <summary>可控完成时机的成就数据源；记录调用次数，证明刷新只发生一次。</summary>
    private sealed class ScriptedSource(Task<AchievementSnapshot> result) : IAchievementSource
    {
        public int Calls { get; private set; }

        public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken)
        {
            Calls++;
            return result;
        }
    }
}
