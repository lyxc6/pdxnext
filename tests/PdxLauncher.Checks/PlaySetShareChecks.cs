namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Share;
using PdxLauncher.Core.Storage;

/// <summary>
/// 官方「分享 / 导入播放集」自检：
/// <list type="bullet">
/// <item>解析与序列化必须与官方 2026.13 的 <c>getPlaysetToExport</c> 逐字段一致（紧凑单行、字段顺序、null 省略）；</item>
/// <item>导出时本地 mod 被排除、PDX 锁定版本写 <c>version</c>、无平台 id 的条目单列；</item>
/// <item>导入按 steamId → pdxId → 显示名匹配，缺失条目单独列出，重复 id 直接拒绝；</item>
/// <item>同一份文件再次导入同名播放集时合并补齐，已有条目与顺序不被破坏。</item>
/// </list>
/// </summary>
public static class PlaySetShareChecks
{
    private const string Ck3 = "ck3";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckDocumentFormat(fs);
        CheckExportMapping();
        CheckImportMapping();
        CheckServiceRoundTrip(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 文档格式
    // ------------------------------------------------------------------

    private static void CheckDocumentFormat(IFileSystem fs)
    {
        Console.WriteLine("[PlaySetShareChecks] 官方分享 JSON 解析 / 序列化");

        var path = ChecksContext.FixturePath("playsets/ck3-share.json");
        var text = fs.ReadAllText(path).TrimEnd('\r', '\n');

        var document = PlaySetShareDocument.TryParse(text, out var error);
        ChecksContext.Check("真实形状的样本能解析", document is not null, error);
        if (document is null)
        {
            return;
        }

        ChecksContext.Equal("game 原样保留", "ck3", document.Game);
        ChecksContext.Equal("name 原样保留", "Share Sample", document.Name);
        ChecksContext.Equal("mod 条目数", 3, document.Mods.Count);
        ChecksContext.Equal("第 0 条的 steamId", "2220326926", document.Mods[0].SteamId ?? "<null>");
        ChecksContext.Equal("第 1 条的 position 保留跳号", 2, document.Mods[1].Position);
        ChecksContext.Check("第 1 条是禁用", !document.Mods[1].Enabled);
        ChecksContext.Equal("第 2 条的 pdxId", "74238", document.Mods[2].PdxId ?? "<null>");
        ChecksContext.Equal("第 2 条的锁定版本", "37", document.Mods[2].Version ?? "<null>");

        // 序列化必须与官方一致：紧凑单行、字段顺序、null 字段省略。
        ChecksContext.Equal("序列化与官方逐字符一致", text, document.ToJson());

        var invalid = PlaySetShareDocument.TryParse("{ not json", out var invalidError);
        ChecksContext.Check("坏 JSON 被拒绝", invalid is null);
        ChecksContext.Equal("坏 JSON 错误码", PlaySetShareDocument.ErrorInvalidJson, invalidError ?? "<null>");

        var missingGame = PlaySetShareDocument.TryParse("{\"name\":\"x\",\"mods\":[]}", out var missingGameError);
        ChecksContext.Check("缺 game 被拒绝", missingGame is null);
        ChecksContext.Equal("缺 game 错误码", "share-missing-field:game", missingGameError ?? "<null>");

        var badMod = PlaySetShareDocument.TryParse(
            "{\"game\":\"ck3\",\"name\":\"x\",\"mods\":[{\"displayName\":\"no id\",\"enabled\":true,\"position\":0}]}",
            out var badModError);
        ChecksContext.Check("缺平台 id 的条目被拒绝", badMod is null);
        ChecksContext.Equal("缺平台 id 错误码", "share-invalid-mod:0", badModError ?? "<null>");

        var tolerant = PlaySetShareDocument.TryParse(
            "{\"game\":\"ck3\",\"name\":\"x\",\"mods\":[{\"steamId\":111}]}",
            out var tolerantError);
        ChecksContext.Check("数字 id / 缺 displayName / 缺 enabled 都能容错", tolerant is not null, tolerantError);
        if (tolerant is not null)
        {
            ChecksContext.Equal("数字 id 转成文本", "111", tolerant.Mods[0].SteamId ?? "<null>");
            ChecksContext.Equal("displayName 退化为 id", "111", tolerant.Mods[0].DisplayName);
            ChecksContext.Check("enabled 缺省为 true", tolerant.Mods[0].Enabled);
        }
    }

    // ------------------------------------------------------------------
    // 导出映射
    // ------------------------------------------------------------------

    private static void CheckExportMapping()
    {
        Console.WriteLine("[PlaySetShareChecks] 导出映射");

        var game = PlaysetChecks.CreateGame(
            Ck3,
            "install",
            LoadFileFormat.ContentLoadV1_1,
            "documents",
            "dlc",
            "mod",
            steamAppId: null);
        var playSet = new PlaySet(Guid.NewGuid(), "Pack", IsActive: true);

        var mods = new[]
        {
            MakeMod(Guid.NewGuid(), "Steam Mod", @"D:\steam\111", true, 0, ModOrigins.SteamSource, steamId: "111"),
            // 真机存在「source=local 但带 steamId」的本地 mod：官方仍然按本地排除。
            MakeMod(Guid.NewGuid(), "Local Mod", @"D:\local", true, 1, ModOrigins.LocalSource, steamId: "555"),
            MakeMod(
                Guid.NewGuid(),
                "PDX Mod",
                @"D:\pdx\222",
                false,
                2,
                ModOrigins.PdxModsSource,
                steamId: null,
                registryId: "pdx:222",
                pdxVersion: "37"),
            MakeMod(Guid.NewGuid(), "No Platform Id", @"D:\steam\333", true, 3, ModOrigins.ParadoxSource),
        };

        var plan = PlaySetShareMapper.BuildExport(game, playSet, mods);
        ChecksContext.Equal("导出 2 条可表达的 mod", 2, plan.Document.Mods.Count);
        ChecksContext.Equal("排除 1 个本地 mod", 1, plan.LocalExcluded);
        ChecksContext.Equal("1 个条目没有平台 id", 1, plan.UnsupportedMods.Count);
        ChecksContext.Equal("无法表达的条目名", "No Platform Id", plan.UnsupportedMods[0].Name);

        ChecksContext.Equal("game 用当前游戏 id", Ck3, plan.Document.Game);
        ChecksContext.Equal("name 用播放集名", "Pack", plan.Document.Name);
        ChecksContext.Equal("Steam 条写 steamId", "111", plan.Document.Mods[0].SteamId ?? "<null>");
        ChecksContext.Equal("Steam 条不写 pdxId", null, plan.Document.Mods[0].PdxId);
        ChecksContext.Equal("PDX 条写 pdxId", "222", plan.Document.Mods[1].PdxId ?? "<null>");
        ChecksContext.Equal("PDX 锁定版本写 version", "37", plan.Document.Mods[1].Version ?? "<null>");
        ChecksContext.Equal("position 原样保留（不因过滤重排）", 2, plan.Document.Mods[1].Position);
    }

    // ------------------------------------------------------------------
    // 导入映射
    // ------------------------------------------------------------------

    private static void CheckImportMapping()
    {
        Console.WriteLine("[PlaySetShareChecks] 导入映射");

        var game = PlaysetChecks.CreateGame(
            Ck3,
            "install",
            LoadFileFormat.ContentLoadV1_1,
            "documents",
            "dlc",
            "mod",
            steamAppId: null);

        var steamId = Guid.NewGuid();
        var pdxId = Guid.NewGuid();
        var nameMatchId = Guid.NewGuid();
        var library = new[]
        {
            Entry(steamId, "Steam Mod A", @"D:\steam\111", steamId: "111", source: ModOrigins.SteamSource),
            Entry(pdxId, "PDX Mod", @"D:\pdx\222", steamId: null, source: ModOrigins.PdxModsSource, registryId: "pdx:222"),
            Entry(nameMatchId, "Name Match", @"D:\local\name", steamId: null, source: ModOrigins.LocalSource),
            // 订阅了但没下载完：没有内容，不能算可导入。
            Entry(Guid.NewGuid(), "Not Downloaded", null, steamId: "333", source: ModOrigins.SteamSource),
        };

        var document = new PlaySetShareDocument(
            Ck3,
            "Pack",
            new[]
            {
                new PlaySetShareMod("PDX Mod", true, 2, PdxId: "222"),
                new PlaySetShareMod("Steam Mod A", false, 0, SteamId: "111"),
                new PlaySetShareMod("Not Downloaded", true, 1, SteamId: "333"),
                new PlaySetShareMod("Name Match", true, 3, SteamId: "999"),
            });

        var resolution = PlaySetShareMapper.Resolve(game, document, library);
        ChecksContext.Equal("sid/pdx/名称都能匹配，剩 1 条缺失", 3, resolution.Resolved.Count);
        ChecksContext.Equal("缺失 1 条", 1, resolution.Missing.Count);
        ChecksContext.Equal("缺失的是没下载完的那条", "Not Downloaded", resolution.Missing[0].DisplayName);
        ChecksContext.Equal("缺失条目保留 steamId", "333", resolution.Missing[0].SteamId ?? "<null>");
        ChecksContext.Check("没有重复 id", resolution.DuplicateIds.Count == 0);

        ChecksContext.Equal("按 position 排序后第 0 条是 Steam", steamId, resolution.Resolved[0].ModId);
        ChecksContext.Equal("重排后 position 连续（0）", 0, resolution.Resolved[0].Position);
        ChecksContext.Check("文件里的 enabled 生效", !resolution.Resolved[0].Enabled);
        ChecksContext.Equal("第 1 条是 PDX", pdxId, resolution.Resolved[1].ModId);
        ChecksContext.Equal("第 2 条按名称兜底对上", nameMatchId, resolution.Resolved[2].ModId);

        var duplicated = new PlaySetShareDocument(
            Ck3,
            "Pack",
            new[]
            {
                new PlaySetShareMod("A", true, 0, SteamId: "111"),
                new PlaySetShareMod("A again", true, 1, SteamId: "111"),
            });
        var duplicateResolution = PlaySetShareMapper.Resolve(game, duplicated, library);
        ChecksContext.Equal("重复 id 被识别", 1, duplicateResolution.DuplicateIds.Count);
        ChecksContext.Equal("重复 id 原文", "111", duplicateResolution.DuplicateIds[0]);

        ChecksContext.Check("游戏 id 一致", PlaySetShareMapper.MatchesGame(game, document));
        ChecksContext.Check(
            "游戏 id 不一致",
            !PlaySetShareMapper.MatchesGame(
                game,
                document with { Game = "victoria3" }));

        ChecksContext.Equal("重名自动加序号", "Pack (2)", PlaySetShareMapper.EnsureUniqueName("Pack", new[] { "Pack" }));
        ChecksContext.Equal("大小写不敏感判重", "pack (2)", PlaySetShareMapper.EnsureUniqueName("pack", new[] { "Pack" }));
        ChecksContext.Equal("不重名原样返回", "Fresh", PlaySetShareMapper.EnsureUniqueName("Fresh", new[] { "Pack" }));
    }

    // ------------------------------------------------------------------
    // 服务端到端（自有库）
    // ------------------------------------------------------------------

    private static void CheckServiceRoundTrip(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[PlaySetShareChecks] 导出 / 导入 / 同名合并（自有库）");

        var root = PlaysetChecks.CreateTempRoot(fs, "playset-share");
        try
        {
            var gameDataDir = Path.Combine(root, "documents", Ck3);
            fs.EnsureDirectory(gameDataDir);
            var game = PlaysetChecks.CreateGame(
                Ck3,
                Path.Combine(root, "install", Ck3),
                LoadFileFormat.ContentLoadV1_1,
                gameDataDir,
                Path.Combine(root, "install", Ck3, "game", "dlc"),
                Path.Combine(gameDataDir, "mod"),
                steamAppId: null);

            var database = new PdxDatabase(fs, logger, Path.Combine(root, "own.sqlite"));
            var repository = new OwnDatabasePlaySetRepository(database, logger, SystemClock.Instance);
            var service = new PlaySetShareService(repository, fs, logger);

            var created = repository.CreatePlaySetAsync(game, "Pack", CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("建播放集", created.IsSuccess, created.ErrorMessage);
            var originalId = created.Value;

            var steamModId = Guid.NewGuid();
            var localModId = Guid.NewGuid();
            var pdxModId = Guid.NewGuid();
            var mods = new[]
            {
                MakeMod(steamModId, "Steam Mod A", @"D:\steam\111", true, 0, ModOrigins.SteamSource, steamId: "111"),
                MakeMod(localModId, "Local Mod", @"D:\local", true, 1, ModOrigins.LocalSource),
                MakeMod(pdxModId, "PDX Mod", @"D:\pdx\222", false, 2, ModOrigins.PdxModsSource, registryId: "pdx:222"),
            };
            ChecksContext.Check(
                "写播放集 mod 行",
                repository.SavePlaySetModsAsync(game, originalId, mods, CancellationToken.None)
                    .GetAwaiter().GetResult().IsSuccess);

            var exportPath = Path.Combine(root, "Pack.json");
            var exported = service.ExportAsync(game, originalId, exportPath, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("导出成功", exported.IsSuccess, exported.ErrorMessage);
            if (!exported.IsSuccess)
            {
                return;
            }

            ChecksContext.Equal("导出 2 条", 2, exported.Value!.ExportedMods);
            ChecksContext.Equal("排除 1 条本地", 1, exported.Value.LocalExcluded);
            ChecksContext.Check("文件已落盘", fs.FileExists(exportPath));

            var expectedJson =
                "{\"game\":\"ck3\",\"name\":\"Pack\",\"mods\":["
                + "{\"displayName\":\"Steam Mod A\",\"enabled\":true,\"position\":0,\"steamId\":\"111\"},"
                + "{\"displayName\":\"PDX Mod\",\"enabled\":false,\"position\":2,\"pdxId\":\"222\"}]}";
            ChecksContext.Equal("导出文件与官方格式逐字符一致", expectedJson, fs.ReadAllText(exportPath));

            var peek = service.PeekAsync(game, exportPath, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("预览成功", peek.IsSuccess, peek.ErrorMessage);
            ChecksContext.Equal("预览识别同名播放集", originalId, peek.Value!.ExistingPlaySetId ?? Guid.Empty);
            ChecksContext.Equal("预览条目数", 2, peek.Value!.Mods);

            // 首次导入：同名 → 新建副本。
            var imported = service.ImportAsync(game, exportPath, mergeIntoPlaySetId: null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("导入成功", imported.IsSuccess, imported.ErrorMessage);
            if (!imported.IsSuccess)
            {
                return;
            }

            ChecksContext.Equal("新建了副本", "Pack (2)", imported.Value!.PlaySetName);
            ChecksContext.Check("不是合并", !imported.Value!.Merged);
            ChecksContext.Equal("导入 2 条", 2, imported.Value!.ImportedMods);
            ChecksContext.Check("没有缺失", imported.Value!.Missing.Count == 0);

            var copyRows = repository.GetPlaySetModsAsync(game, imported.Value!.PlaySetId, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Equal("副本里有 2 条", 2, copyRows.Count);
            ChecksContext.Equal("副本第 0 条是 Steam", steamModId, copyRows[0].ModId);
            ChecksContext.Equal("副本重排后 position 连续", 1, copyRows[1].Position);

            // 再次导入：合并进原播放集，补齐/更新启用态，且保留集内本地 mod。
            var flipped = new[]
            {
                MakeMod(steamModId, "Steam Mod A", @"D:\steam\111", false, 0, ModOrigins.SteamSource, steamId: "111"),
                MakeMod(localModId, "Local Mod", @"D:\local", true, 1, ModOrigins.LocalSource),
                MakeMod(pdxModId, "PDX Mod", @"D:\pdx\222", false, 2, ModOrigins.PdxModsSource, registryId: "pdx:222"),
            };
            repository.SavePlaySetModsAsync(game, originalId, flipped, CancellationToken.None)
                .GetAwaiter().GetResult();

            var merged = service.ImportAsync(game, exportPath, originalId, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("合并成功", merged.IsSuccess, merged.ErrorMessage);
            if (!merged.IsSuccess)
            {
                return;
            }

            ChecksContext.Check("标记为合并", merged.Value!.Merged);
            ChecksContext.Equal("没有新增条目", 0, merged.Value!.AddedMods);

            var mergedRows = repository.GetPlaySetModsAsync(game, originalId, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Equal("合并后仍是 3 条（没丢本地 mod）", 3, mergedRows.Count);
            ChecksContext.Equal("文件顺序优先：第 0 条 Steam", steamModId, mergedRows[0].ModId);
            ChecksContext.Equal("文件顺序优先：第 1 条 PDX", pdxModId, mergedRows[1].ModId);
            ChecksContext.Equal("集内原有本地 mod 排在末尾", localModId, mergedRows[2].ModId);
            ChecksContext.Check("同 id 条目按文件的启用态更新（false→true）", mergedRows[0].Enabled);

            // 错误路径。
            var wrongGamePath = Path.Combine(root, "wrong-game.json");
            fs.WriteAllTextAtomic(wrongGamePath, "{\"game\":\"victoria3\",\"name\":\"X\",\"mods\":[]}");
            var wrongGame = service.ImportAsync(game, wrongGamePath, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("游戏不一致被拒绝", !wrongGame.IsSuccess);
            ChecksContext.Equal("游戏不一致错误码", "share-wrong-game:victoria3", wrongGame.ErrorMessage ?? "<null>");

            var duplicatePath = Path.Combine(root, "duplicate.json");
            fs.WriteAllTextAtomic(
                duplicatePath,
                "{\"game\":\"ck3\",\"name\":\"X\",\"mods\":["
                + "{\"displayName\":\"A\",\"enabled\":true,\"position\":0,\"steamId\":\"111\"},"
                + "{\"displayName\":\"B\",\"enabled\":true,\"position\":1,\"steamId\":\"111\"}]}");
            var duplicate = service.ImportAsync(game, duplicatePath, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("重复 mod 被拒绝", !duplicate.IsSuccess);
            ChecksContext.Check(
                "重复 mod 错误码带 id",
                (duplicate.ErrorMessage ?? string.Empty).StartsWith("share-duplicate-mods:", StringComparison.Ordinal),
                duplicate.ErrorMessage);

            var invalidPath = Path.Combine(root, "invalid.json");
            fs.WriteAllTextAtomic(invalidPath, "{ not json");
            var invalid = service.ImportAsync(game, invalidPath, null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Equal(
                "坏 JSON 错误码",
                PlaySetShareDocument.ErrorInvalidJson,
                invalid.ErrorMessage ?? "<null>");

            var missingFile = service.ImportAsync(game, Path.Combine(root, "nope.json"), null, CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check(
                "文件不存在错误码",
                (missingFile.ErrorMessage ?? string.Empty).StartsWith("share-file-not-found:", StringComparison.Ordinal),
                missingFile.ErrorMessage);

            var missingPlaySet = service.ExportAsync(game, Guid.NewGuid(), Path.Combine(root, "x.json"), CancellationToken.None)
                .GetAwaiter().GetResult();
            ChecksContext.Check("导出不存在的播放集被拒绝", !missingPlaySet.IsSuccess);
            ChecksContext.Check(
                "导出不存在错误码",
                (missingPlaySet.ErrorMessage ?? string.Empty).StartsWith("playSet-not-found:", StringComparison.Ordinal),
                missingPlaySet.ErrorMessage);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 构造工具
    // ------------------------------------------------------------------

    private static PlaySetMod MakeMod(
        Guid modId,
        string name,
        string dirPath,
        bool enabled,
        int position,
        string source,
        string? steamId = null,
        string? registryId = null,
        string? pdxVersion = null)
    {
        return new PlaySetMod(
            modId,
            name,
            dirPath,
            ArchivePath: null,
            GameRegistryId: registryId ?? string.Empty,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Enabled: enabled,
            Position: position,
            SteamId: steamId,
            Source: source,
            PdxVersion: pdxVersion);
    }

    private static ModLibraryEntry Entry(
        Guid modId,
        string name,
        string? dirPath,
        string? steamId,
        string source,
        string? registryId = null)
    {
        return new ModLibraryEntry(
            modId,
            name,
            registryId ?? string.Empty,
            dirPath,
            ArchivePath: null,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Source: source,
            SizeBytes: null,
            SubscribedOn: null,
            PlaySetIds: Array.Empty<Guid>(),
            SteamId: steamId);
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
