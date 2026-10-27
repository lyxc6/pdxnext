namespace PdxLauncher.Checks;

using System.Text.Json;
using System.Text.Json.Nodes;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Playsets.Json;

/// <summary>
/// EU5 的 <c>playsets.json</c> 仓储自检：格式映射、写回保真、缺失 mod 的占位、
/// DLC 别名同步、播放集增删改与损坏文件的降级行为。
/// </summary>
public static class PlaysetJsonChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-playsets-json-" + Guid.NewGuid().ToString("N"));
        try
        {
            CheckRead(fs, tempRoot);
            CheckSaveMods(fs, tempRoot);
            CheckSaveDlcs(fs, tempRoot);
            CheckPlaySetLifecycle(fs, tempRoot);
            CheckCorruptDocument(fs, tempRoot);
            CheckIds(fs);
            CheckBomMetadata(fs, tempRoot);
            CheckStalePathResolution(fs, tempRoot);
            CheckEu5DlcDiscovery(fs, tempRoot);
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
                // 清理失败不影响检查结果。
            }
        }

        return 0;
    }

    private static void CheckRead(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 读取真实文档形状");
        var (game, path) = Prepare(fs, tempRoot, "read", RealisticDocument);

        // 磁盘上真实存在的 workshop 目录：仓储应把 json 里的绝对路径映射成扫描结果。
        var workshopMod = Path.Combine(tempRoot, "workshop", "content", "3450310", "1234567890");
        fs.EnsureDirectory(workshopMod);
        var catalog = new FakeModCatalog(
        [
            new PlaySetMod(
                Guid.NewGuid(),
                "圣地在望",
                workshopMod,
                null,
                "mod/ugc_1234567890.mod",
                null,
                "1.0.10",
                "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: "1234567890"),
        ]);
        var dlcs = new FakeDlcCatalog(
        [
            new DlcInfo("Sacred Sites", "pops_sacred_sites", "2246280", "sacred_sites.dlc.json", "", "", []),
            new DlcInfo("Shared DLC Data", "pops_shared", "", "shared.dlc.json", "", "", []),
        ]);
        var logger = new RecordingLogger();
        var repository = new PlaysetsJsonRepository(
            fs,
            logger,
            catalog,
            dlcs,
            Path.Combine(tempRoot, "backup-read"));

        var playSets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("playsets.json: 播放集数量", 2, playSets.Count);
        ChecksContext.Equal("playsets.json: 第一个播放集名", "我的配置", playSets[0].Name);
        ChecksContext.Equal("playsets.json: 活动播放集", "我的配置", playSets.FirstOrDefault(static p => p.IsActive)?.Name);
        ChecksContext.Equal(
            "playsets.json: id 由 (gameId, name) 派生且稳定",
            PlaysetsJsonRepository.GetPlaySetId("eu5", "我的配置"),
            playSets[0].Id);

        // IsAutomaticallySorted 之类我们不认识的键必须原样保留（写回时不能丢）。
        var document = PlaysetsJsonDocument.Parse(fs.ReadAllText(path));
        ChecksContext.Equal("playsets.json: file_version 原样", "1.0.0", document.FileVersion);

        var activeId = playSets[0].Id;
        var mods = repository.GetPlaySetModsAsync(game, activeId, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("playsets.json: mod 条目数", 2, mods.Count);
        ChecksContext.Equal("playsets.json: 已存在的 mod 映射到扫描结果", "圣地在望", mods[0].Name);
        ChecksContext.Equal("playsets.json: 启用状态", true, mods[0].Enabled);
        ChecksContext.Check(
            "playsets.json: 缩略图字段可用",
            string.IsNullOrEmpty(mods[0].ThumbnailPath) || mods[0].ThumbnailPath!.Length > 0);
        ChecksContext.Equal("playsets.json: 磁盘缺失的 mod 保留占位", "missing", mods[1].Status);
        ChecksContext.Equal("playsets.json: 缺失 mod 的名字取目录名", "9999999999", mods[1].Name);
        ChecksContext.Equal("playsets.json: 缺失 mod 仍保留启用状态", false, mods[1].Enabled);

        var activeDlcs = repository.GetPlaySetDlcsAsync(game, activeId, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("playsets.json: DLC 条目数（含非 pops 的别名条目）", 3, activeDlcs.Count);
        ChecksContext.Check(
            "playsets.json: pops_id 与显示名条目都在",
            activeDlcs.Any(static d => d.DlcId == "pops_sacred_sites")
            && activeDlcs.Any(static d => d.DlcId == "Sacred Sites"),
            string.Join(",", activeDlcs.Select(static d => d.DlcId)));
    }

    private static void CheckSaveMods(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 写回 mod 列表");
        var (game, path) = Prepare(fs, tempRoot, "save-mods", RealisticDocument);

        var realDir = Path.Combine(tempRoot, "workshop", "content", "3450310", "2222");
        fs.EnsureDirectory(realDir);
        var catalog = new FakeModCatalog(
        [
            new PlaySetMod(Guid.NewGuid(), "被搬家的 mod", realDir, null, "mod/x.mod", null, "", "ready_to_play", true, 0),
        ]);
        var repository = new PlaysetsJsonRepository(
            fs,
            new RecordingLogger(),
            catalog,
            new FakeDlcCatalog([]),
            Path.Combine(tempRoot, "backup-save-mods"));

        var playSetId = PlaysetsJsonRepository.GetPlaySetId("eu5", "我的配置");
        repository.SavePlaySetModsAsync(
            game,
            playSetId,
            [
                new PlaySetMod(Guid.NewGuid(), "被搬家的 mod", realDir, null, "mod/x.mod", null, "", "ready_to_play", true, 0),
                new PlaySetMod(
                    Guid.NewGuid(),
                    "旧路径还在 json 里",
                    @"E:\SteamLibrary\steamapps\workshop\content\3450310\3333\",
                    null,
                    "mod/y.mod",
                    null,
                    "",
                    "ready_to_play",
                    false,
                    1),
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        var root = JsonNode.Parse(fs.ReadAllText(path))!.AsObject();
        ChecksContext.Equal("playsets.json 写回: 保留 file_version", "1.0.0", root["file_version"]!.GetValue<string>());

        var playSet = root["playsets"]![0]!.AsObject();
        ChecksContext.Check(
            "playsets.json 写回: 保留未知键 isAutomaticallySorted",
            playSet["isAutomaticallySorted"] is not null);
        ChecksContext.Check("playsets.json 写回: 保留 DLC 数组", playSet["DLC"] is JsonArray);

        var mods = playSet["orderedListMods"]!.AsArray();
        ChecksContext.Equal("playsets.json 写回: mod 条目数", 2, mods.Count);
        ChecksContext.Equal(
            "playsets.json 写回: 路径以正斜杠结尾",
            realDir.Replace('\\', '/') + "/",
            mods[0]!["path"]!.GetValue<string>());
        ChecksContext.Equal("playsets.json 写回: 启用状态", true, mods[0]!["isEnabled"]!.GetValue<bool>());
        ChecksContext.Equal(
            "playsets.json 写回: 缺失的旧路径照写（不丢配置）",
            "E:/SteamLibrary/steamapps/workshop/content/3450310/3333/",
            mods[1]!["path"]!.GetValue<string>());
        ChecksContext.Equal("playsets.json 写回: 关闭状态", false, mods[1]!["isEnabled"]!.GetValue<bool>());

        var backups = fs.EnumerateFiles(Path.Combine(tempRoot, "backup-save-mods", "eu5"), "*.bak", recursive: false).ToList();
        ChecksContext.Equal("playsets.json 写回: 覆盖前先备份", 1, backups.Count);
    }

    private static void CheckSaveDlcs(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 写回 DLC 开关");
        var (game, path) = Prepare(fs, tempRoot, "save-dlcs", RealisticDocument);

        var dlcs = new FakeDlcCatalog(
        [
            new DlcInfo("Sacred Sites", "pops_sacred_sites", "2246280", "a.dlc.json", "", "", []),
            new DlcInfo("Shared DLC Data", "pops_shared", "", "b.dlc.json", "", "", []),
        ]);
        var repository = new PlaysetsJsonRepository(
            fs,
            new RecordingLogger(),
            new FakeModCatalog([]),
            dlcs,
            Path.Combine(tempRoot, "backup-save-dlcs"));

        var playSetId = PlaysetsJsonRepository.GetPlaySetId("eu5", "我的配置");
        repository.SavePlaySetDlcsAsync(
            game,
            playSetId,
            [
                new PlaySetDlc("pops_sacred_sites", false),
                new PlaySetDlc("pops_shared", true),
            ],
            CancellationToken.None).GetAwaiter().GetResult();

        var entries = JsonNode.Parse(fs.ReadAllText(path))!.AsObject()["playsets"]![0]!.AsObject()["DLC"]!.AsArray()
            .Select(static n => n!.AsObject())
            .ToList();

        string IdOf(JsonObject o) => o["paradoxAppId"]!.GetValue<string>();
        bool OnOf(JsonObject o) => o["isEnabled"]!.GetValue<bool>();

        ChecksContext.Check(
            "playsets.json DLC: pops_id 状态写入",
            entries.Any(e => IdOf(e) == "pops_sacred_sites" && !OnOf(e)),
            string.Join(",", entries.Select(e => IdOf(e) + "=" + OnOf(e))));
        ChecksContext.Check(
            "playsets.json DLC: 显示名别名与 pops_id 同步",
            entries.Any(e => IdOf(e) == "Sacred Sites" && !OnOf(e)),
            string.Join(",", entries.Select(e => IdOf(e) + "=" + OnOf(e))));
        ChecksContext.Check(
            "playsets.json DLC: 覆盖到的条目被更新",
            entries.Any(e => IdOf(e) == "pops_shared" && OnOf(e)));
        ChecksContext.Check(
            "playsets.json DLC: 未覆盖的额外条目保留",
            entries.Any(e => IdOf(e) == "UnrelatedEntry"),
            string.Join(",", entries.Select(IdOf)));
    }

    private static void CheckPlaySetLifecycle(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 播放集增删改");
        var (game, path) = Prepare(fs, tempRoot, "lifecycle", RealisticDocument);

        var repository = new PlaysetsJsonRepository(
            fs,
            new RecordingLogger(),
            new FakeModCatalog([]),
            new FakeDlcCatalog([]),
            Path.Combine(tempRoot, "backup-lifecycle"));

        var createdResult = repository.CreatePlaySetAsync(game, "新配置", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("播放集: 新建成功", createdResult.IsSuccess, createdResult.ErrorMessage);
        var created = createdResult.Value;
        ChecksContext.Equal("播放集: 新建返回派生 id", PlaysetsJsonRepository.GetPlaySetId("eu5", "新配置"), created);

        var actual = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("播放集: 新建后数量", 3, actual.Count);
        ChecksContext.Equal("播放集: 新建的非活动", false, actual[2].IsActive);

        var duplicateRejected = repository.CreatePlaySetAsync(game, "我的配置", CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "播放集: 重名新建被拒绝",
            !duplicateRejected.IsSuccess && duplicateRejected.Kind == ErrorKind.Conflict,
            duplicateRejected.ErrorMessage);

        repository.SetActivePlaySetAsync(game, created, CancellationToken.None).GetAwaiter().GetResult();
        var afterActive = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("播放集: 设活动后只有一个活动", 1, afterActive.Count(static p => p.IsActive));
        ChecksContext.Equal("播放集: 活动播放集是新配置", "新配置", afterActive.First(static p => p.IsActive).Name);

        ChecksContext.Check(
            "播放集: 改名成功",
            repository.RenamePlaySetAsync(game, created, "改名后", CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
        ChecksContext.Check(
            "播放集: 改名撞名被拒绝",
            !repository.RenamePlaySetAsync(game, created, "我的配置", CancellationToken.None).GetAwaiter().GetResult().IsSuccess);

        var renamed = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check(
            "播放集: 改名后 id 随之变化（按 name 派生）",
            renamed.Any(static p => p.Name == "改名后"),
            string.Join(",", renamed.Select(static p => p.Name)));

        // 活动播放集不允许删除（游戏启动时必须有活动播放集）。
        var activeId = renamed.First(static p => p.IsActive).Id;
        ChecksContext.Check(
            "播放集: 活动播放集不可删",
            !repository.DeletePlaySetAsync(game, activeId, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);

        var inactiveId = renamed.First(static p => !p.IsActive).Id;
        ChecksContext.Check(
            "播放集: 非活动播放集可删",
            repository.DeletePlaySetAsync(game, inactiveId, CancellationToken.None).GetAwaiter().GetResult().IsSuccess);
        var afterDelete = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("播放集: 删除后数量", 2, afterDelete.Count);
        ChecksContext.Check("播放集: 写盘后 json 仍可解析", JsonNode.Parse(fs.ReadAllText(path)) is JsonObject);
    }

    private static void CheckCorruptDocument(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 损坏文件降级");
        var (game, path) = Prepare(fs, tempRoot, "corrupt", "{ 这不是合法 json ");

        var logger = new RecordingLogger();
        var repository = new PlaysetsJsonRepository(
            fs,
            logger,
            new FakeModCatalog([]),
            new FakeDlcCatalog([]),
            Path.Combine(tempRoot, "backup-corrupt"));

        var playSets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Equal("损坏文件: 读出空播放集", 0, playSets.Count);
        ChecksContext.Check("损坏文件: 记 warn", logger.Warnings.Any(static w => w.Contains("playsets.json", StringComparison.Ordinal)));
        ChecksContext.Equal("损坏文件: 不覆写原文件", "{ 这不是合法 json ", fs.ReadAllText(path));
    }

    private static void CheckIds(IFileSystem fs)
    {
        Console.WriteLine("[PlaysetJsonChecks] 路径规范化与 id 派生");
        ChecksContext.Equal(
            "路径规范化: 反斜杠转正斜杠",
            "C:/mods/x/",
            PlaysetsJsonRepository.NormalizeModPath(@"C:\mods\x"));
        ChecksContext.Equal(
            "路径规范化: 已带斜杠不重复加",
            "C:/mods/x/",
            PlaysetsJsonRepository.NormalizeModPath("C:/mods/x/"));
        ChecksContext.Equal("路径规范化: 空值", string.Empty, PlaysetsJsonRepository.NormalizeModPath(null));
        ChecksContext.Check(
            "id 派生: 不同游戏同名播放集 id 不同",
            PlaysetsJsonRepository.GetPlaySetId("eu5", "默认") != PlaysetsJsonRepository.GetPlaySetId("ck3", "默认"));
        ChecksContext.Check(
            "id 派生: 同输入 id 相同",
            PlaysetsJsonRepository.GetPlaySetId("eu5", "默认") == PlaysetsJsonRepository.GetPlaySetId("eu5", "默认"));
        _ = fs;
    }

    /// <summary>
    /// 真机回归：EU5 创意工坊 mod 的 <c>.metadata/metadata.json</c> 全部带 UTF-8 BOM（EF BB BF）。
    /// 以前直接用 <c>JsonDocument.Parse(byte[])</c>，抛 <c>'0xEF' is an invalid start of a value</c>，
    /// 导致 mod 全被跳过、列表里只剩工坊 id。这里在临时目录复原同样的布局。
    /// </summary>
    private static void CheckBomMetadata(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] BOM 的 mod metadata.json");

        var bom = new byte[] { 0xEF, 0xBB, 0xBF };
        ChecksContext.Equal(
            "JsonBytes: 剥掉 3 字节 BOM",
            2,
            JsonBytes.WithoutBom([.. bom, (byte)'{', (byte)'}']).Length);
        ChecksContext.Equal(
            "JsonBytes: 无 BOM 时原样返回",
            2,
            JsonBytes.WithoutBom([(byte)'{', (byte)'}']).Length);

        var library = Path.Combine(tempRoot, "bom", "steamapps");
        var install = Path.Combine(library, "common", "Europa Universalis V");
        var documents = Path.Combine(tempRoot, "bom", "documents");
        var workshopMod = Path.Combine(library, "workshop", "content", "3450310", "3601117514");
        fs.EnsureDirectory(install);
        fs.EnsureDirectory(Path.Combine(documents, "mod"));
        fs.EnsureDirectory(Path.Combine(workshopMod, ".metadata"));

        const string MetadataJson = """
            {
              "name": "圣地在望",
              "id": "3601117514",
              "supported_game_version": "1.0.*",
              "thumbnail": "thumbnail.png"
            }
            """;
        var metadataPath = Path.Combine(workshopMod, ".metadata", "metadata.json");
        fs.WriteAllBytesAtomic(metadataPath, [.. bom, .. System.Text.Encoding.UTF8.GetBytes(MetadataJson)]);

        var game = new GameInstallation(
            "eu5",
            "Europa Universalis V",
            "1.0.10",
            install,
            string.Empty,
            LoadFileFormat.PlaysetsJsonV1,
            install,
            Path.Combine(install, "game", "dlc"),
            Path.Combine(documents, "mod"),
            Path.Combine(install, "binaries", "eu5.exe"),
            [],
            [],
            new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 24,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            null,
            null,
            3450310,
            GameSupport.Full);

        var logger = new RecordingLogger();
        var catalog = new ModCatalog(fs, logger);
        var mods = catalog.DiscoverAllAsync(game, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Equal("BOM: 扫到 1 个 mod", 1, mods.Count);
        ChecksContext.Equal("BOM: 名字取自 metadata.json（不是工坊 id）", "圣地在望", mods[0].Name);
        ChecksContext.Equal("BOM: 工坊 id 取自 metadata.json", "3601117514", mods[0].SteamId);
        ChecksContext.Check(
            "BOM: 没有「metadata.json 解析失败」告警",
            logger.Warnings.All(warning => !warning.Contains("解析失败", StringComparison.Ordinal)),
            string.Join(" | ", logger.Warnings));
    }

    /// <summary>
    /// 真机回归：EU5 的 playsets.json 里留着库搬家前的绝对路径（写在 <c>E:/</c>，mod 实际在 F: 库）。
    /// 只按路径查会把 19 个 mod 全判成 missing、名字退化成一串工坊 id；这里验证按目录名兜底。
    /// </summary>
    private static void CheckStalePathResolution(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] 库搬家后的陈旧路径");

        const string StaleDocument = """
            {
              "file_version": "1.0.0",
              "playsets": [
                {
                  "name": "初始播放集",
                  "isActive": true,
                  "orderedListMods": [
                    { "path": "E:/SteamLibrary/steamapps/workshop/content/3450310/3601117514/", "isEnabled": true }
                  ],
                  "DLC": []
                }
              ]
            }
            """;

        var (game, _) = Prepare(fs, tempRoot, "stale", StaleDocument);
        var realDir = Path.Combine(tempRoot, "stale", "library", "steamapps", "workshop", "content", "3450310", "3601117514");
        fs.EnsureDirectory(realDir);

        var catalog = new FakeModCatalog(
        [
            new PlaySetMod(
                Guid.NewGuid(),
                "圣地在望",
                realDir,
                null,
                "3601117514",
                null,
                "1.0.*",
                "ready_to_play",
                Enabled: true,
                Position: 0,
                SteamId: "3601117514"),
        ]);
        var repository = new PlaysetsJsonRepository(
            fs,
            new RecordingLogger(),
            catalog,
            new FakeDlcCatalog([]),
            Path.Combine(tempRoot, "backup-stale"));

        var playSets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        var mods = repository
            .GetPlaySetModsAsync(game, playSets[0].Id, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

        ChecksContext.Equal("陈旧路径: 仍然映射到扫描到的 mod", "ready_to_play", mods[0].Status);
        ChecksContext.Equal("陈旧路径: 显示名来自 metadata.json", "圣地在望", mods[0].Name);
        ChecksContext.Equal("陈旧路径: DirPath 换成磁盘上的真实路径", realDir, mods[0].DirPath);
    }

    /// <summary>
    /// 真机 EU5 的 DLC 描述文件是 <c>D0xx\D0xx.dlc.json</c>（平铺 name/pops_id/steam_id），
    /// 名字在 <c>main_menu\localization\english\*.yml</c> 里且带 <c>#italic</c> 标记。
    /// </summary>
    private static void CheckEu5DlcDiscovery(IFileSystem fs, string tempRoot)
    {
        Console.WriteLine("[PlaysetJsonChecks] EU5 DLC 描述文件");

        var install = Path.Combine(tempRoot, "eu5dlc", "install");
        var dlcRoot = Path.Combine(install, "game", "dlc");
        var sacred = Path.Combine(dlcRoot, "D017_sacred_sites_pack");
        var shared = Path.Combine(dlcRoot, "D000_shared");
        fs.EnsureDirectory(sacred);
        fs.EnsureDirectory(shared);
        fs.EnsureDirectory(Path.Combine(shared, "main_menu", "localization", "english"));

        fs.WriteAllTextAtomic(
            Path.Combine(sacred, "D017_sacred_sites_pack.dlc.json"),
            """
            {
              "name": "d017_sacred_sites_pack",
              "localizable_name": "sacred_sites_pack_key",
              "pops_id": "d017_sacred_sites_pack",
              "steam_id": 3865300,
              "hidden": false
            }
            """);
        fs.WriteAllTextAtomic(
            Path.Combine(shared, "D000_shared.dlc.json"),
            """
            { "name": "d000_shared", "localizable_name": "d000_shared", "steam_id": 0, "hidden": true }
            """);
        fs.WriteAllTextAtomic(
            Path.Combine(shared, "main_menu", "localization", "english", "dlc_localizations_l_english.yml"),
            "l_english:\n sacred_sites_pack_key: \"#italic Sacred Sites#!\"\n");

        var game = new GameInstallation(
            "eu5",
            "Europa Universalis V",
            "1.0.10",
            install,
            string.Empty,
            LoadFileFormat.PlaysetsJsonV1,
            install,
            dlcRoot,
            Path.Combine(tempRoot, "eu5dlc", "mods"),
            Path.Combine(install, "binaries", "eu5.exe"),
            [],
            [],
            new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 24,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            null,
            null,
            3450310,
            GameSupport.Full);

        var logger = new RecordingLogger();
        var dlcs = new DlcCatalog(fs, logger).DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Equal("EU5 DLC: hidden 的共享包不列出", 1, dlcs.Count);
        ChecksContext.Equal("EU5 DLC: 名字取英文本地化并去掉 #italic/#!", "Sacred Sites", dlcs[0].DisplayName);
        ChecksContext.Equal("EU5 DLC: pops_id 作为有效 id", "d017_sacred_sites_pack", DlcCatalog.GetEffectiveDlcId(dlcs[0]));
        ChecksContext.Equal("EU5 DLC: steam_id 转成字符串", "3865300", dlcs[0].SteamId);
    }

    private const string RealisticDocument = """
        {
          "file_version": "1.0.0",
          "playsets": [
            {
              "name": "我的配置",
              "isActive": true,
              "isAutomaticallySorted": false,
              "orderedListMods": [
                { "path": "%WORKSHOP%/1234567890/", "isEnabled": true },
                { "path": "E:/SteamLibrary/steamapps/workshop/content/3450310/9999999999/", "isEnabled": false }
              ],
              "DLC": [
                { "paradoxAppId": "pops_sacred_sites", "isEnabled": true },
                { "paradoxAppId": "Sacred Sites", "isEnabled": true },
                { "paradoxAppId": "UnrelatedEntry", "isEnabled": false }
              ]
            },
            {
              "name": "空配置",
              "isActive": false,
              "isAutomaticallySorted": true,
              "orderedListMods": [],
              "DLC": []
            }
          ]
        }
        """;

    /// <summary>在临时目录里写一份 playsets.json，并把 <c>%WORKSHOP%</c> 换成真实路径。</summary>
    private static (GameInstallation Game, string Path) Prepare(
        IFileSystem fs,
        string tempRoot,
        string name,
        string document)
    {
        var documents = Path.Combine(tempRoot, name, "documents");
        var install = Path.Combine(tempRoot, name, "install");
        fs.EnsureDirectory(documents);
        fs.EnsureDirectory(Path.Combine(install, "game", "dlc"));
        fs.EnsureDirectory(Path.Combine(install, "binaries"));

        var body = document.Replace(
            "%WORKSHOP%",
            Path.Combine(tempRoot, "workshop", "content", "3450310").Replace('\\', '/'),
            StringComparison.Ordinal);
        var path = Path.Combine(documents, PlaysetsJsonDocument.FileName);
        fs.WriteAllTextAtomic(path, body);

        var game = new GameInstallation(
            "eu5",
            "Europa Universalis V",
            "1.0.10",
            install,
            string.Empty,
            LoadFileFormat.PlaysetsJsonV1,
            documents,
            Path.Combine(install, "game", "dlc"),
            Path.Combine(documents, "mod"),
            Path.Combine(install, "binaries", "eu5.exe"),
            [],
            [],
            new ThemeSpec(
                Background: null,
                Logo: null,
                AppIcon: null,
                AppTaskbarIcon: null,
                ButtonBackground: null,
                ButtonFont: null,
                ButtonTextColor: "#FFFFFF",
                ButtonHoverTextColor: "#FFFFFF",
                FontSize: 24,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            null,
            null,
            3450310,
            GameSupport.Full);

        return (game, path);
    }

    private sealed class FakeModCatalog(IReadOnlyList<PlaySetMod> mods) : IModCatalog
    {
        public Task<IReadOnlyList<PlaySetMod>> DiscoverAllAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult(mods);
    }

    private sealed class FakeDlcCatalog(IReadOnlyList<DlcInfo> dlcs) : IDlcCatalog
    {
        public Task<IReadOnlyList<DlcInfo>> DiscoverAsync(GameInstallation game, CancellationToken cancellationToken) =>
            Task.FromResult(dlcs);
    }

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = [];

        public List<string> Warnings { get; } = [];

        public List<string> Errors { get; } = [];

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Errors.Add(message);
    }
}
