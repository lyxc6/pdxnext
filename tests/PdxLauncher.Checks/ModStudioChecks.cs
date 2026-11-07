namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Storage;

/// <summary>
/// 「Mod 创建」页背后那层核心逻辑的自检：描述符规则、两族 mod 的生成与回读、
/// 本地清单过滤、发布记录持久化，以及发布协议的进度/结果行。
/// 全部在临时目录里跑，不碰真机 mod，也不加载 steam_api64.dll。
/// </summary>
public static class ModStudioChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckDescriptorRules();
        CheckModIdInput();
        CheckTagCatalog();
        CheckCreateLegacy(fs);
        CheckCreateMetadata(fs);
        CheckCreateRejects(fs);
        CheckLocalInventory(fs);
        CheckPublicationStore(fs, logger);
        CheckPublishProtocol();
        CheckPublishRunnerArguments();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 描述符规则
    // ------------------------------------------------------------------

    private static void CheckDescriptorRules()
    {
        Console.WriteLine("[ModStudioChecks] 描述符规则");
        Equal(
            "CK3 使用旧式 .mod",
            ModDescriptorFamily.Legacy,
            ModDescriptorRules.FamilyFor("ck3"));
        Equal(
            "V3 使用 metadata.json",
            ModDescriptorFamily.Metadata,
            ModDescriptorRules.FamilyFor("victoria3"));
        Equal(
            "EU5 使用 metadata.json",
            ModDescriptorFamily.Metadata,
            ModDescriptorRules.FamilyFor("eu5"));
        Equal(
            "CS2 不支持本启动器创建",
            ModDescriptorFamily.None,
            ModDescriptorRules.FamilyFor("cities_skylines_2"));
        ChecksContext.Check("Stellaris 可创建", ModDescriptorRules.CanCreate("stellaris"));
        ChecksContext.Check("CS2 不可创建", !ModDescriptorRules.CanCreate("cities_skylines_2"));
        ChecksContext.Check("CK3 可发布", ModDescriptorRules.CanPublish("ck3"));
        ChecksContext.Check("CS2 没有 Steam 工坊", !ModDescriptorRules.CanPublish("cities_skylines_2"));
        Equal(
            "旧式描述符文案键",
            "ModstudioDescriptorLegacy",
            ModDescriptorRules.DescribeKey(ModDescriptorFamily.Legacy));
        Equal(
            "metadata 描述符文案键",
            "ModstudioDescriptorMetadata",
            ModDescriptorRules.DescribeKey(ModDescriptorFamily.Metadata));
    }

    // ------------------------------------------------------------------
    // 上传目标：Mod ID 与标签清单
    // ------------------------------------------------------------------

    private static void CheckModIdInput()
    {
        Console.WriteLine("[ModStudioChecks] Mod ID 输入");
        ChecksContext.Check(
            "空 Mod ID 表示新建",
            ModIdInput.TryParse("   ", out var empty) && empty is null);
        ChecksContext.Check(
            "数字 Mod ID 可解析",
            ModIdInput.TryParse("123456789", out var parsed) && parsed == 123456789UL);
        ChecksContext.Check(
            "Mod ID 允许 ulong 最大值",
            ModIdInput.TryParse(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), out var max)
            && max == ulong.MaxValue);
        ChecksContext.Check("Mod ID 不接受 0", !ModIdInput.TryParse("0", out _));
        ChecksContext.Check("Mod ID 不接受负数", !ModIdInput.TryParse("-1", out _));
        ChecksContext.Check("Mod ID 不接受字母", !ModIdInput.TryParse("12a", out _));
        ChecksContext.Check(
            "Mod ID 不接受溢出值",
            !ModIdInput.TryParse("18446744073709551616", out _));
    }

    private static void CheckTagCatalog()
    {
        Console.WriteLine("[ModStudioChecks] 上传标签清单");
        var v3 = ModTagCatalog.For("victoria3");
        Equal("V3 标签数量", 24, v3.Count);
        Equal("V3 第一个标签", "Alternative History", v3[0]);
        ChecksContext.Check("V3 含 Gameplay", v3.Contains("Gameplay", StringComparer.Ordinal));
        ChecksContext.Check("CK3 有标签", ModTagCatalog.For("ck3").Count >= 12);
        Equal("未知游戏回退通用标签", 13, ModTagCatalog.For(null).Count);
    }

    // ------------------------------------------------------------------
    // 创建：CK3 旧式描述符
    // ------------------------------------------------------------------

    private static void CheckCreateLegacy(IFileSystem fs)
    {
        Console.WriteLine("[ModStudioChecks] 创建 CK3 mod");
        var root = PlaysetChecks.CreateTempRoot(fs, "modstudio-legacy");
        try
        {
            var game = CreateGame(fs, root, "ck3", "Crusader Kings III");
            var preview = Path.Combine(root, "preview.png");
            fs.WriteAllBytesAtomic(preview, [1, 2, 3, 4, 5]);

            var request = new ModCreateRequest(
                "My Test Mod",
                "my_test_mod",
                "1.2.3",
                "1.20.*",
                ["Gameplay", "Balance"],
                "A test mod",
                preview);

            var result = ModCreator.Create(game, request, fs);
            ChecksContext.Check("CK3 创建成功", result.Ok, result.ErrorCode ?? string.Empty);
            Equal(
                "返回目标目录",
                Path.Combine(game.ModDir, "my_test_mod"),
                result.DirPath ?? "<null>");

            var target = result.DirPath!;
            var outer = Path.Combine(game.ModDir, "my_test_mod.mod");
            var descriptor = Path.Combine(target, "descriptor.mod");
            var thumbnail = Path.Combine(target, "thumbnail.png");
            ChecksContext.Check("外层 .mod 已写", fs.FileExists(outer));
            ChecksContext.Check("目录内 descriptor.mod 已写", fs.FileExists(descriptor));
            ChecksContext.Check("封面已复制", fs.FileExists(thumbnail));
            ChecksContext.Check("目录已创建", fs.DirectoryExists(target));

            var outerText = fs.ReadAllText(outer);
            var descriptorText = fs.ReadAllText(descriptor);
            ChecksContext.Check("外层 .mod 带 path", outerText.Contains("path=", StringComparison.Ordinal), outerText);
            ChecksContext.Check(
                "descriptor.mod 不带 path",
                !descriptorText.Contains("path=", StringComparison.Ordinal),
                descriptorText);
            ChecksContext.Check("名称已写入", outerText.Contains("name=\"My Test Mod\"", StringComparison.Ordinal));
            ChecksContext.Check("版本已写入", outerText.Contains("version=\"1.2.3\"", StringComparison.Ordinal));
            ChecksContext.Check(
                "标签已写入",
                outerText.Contains("tags={\"Gameplay\" \"Balance\"}", StringComparison.Ordinal),
                outerText);
            EqualBytes(
                "用户封面原样复制",
                [1, 2, 3, 4, 5],
                fs.ReadAllBytes(thumbnail));

            ChecksContext.Check(
                "描述符可回读",
                ModDescriptorReader.TryRead(game, target, "fallback", fs, out var info),
                "reader");
            Equal("回读名称", "My Test Mod", info!.Name);
            Equal("回读版本", "1.2.3", info.Version);
            Equal("回读支持版本", "1.20.*", info.SupportedVersion);
            Equal("回读标签数", 2, info.Tags.Count);
            Equal("回读第一个标签", "Gameplay", info.Tags[0]);
            ChecksContext.Check("回读封面存在", info.PicturePath is not null && fs.FileExists(info.PicturePath), info.PicturePath ?? "<null>");
            ChecksContext.Check("回读描述符路径指向外层 .mod", string.Equals(info.DescriptorPath, outer, StringComparison.OrdinalIgnoreCase), info.DescriptorPath ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 创建：V3 metadata
    // ------------------------------------------------------------------

    private static void CheckCreateMetadata(IFileSystem fs)
    {
        Console.WriteLine("[ModStudioChecks] 创建 V3 mod");
        var root = PlaysetChecks.CreateTempRoot(fs, "modstudio-metadata");
        try
        {
            var game = CreateGame(fs, root, "victoria3", "Victoria 3");
            var request = new ModCreateRequest(
                "V3 Test Mod",
                "v3_test_mod",
                "0.2.0",
                "1.8.*",
                ["Economy", "Fixes"],
                "Short description",
                null);

            var result = ModCreator.Create(game, request, fs);
            ChecksContext.Check("V3 创建成功", result.Ok, result.ErrorCode ?? string.Empty);
            var target = result.DirPath!;
            var metadata = Path.Combine(target, ".metadata", "metadata.json");
            var thumbnail = Path.Combine(target, ".metadata", "thumbnail.png");
            ChecksContext.Check("metadata.json 已写", fs.FileExists(metadata));
            ChecksContext.Check("占位封面已写", fs.FileExists(thumbnail));
            ChecksContext.Check("没有外层 .mod", !fs.FileExists(Path.Combine(game.ModDir, "v3_test_mod.mod")));

            using var document = JsonDocument.Parse(fs.ReadAllText(metadata));
            var rootElement = document.RootElement;
            Equal("metadata name", "V3 Test Mod", rootElement.GetProperty("name").GetString() ?? "<null>");
            Equal("metadata id 为空", string.Empty, rootElement.GetProperty("id").GetString() ?? "<null>");
            Equal("metadata version", "0.2.0", rootElement.GetProperty("version").GetString() ?? "<null>");
            Equal(
                "metadata supported_game_version",
                "1.8.*",
                rootElement.GetProperty("supported_game_version").GetString() ?? "<null>");
            Equal(
                "metadata short_description",
                "Short description",
                rootElement.GetProperty("short_description").GetString() ?? "<null>");
            Equal(
                "metadata picture",
                "thumbnail.png",
                rootElement.GetProperty("picture").GetString() ?? "<null>");
            var tags = rootElement.GetProperty("tags");
            Equal("metadata tags 是数组", JsonValueKind.Array, tags.ValueKind);
            Equal("metadata tags 数量", 2, tags.GetArrayLength());

            ChecksContext.Check(
                "metadata 可回读",
                ModDescriptorReader.TryRead(game, target, "fallback", fs, out var info),
                "reader");
            Equal("回读 metadata 简介", "Short description", info!.Description ?? "<null>");
            Equal("回读 metadata 标签数", 2, info.Tags.Count);
            ChecksContext.Check(
                "回读 metadata 封面",
                info.PicturePath is not null && fs.FileExists(info.PicturePath),
                info.PicturePath ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 创建失败路径
    // ------------------------------------------------------------------

    private static void CheckCreateRejects(IFileSystem fs)
    {
        Console.WriteLine("[ModStudioChecks] 创建失败路径");
        var root = PlaysetChecks.CreateTempRoot(fs, "modstudio-reject");
        try
        {
            var game = CreateGame(fs, root, "ck3", "Crusader Kings III");

            var invalidName = ModCreator.Create(
                game,
                new ModCreateRequest(string.Empty, "folder", "1.0", "1.*", [], null, null),
                fs);
            Equal("空名称被拒绝", "invalid-name", invalidName.ErrorCode ?? "<null>");

            var invalidFolder = ModCreator.Create(
                game,
                new ModCreateRequest("Name", "../escape", "1.0", "1.*", [], null, null),
                fs);
            Equal("非法目录名被拒绝", "invalid-folder", invalidFolder.ErrorCode ?? "<null>");

            var created = ModCreator.Create(
                game,
                new ModCreateRequest("Name", "same_folder", "1.0", "1.*", [], null, null),
                fs);
            ChecksContext.Check("首次创建成功", created.Ok);
            var duplicate = ModCreator.Create(
                game,
                new ModCreateRequest("Name", "same_folder", "1.0", "1.*", [], null, null),
                fs);
            Equal("同名目录被拒绝", "folder-exists", duplicate.ErrorCode ?? "<null>");

            var unsupported = CreateGame(fs, root, "cities_skylines_2", "Cities Skylines II");
            var unsupportedResult = ModCreator.Create(
                unsupported,
                new ModCreateRequest("Name", "folder", "1.0", "1.*", [], null, null),
                fs);
            Equal("不支持的游戏被拒绝", "unsupported-game", unsupportedResult.ErrorCode ?? "<null>");

            var v3Root = Path.Combine(root, "v3", "中文目录");
            fs.EnsureDirectory(v3Root);
            var v3Game = CreateGame(fs, root, "victoria3", "Victoria 3") with
            {
                ModDir = Path.Combine(v3Root, "mod"),
            };
            fs.EnsureDirectory(v3Game.ModDir);
            var ascii = ModCreator.Create(
                v3Game,
                new ModCreateRequest("Name", "ascii_folder", "1.0", "1.*", [], null, null),
                fs);
            Equal("V3 非 ASCII 路径被拒绝", "ascii-required", ascii.ErrorCode ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 本地清单合并
    // ------------------------------------------------------------------

    private static void CheckLocalInventory(IFileSystem fs)
    {
        Console.WriteLine("[ModStudioChecks] 本地清单合并");
        var root = PlaysetChecks.CreateTempRoot(fs, "modstudio-inventory");
        try
        {
            var game = CreateGame(fs, root, "ck3", "Crusader Kings III");
            fs.EnsureDirectory(game.ModDir);

            var localId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var archiveId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var workshopId = Guid.Parse("33333333-3333-3333-3333-333333333333");
            var catalogId = Guid.Parse("44444444-4444-4444-4444-444444444444");
            var duplicateId = localId;

            var localPath = Path.Combine(game.ModDir, "local_mod");
            var workshopPath = Path.Combine(root, "library", "steamapps", "workshop", "content", "1158310", "42");
            var archivePath = Path.Combine(game.ModDir, "local_archive.zip");
            var workshopArchive = Path.Combine(root, "library", "steamapps", "workshop", "content", "1158310", "43.zip");

            var known = new[]
            {
                Entry(localId, "Local Mod", localPath, source: ModOrigins.SteamSource, steamId: "42"),
                Entry(workshopId, "Workshop Mod", workshopPath, source: ModOrigins.SteamSource, steamId: "42"),
                Entry(archiveId, "Archive Mod", dirPath: null, archivePath: archivePath),
                Entry(Guid.NewGuid(), "Workshop Archive", dirPath: null, archivePath: workshopArchive),
            };
            var catalog = new[]
            {
                PlaySetMod(catalogId, "Catalog Mod", Path.Combine(game.ModDir, "catalog_mod")),
                PlaySetMod(duplicateId, "Duplicate", localPath),
            };

            var merged = LocalModInventory.Merge(game, known, catalog, fs);
            Equal("本地清单数量", 3, merged.Count);
            var local = merged.Single(entry => entry.ModId == localId);
            Equal("已知本地行强制 source=local", ModOrigins.LocalSource, local.Source);
            ChecksContext.Check("已知本地行清空 steamId", local.SteamId is null);
            var archive = merged.Single(entry => entry.ModId == archiveId);
            Equal("本地归档保留", "Archive Mod", archive.Name);
            var catalogRow = merged.Single(entry => entry.ModId == catalogId);
            ChecksContext.Check("扫描到的本地行标记未登记", catalogRow.IsUnregistered);
            Equal("扫描到的本地行 source=local", ModOrigins.LocalSource, catalogRow.Source);
            ChecksContext.Check("工坊目录被排除", merged.All(entry => entry.ModId != workshopId));
            ChecksContext.Check("重复 ModId 只保留一条", merged.Count(entry => entry.ModId == duplicateId) == 1);
            ChecksContext.Check("清单按名称排序", merged.Select(entry => entry.Name).SequenceEqual(merged.Select(entry => entry.Name).OrderBy(name => name, StringComparer.OrdinalIgnoreCase)));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 发布记录
    // ------------------------------------------------------------------

    private static void CheckPublicationStore(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[ModStudioChecks] 发布记录持久化");
        var root = PlaysetChecks.CreateTempRoot(fs, "modstudio-publication");
        try
        {
            var database = new PdxDatabase(fs, logger, Path.Combine(root, "pdxnext.sqlite"));
            var documents = new SqliteJsonDocumentStore(database, logger, SystemClock.Instance);
            var store = new ModPublicationStore(documents);
            var publication = new ModPublication(
                "ck3",
                Path.Combine(root, "mods", "My Mod"),
                123456789UL,
                "Workshop Title",
                "Workshop Description",
                ["Gameplay"],
                Public: true,
                PreviewPath: null,
                LastError: null,
                UpdatedAt: DateTimeOffset.UnixEpoch);

            ChecksContext.Check("保存发布记录", store.Save(publication));
            var read = store.TryGet("ck3", publication.ModPath);
            ChecksContext.Check("读回发布记录", read is not null);
            Equal("发布 id 往返", 123456789UL, read!.PublishedFileId ?? 0);
            Equal("标题往返", "Workshop Title", read.Title);
            Equal("标签往返", 1, read.Tags.Count);

            var list = store.List("ck3");
            Equal("按游戏列出记录", 1, list.Count);
            Equal("列表记录标题", "Workshop Title", list[0].Title);

            ChecksContext.Check("删除发布记录", store.Delete("ck3", publication.ModPath));
            ChecksContext.Check("删除后读不到", store.TryGet("ck3", publication.ModPath) is null);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 发布协议
    // ------------------------------------------------------------------

    private static void CheckPublishProtocol()
    {
        Console.WriteLine("[ModStudioChecks] 发布协议");
        var progress = new ModPublishProgress("uploading", 42, 1024, 4096);
        var line = SteamHostProtocol.EncodeProgress(progress);
        ChecksContext.Check("进度行带前缀", line.StartsWith(SteamHostProtocol.ProgressPrefix, StringComparison.Ordinal), line);
        ChecksContext.Check(
            "进度行可解码",
            SteamHostProtocol.TryDecodeProgress<ModPublishProgress>(line + "\n", out var decodedProgress),
            "progress");
        Equal("进度阶段往返", "uploading", decodedProgress!.Stage);
        Equal("进度百分比往返", 42, decodedProgress.Percent);
        Equal("已上传字节往返", 1024UL, decodedProgress.BytesUploaded);

        var result = new ModPublishResult(true, 987654321UL, NeedsLegalAgreement: false, Error: null);
        var resultLine = SteamHostProtocol.Encode(result);
        ChecksContext.Check(
            "发布结果可解码",
            SteamHostProtocol.TryDecode<ModPublishResult>(resultLine, out var decodedResult),
            "result");
        ChecksContext.Check("发布结果 Ok", decodedResult!.Ok);
        Equal("发布结果 id", 987654321UL, decodedResult.PublishedFileId ?? 0);
        ChecksContext.Check(
            "进度行不会被当成最终结果",
            !SteamHostProtocol.TryDecode<ModPublishResult>(line, out _));

        var legal = new ModPublishResult(false, 5UL, NeedsLegalAgreement: true, Error: "legal");
        ChecksContext.Check(
            "法律协议标记往返",
            SteamHostProtocol.TryDecode<ModPublishResult>(SteamHostProtocol.Encode(legal), out var decodedLegal)
            && decodedLegal!.NeedsLegalAgreement);

        var request = new ModPublishRequest(
            1158310,
            null,
            @"C:\mods\My Mod",
            @"C:\mods\cover.png",
            "Title",
            "Description",
            "note",
            ["Gameplay"],
            SteamWorkshopVisibility.Public);
        ChecksContext.Check(
            "发布请求可解码",
            SteamHostProtocol.TryDecode<ModPublishRequest>(SteamHostProtocol.Encode(request), out var decodedRequest),
            "request");
        Equal("发布请求 appid", 1158310, decodedRequest!.AppId);
        Equal("发布请求标签数", 1, decodedRequest.Tags.Count);
    }

    // ------------------------------------------------------------------
    // 宿主发布参数
    // ------------------------------------------------------------------

    private static void CheckPublishRunnerArguments()
    {
        Console.WriteLine("[ModStudioChecks] 宿主发布参数");
        var missingRequestOut = new StringWriter(CultureInfo.InvariantCulture);
        var missingRequestErr = new StringWriter(CultureInfo.InvariantCulture);
        var code = SteamHostRunner.Run(
            [
                SteamHostProtocol.VerbArgument,
                SteamHostProtocol.WorkshopPublishVerb,
                "--api", @"C:\nope\steam_api64.dll",
                "--appid", "1158310",
            ],
            missingRequestOut,
            missingRequestErr);
        Equal("发布缺 --request 以失败码退出", SteamHostProtocol.ExitFailure, code);
        ChecksContext.Check("发布缺 --request 时提示用法", missingRequestErr.ToString().Contains("--request", StringComparison.Ordinal), missingRequestErr.ToString());

        var emptyContent = new ModPublishRequest(1158310, null, string.Empty, null, "t", "d", null, [], SteamWorkshopVisibility.Public);
        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(emptyContent, SteamHostProtocol.JsonOptions)));
        var emptyContentOut = new StringWriter(CultureInfo.InvariantCulture);
        var emptyContentErr = new StringWriter(CultureInfo.InvariantCulture);
        code = SteamHostRunner.Run(
            [
                SteamHostProtocol.VerbArgument,
                SteamHostProtocol.WorkshopPublishVerb,
                "--api", @"C:\nope\steam_api64.dll",
                "--appid", "1158310",
                "--request", encoded,
            ],
            emptyContentOut,
            emptyContentErr);
        Equal("发布缺内容目录以失败码退出", SteamHostProtocol.ExitFailure, code);
        ChecksContext.Check("发布缺内容目录时提示", emptyContentErr.ToString().Contains("内容目录", StringComparison.Ordinal), emptyContentErr.ToString());
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static GameInstallation CreateGame(IFileSystem fs, string root, string gameId, string folder)
    {
        var installRoot = Path.Combine(root, "library", "steamapps", "common", folder);
        var gameDataDir = Path.Combine(root, "documents", folder);
        var modDir = Path.Combine(gameDataDir, "mod");
        fs.EnsureDirectory(installRoot);
        fs.EnsureDirectory(gameDataDir);
        fs.EnsureDirectory(modDir);
        return PlaysetChecks.CreateGame(
            gameId,
            installRoot,
            LoadFileFormat.DlcLoadV0,
            gameDataDir,
            Path.Combine(installRoot, "game", "dlc"),
            modDir,
            gameId == "ck3" ? 1158310 : null);
    }

    private static ModLibraryEntry Entry(
        Guid id,
        string name,
        string? dirPath,
        string? archivePath = null,
        string source = ModOrigins.LocalSource,
        string? steamId = null) =>
        new(
            id,
            name,
            "ck3",
            dirPath,
            archivePath,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Source: source,
            SizeBytes: null,
            SubscribedOn: null,
            PlaySetIds: Array.Empty<Guid>(),
            IsUnregistered: false,
            SteamId: steamId);

    private static PlaySetMod PlaySetMod(Guid id, string name, string dirPath) =>
        new(
            id,
            name,
            dirPath,
            ArchivePath: null,
            GameRegistryId: "ck3",
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Enabled: true,
            Position: 0,
            SteamId: null,
            Source: ModOrigins.LocalSource);

    private static void Equal<T>(string label, T expected, T actual) => ChecksContext.Equal(label, expected, actual);

    private static void EqualBytes(string label, byte[] expected, byte[] actual) => ChecksContext.EqualBytes(label, expected, actual);

    /// <summary>自检用的静默日志。</summary>
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
