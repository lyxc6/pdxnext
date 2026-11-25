namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// V0 工坊 mod 缺注册 ID 的自动修复自检：诊断条件、指针内容、库回填与失败回滚。
/// </summary>
public static class ModRegistryRepairChecks
{
    private const string Ck3 = "ck3";
    private const string WorkshopId = "3840275677";

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckFindsMissingRegistry(fs, logger);
        CheckSkipsIneligibleMods(fs, logger);
        CheckApplyWritesPointerAndRegistry(fs, logger);
        CheckApplyRollsBackWhenRepositoryFails(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckFindsMissingRegistry(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistryRepairChecks] 诊断缺注册 ID");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistryrepair-find");
        try
        {
            var (game, repository, mod) = CreateFixture(fs, root, logger);
            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog([mod]), logger);
            var repairs = repairer.FindAsync(game, [mod], CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Equal("找到 1 条修复建议", 1, repairs.Count);
            if (repairs.Count == 1)
            {
                ChecksContext.Equal("修复类型是补注册 ID", ModPathRepairKind.FillRegistryId, repairs[0].Kind);
                ChecksContext.Equal("建议注册 ID", "mod/ugc_3840275677.mod", repairs[0].SuggestedRegistryId ?? "<null>");
                ChecksContext.Equal("原因键", ModLoadRules.WarnNoRegistryId, repairs[0].ReasonKey ?? "<null>");
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckSkipsIneligibleMods(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistryRepairChecks] 跳过不该修的行");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistryrepair-skip");
        try
        {
            var (game, repository, mod) = CreateFixture(fs, root, logger);
            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog([mod]), logger);
            var v1 = game with { Format = LoadFileFormat.ContentLoadV1 };
            var noSteam = mod with { SteamId = null };
            var disabled = mod with { Enabled = false };
            var alreadyRegistered = mod with { GameRegistryId = "mod/existing.mod" };
            var noDescriptor = mod with { DirPath = Path.Combine(game.GameDataDir, "no-descriptor") };

            var cases = new[]
            {
                ("非 V0 不修", v1, mod),
                ("没有工坊 id 不修", game, noSteam),
                ("停用行不修", game, disabled),
                ("已有注册 ID 不修", game, alreadyRegistered),
                ("描述符不存在不修", game, noDescriptor),
            };

            foreach (var (name, caseGame, caseMod) in cases)
            {
                var repairs = repairer.FindAsync(caseGame, [caseMod], CancellationToken.None).GetAwaiter().GetResult();
                ChecksContext.Equal(name, 0, repairs.Count);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckApplyWritesPointerAndRegistry(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistryRepairChecks] 应用修复写指针与库");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistryrepair-apply");
        try
        {
            var (game, repository, mod) = CreateFixture(fs, root, logger);
            var repairer = new ModPathRepairer(fs, repository, new FakeModCatalog([mod]), logger);
            var repairs = repairer.FindAsync(game, [mod], CancellationToken.None).GetAwaiter().GetResult();
            var applied = repairer.ApplyAsync(game, repairs, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Check("应用成功", applied.IsSuccess, applied.ErrorMessage);
            ChecksContext.Equal("修复 1 条", 1, applied.Value);

            var pointer = repairs[0].SuggestedPath!;
            ChecksContext.Check("指针文件已生成", fs.FileExists(pointer));
            var text = fs.ReadAllText(pointer);
            ChecksContext.Check("指针写 path", text.Contains("path=\"" + mod.DirPath.Replace('\\', '/') + "\"", StringComparison.Ordinal), text);
            ChecksContext.Check("指针写 remote_file_id", text.Contains("remote_file_id=\"3840275677\"", StringComparison.Ordinal), text);

            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            var repaired = library.Single(entry => entry.ModId == mod.ModId);
            ChecksContext.Equal("库里注册 ID 已回填", "mod/ugc_3840275677.mod", repaired.GameRegistryId);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static void CheckApplyRollsBackWhenRepositoryFails(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModRegistryRepairChecks] 库写入失败回滚指针");
        var root = PlaysetChecks.CreateTempRoot(fs, "modregistryrepair-rollback");
        try
        {
            var (game, repository, mod) = CreateFixture(fs, root, logger);
            var failing = new FailingRepository(repository);
            var repairer = new ModPathRepairer(fs, failing, new FakeModCatalog([mod]), logger);
            var repairs = repairer.FindAsync(game, [mod], CancellationToken.None).GetAwaiter().GetResult();
            var applied = repairer.ApplyAsync(game, repairs, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Check("库失败时整体失败", !applied.IsSuccess);
            var pointer = repairs[0].SuggestedPath!;
            ChecksContext.Check("新建指针已删除", !fs.FileExists(pointer));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    private static (GameInstallation Game, OwnDatabasePlaySetRepository Repository, PlaySetMod Mod) CreateFixture(
        IFileSystem fs,
        string root,
        NullLogger logger)
    {
        var databasePath = Path.Combine(root, "own.sqlite");
        var game = PlaysetChecks.CreateGame(
            Ck3,
            Path.Combine(root, "install", Ck3),
            LoadFileFormat.DlcLoadV0,
            Path.Combine(root, "documents", Ck3),
            Path.Combine(root, "install", Ck3, "game", "dlc"),
            Path.Combine(root, "documents", Ck3, "mod"),
            steamAppId: 1158310);
        fs.EnsureDirectory(game.ModDir);

        var modDir = Path.Combine(root, "library", "steamapps", "workshop", "content", "1158310", WorkshopId);
        fs.EnsureDirectory(modDir);
        fs.WriteAllTextAtomic(
            Path.Combine(modDir, "descriptor.mod"),
            "version=\"1.0\"\nname=\"Carnal Desires\"\nsupported_version=\"1.21.*\"\nremote_file_id=\"" + WorkshopId + "\"\n");

        var mod = new PlaySetMod(
            Guid.NewGuid(),
            "[1.21] Carnal Desires",
            modDir,
            ArchivePath: null,
            GameRegistryId: string.Empty,
            ThumbnailPath: null,
            RequiredVersion: "1.21.*",
            Status: "ready_to_play",
            Enabled: true,
            Position: 0,
            SteamId: WorkshopId,
            Source: ModOrigins.SteamSource);

        var repository = new OwnDatabasePlaySetRepository(
            new PdxDatabase(fs, logger, databasePath),
            logger,
            SystemClock.Instance);
        var playSetId = repository.CreatePlaySetAsync(game, "自检播放集", CancellationToken.None)
            .GetAwaiter().GetResult().Value;
        repository.SavePlaySetModsAsync(
            game,
            playSetId,
            [mod],
            CancellationToken.None).GetAwaiter().GetResult();

        return (game, repository, mod);
    }

    private sealed class FakeModCatalog(IReadOnlyList<PlaySetMod> mods) : IModCatalog
    {
        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult(mods);
    }

    private sealed class FailingRepository(IPlaySetRepository inner) : IPlaySetRepository
    {
        public Task<IReadOnlyList<PlaySet>> GetPlaySetsAsync(GameInstallation game, CancellationToken cancellationToken) =>
            inner.GetPlaySetsAsync(game, cancellationToken);

        public Task<PlaySet?> GetActivePlaySetAsync(GameInstallation game, CancellationToken cancellationToken) =>
            inner.GetActivePlaySetAsync(game, cancellationToken);

        public Task<IReadOnlyList<PlaySetMod>> GetPlaySetModsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            inner.GetPlaySetModsAsync(game, playSetId, cancellationToken);

        public Task<IReadOnlyList<ModLibraryEntry>> GetModLibraryAsync(GameInstallation game, CancellationToken cancellationToken) =>
            inner.GetModLibraryAsync(game, cancellationToken);

        public Task<AppResult<int>> RemoveWorkshopModAsync(GameInstallation game, string workshopId, CancellationToken cancellationToken) =>
            inner.RemoveWorkshopModAsync(game, workshopId, cancellationToken);

        public Task<IReadOnlyList<PlaySetDlc>> GetPlaySetDlcsAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            inner.GetPlaySetDlcsAsync(game, playSetId, cancellationToken);

        public Task<AppResult<bool>> UpdateModPathsAsync(GameInstallation game, Guid modId, string? dirPath, string? archivePath, CancellationToken cancellationToken) =>
            inner.UpdateModPathsAsync(game, modId, dirPath, archivePath, cancellationToken);

        public Task<AppResult<bool>> UpdateModRegistryIdAsync(GameInstallation game, Guid modId, string? gameRegistryId, CancellationToken cancellationToken) =>
            Task.FromResult(AppResult<bool>.Fail(ErrorKind.Io, "forced-registry-write-failure"));

        public Task<AppResult<bool>> AdoptLocalModAsync(GameInstallation game, Guid oldModId, string? oldDirPath, string? oldArchivePath, PlaySetMod localMod, CancellationToken cancellationToken) =>
            inner.AdoptLocalModAsync(game, oldModId, oldDirPath, oldArchivePath, localMod, cancellationToken);

        public Task<AppResult<bool>> RevertLocalModAdoptionAsync(GameInstallation game, Guid newModId, Guid oldModId, string? oldDirPath, string? oldArchivePath, CancellationToken cancellationToken, string? newDirPath = null, string? newArchivePath = null) =>
            inner.RevertLocalModAdoptionAsync(game, newModId, oldModId, oldDirPath, oldArchivePath, cancellationToken, newDirPath, newArchivePath);

        public Task<AppResult<bool>> SavePlaySetModsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetMod> mods, CancellationToken cancellationToken) =>
            inner.SavePlaySetModsAsync(game, playSetId, mods, cancellationToken);

        public Task<AppResult<bool>> SavePlaySetDlcsAsync(GameInstallation game, Guid playSetId, IReadOnlyList<PlaySetDlc> dlcs, CancellationToken cancellationToken) =>
            inner.SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken);

        public Task<AppResult<Guid>> CreatePlaySetAsync(GameInstallation game, string name, CancellationToken cancellationToken) =>
            inner.CreatePlaySetAsync(game, name, cancellationToken);

        public Task<AppResult<bool>> RenamePlaySetAsync(GameInstallation game, Guid playSetId, string name, CancellationToken cancellationToken) =>
            inner.RenamePlaySetAsync(game, playSetId, name, cancellationToken);

        public Task<AppResult<bool>> SetActivePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            inner.SetActivePlaySetAsync(game, playSetId, cancellationToken);

        public Task<AppResult<bool>> DeletePlaySetAsync(GameInstallation game, Guid playSetId, CancellationToken cancellationToken) =>
            inner.DeletePlaySetAsync(game, playSetId, cancellationToken);

    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message) { }

        public void Warn(string message) { }

        public void Error(string message) { }

        public void Error(string message, Exception? exception = null) { }
    }
}
