namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Storage;

/// <summary>变更计划确认后由 App 执行的安全边界自检。</summary>
public static class McpChangePlanExecutorChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var request = new McpChangePlanRequest(
            "settings",
            "set_setting",
            string.Empty,
            nameof(AppSettings.UiLanguage),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.UiLanguage),
                ["value"] = "en-US",
            },
            "zh-CN");
        var plan = new McpChangePlan(
            "plan-1",
            request.Domain,
            request.Operation,
            request.GameId,
            request.TargetId,
            request.Patch,
            request.ExpectedHash,
            "key-1",
            McpChangePlanStatus.Approved,
            DateTimeOffset.UnixEpoch,
            "app");

        ChecksContext.Check("审批人必须是 app", plan.ApprovedBy == "app");
        var validation = McpChangePlanPolicy.Validate(request);
        ChecksContext.Check("只允许安全设置字段", validation.IsValid, validation.ErrorCode ?? string.Empty);
        ChecksContext.Check("过期计划拒绝执行", McpChangePlanPolicy.IsExpired(plan, DateTimeOffset.UtcNow));

        var unsafeRequest = request with
        {
            TargetId = nameof(AppSettings.McpEnabled),
            Patch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.McpEnabled),
                ["value"] = "true",
            },
        };
        ChecksContext.Check("MCP 总开关禁止通过计划修改", !McpChangePlanPolicy.Validate(unsafeRequest).IsValid);

        var secretRequest = request with
        {
            TargetId = nameof(AppSettings.TranslateApiKey),
            Patch = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.TranslateApiKey),
                ["value"] = "attacker-key",
            },
        };
        ChecksContext.Check("翻译 API key 禁止通过计划修改", !McpChangePlanPolicy.Validate(secretRequest).IsValid);

        var rename = new McpChangePlanRequest(
            "playsets",
            "rename_play_set",
            "victoria3",
            Guid.NewGuid().ToString("D"),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "新名称" },
            "旧摘要");
        ChecksContext.Check("播放集重命名可进入确认", McpChangePlanPolicy.Validate(rename).IsValid);
        ChecksContext.Check("无游戏 id 的播放集计划被拒绝", !McpChangePlanPolicy.Validate(rename with { GameId = string.Empty }).IsValid);
        ChecksContext.Check(
            "设置域不允许塞游戏 id",
            !McpChangePlanPolicy.Validate(request with { GameId = "ck3" }).IsValid);

        var root = PlaysetChecks.CreateTempRoot(fs, "mcp-plan-executor");
        try
        {
            var database = new PdxDatabase(fs, new NullLogger(), Path.Combine(root, PdxDatabase.FileName));
            var documents = new SqliteJsonDocumentStore(database, new NullLogger(), new PdxClockStub());
            var settingsStore = new AppSettingsStore(documents, new NullLogger());
            var settings = new AppSettings { UiLanguage = "zh-CN" };
            settingsStore.Save(settings);
            var settingPlan = new McpChangePlan(
                "plan-2",
                "settings",
                "set_setting",
                string.Empty,
                nameof(AppSettings.UiLanguage),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["key"] = nameof(AppSettings.UiLanguage),
                    ["value"] = "en-US",
                },
                "zh-CN",
                "key-1",
                McpChangePlanStatus.Approved,
                DateTimeOffset.UtcNow,
                "app");
            var applied = McpChangePlanExecutor.ApplyAsync(
                settingPlan,
                new EmptyGameCatalog(),
                new EmptyPlaySetRepository(),
                settings,
                settingsStore).GetAwaiter().GetResult();
            ChecksContext.Check("已批准的安全设置计划可执行", applied);
            ChecksContext.Check("执行后设置落盘", string.Equals(settingsStore.Load().UiLanguage, "en-US", StringComparison.Ordinal));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private sealed class PdxClockStub : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
    }

    private sealed class EmptyGameCatalog : IGameCatalog
    {
        public Task<IReadOnlyList<GameInstallation>> DiscoverAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<GameInstallation>>([]);

        public Task<GameInstallation?> DiscoverOneAsync(string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<GameInstallation?>(null);
    }

    private sealed class EmptyPlaySetRepository : IPlaySetRepository
    {
        public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySet>>([]);
        public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<PlaySet?>(null);
        public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetMod>>([]);
        public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ModLibraryEntry>>([]);
        public Task<AppResult<int>> RemoveWorkshopModAsync(GameInstallation game, string workshopId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<int>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaySetDlc>>([]);
        public Task<AppResult<bool>> UpdateModPathsAsync(GameInstallation game, Guid modId, string? dirPath, string? archivePath, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> UpdateModRegistryIdAsync(GameInstallation game, Guid modId, string? gameRegistryId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> AdoptLocalModAsync(GameInstallation game, Guid oldModId, string? oldDirPath, string? oldArchivePath, PlaySetMod localMod, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> RevertLocalModAdoptionAsync(GameInstallation game, Guid newModId, Guid oldModId, string? oldDirPath, string? oldArchivePath, CancellationToken cancellationToken, string? newDirPath = null, string? newArchivePath = null) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> SavePlaySetModsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetMod> mods, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> SavePlaySetDlcsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetDlc> dlcs, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<Guid>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> RenamePlaySetAsync(GameInstallation game, Guid playSetId, string newName, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
        public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Unsupported, "stub"));
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }
        public void Warn(string message) { }
        public void Error(string message, Exception? exception = null) { }
    }
}
