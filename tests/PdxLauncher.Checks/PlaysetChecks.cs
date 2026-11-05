namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;

/// <summary>
/// T2 自检：winsqlite3 P/Invoke 封装、PlaySetRepository、DlcCatalog、ModCatalog。
/// <para>临时库用例全部在系统临时目录里跑，绝不碰真机 launcher-v2.sqlite；真机段一律只读打开。</para>
/// </summary>
public static class PlaysetChecks
{
    private const string Ck3GameId = "ck3";
    private const string Victoria3GameId = "victoria3";
    private const string StellarisGameId = "stellaris";

    private const string Ck3DataFolder = "Crusader Kings III";
    private const string Victoria3DataFolder = "Victoria 3";
    private const string StellarisDataFolder = "Stellaris";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var clock = SystemClock.Instance;

        CheckSqliteNative(fs);
        CheckSqliteConnectionOnTempDatabase(fs);
        CheckRepositoryOnTempDatabase(fs, logger, clock);
        CheckDlcCatalogOnFixtures(fs, logger);
        CheckModVersion();
        CheckRealMachine(fs, logger, clock);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // winsqlite3 P/Invoke
    // ------------------------------------------------------------------

    private static void CheckSqliteNative(IFileSystem fs)
    {
        Console.WriteLine("[PlaysetChecks] winsqlite3 P/Invoke");

        var version = SqliteConnection.LibraryVersion;
        ChecksContext.Check("winsqlite3 版本号非空", !string.IsNullOrWhiteSpace(version), version);
        ChecksContext.Check(
            "winsqlite3 版本 >= 3.30（支持 RETURNING/upsert 语法）",
            Version.TryParse(version, out var parsed) && parsed >= new Version(3, 30),
            version);

        var systemLibrary = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            SqliteNative.LibraryName);
        ChecksContext.Check("System32 下存在 winsqlite3.dll（无需 NuGet 包）", fs.FileExists(systemLibrary), systemLibrary);
    }

    // ------------------------------------------------------------------
    // SqliteConnection（临时库）
    // ------------------------------------------------------------------

    private static void CheckSqliteConnectionOnTempDatabase(IFileSystem fs)
    {
        Console.WriteLine("[PlaysetChecks] SqliteConnection（临时库）");

        var tempRoot = CreateTempRoot(fs, "sqlite");
        try
        {
            var dbPath = Path.Combine(tempRoot, "probe.sqlite");

            using (var connection = SqliteConnection.OpenOrCreate(dbPath))
            {
                ChecksContext.Check("OpenOrCreate 后 IsReadOnly 为 false", !connection.IsReadOnly);
                ChecksContext.Equal("FilePath 为传入路径", dbPath, connection.FilePath);

                connection.Execute("CREATE TABLE probe (id INTEGER PRIMARY KEY, name TEXT, flag INTEGER, blob BLOB)");
                ChecksContext.Check("TableExists(probe) 为 true", connection.TableExists("probe"));
                ChecksContext.Check("TableExists(不存在的表) 为 false", !connection.TableExists("no_such_table"));

                connection.Execute(
                    "INSERT INTO probe (id, name, flag, blob) VALUES (?, ?, ?, ?)",
                    1,
                    "中文测试 🙂 ünïcode",
                    true,
                    new byte[] { 1, 2, 3, 4 });
                connection.Execute(
                    "INSERT INTO probe (id, name, flag, blob) VALUES (?, ?, ?, ?)",
                    2,
                    null,
                    false,
                    null);

                var rows = connection.Query("SELECT id, name, flag, blob FROM probe ORDER BY id ASC");
                ChecksContext.Equal("probe 行数", 2, rows.Count);
                ChecksContext.Equal("UTF-8 中文往返一致", "中文测试 🙂 ünïcode", rows[0]["name"] as string ?? "<null>");
                ChecksContext.Equal(
                    "bool 绑定后读出 1",
                    1L,
                    Convert.ToInt64(rows[0]["flag"], CultureInfo.InvariantCulture));
                ChecksContext.Equal(
                    "整数列读出 long",
                    1L,
                    Convert.ToInt64(rows[0]["id"], CultureInfo.InvariantCulture));
                ChecksContext.EqualBytes("blob 列往返一致", new byte[] { 1, 2, 3, 4 }, rows[0]["blob"] as byte[] ?? Array.Empty<byte>());
                ChecksContext.Check("NULL 文本列读出 null（不是空字符串）", rows[1]["name"] is null);
                ChecksContext.Check("NULL blob 列读出 null", rows[1]["blob"] is null);

                ChecksContext.Check("ExecuteScalar(SELECT NULL) 返回 null", connection.ExecuteScalar("SELECT NULL") is null);
                ChecksContext.Equal(
                    "ExecuteScalar(SELECT '') 返回空串",
                    string.Empty,
                    connection.ExecuteScalar("SELECT ''") as string ?? "<null>");

                // 事务：ROLLBACK 不落盘，COMMIT 落盘。
                connection.BeginImmediate();
                ChecksContext.Check("BeginImmediate 后 IsInTransaction 为 true", connection.IsInTransaction);
                connection.Execute("INSERT INTO probe (id, name) VALUES (?, ?)", 90, "rollback");
                connection.Rollback();
                ChecksContext.Check("Rollback 后 IsInTransaction 为 false", !connection.IsInTransaction);
                ChecksContext.Equal("Rollback 后行数不变", 2, connection.Query("SELECT id FROM probe").Count);

                connection.BeginImmediate();
                connection.Execute("INSERT INTO probe (id, name) VALUES (?, ?)", 3, "commit");
                connection.Commit();
                ChecksContext.Equal("Commit 后行数 +1", 3, connection.Query("SELECT id FROM probe").Count);

                var duplicateBeginThrown = false;
                connection.BeginImmediate();
                try
                {
                    connection.BeginImmediate();
                }
                catch (InvalidOperationException)
                {
                    duplicateBeginThrown = true;
                }
                finally
                {
                    connection.Rollback();
                }

                ChecksContext.Check("重复 BEGIN IMMEDIATE 抛 InvalidOperationException", duplicateBeginThrown);
            }

            // 未提交事务在 Dispose 时必须自动回滚。
            using (var connection = SqliteConnection.OpenReadWrite(dbPath))
            {
                connection.BeginImmediate();
                connection.Execute("INSERT INTO probe (id, name) VALUES (?, ?)", 4, "uncommitted");
            }

            using (var connection = SqliteConnection.OpenReadOnly(dbPath))
            {
                ChecksContext.Check("OpenReadOnly 后 IsReadOnly 为 true", connection.IsReadOnly);
                ChecksContext.Equal("Dispose 自动回滚未提交事务", 3, connection.Query("SELECT id FROM probe").Count);

                var readOnlyWriteRejected = false;
                try
                {
                    connection.Execute("INSERT INTO probe (id, name) VALUES (100, 'x')");
                }
                catch (SqliteException exception)
                {
                    readOnlyWriteRejected = exception.ResultCode != SqliteNative.Ok;
                }

                ChecksContext.Check("只读连接拒绝写入并抛 SqliteException", readOnlyWriteRejected);
            }

            var missingThrown = false;
            try
            {
                using var missing = SqliteConnection.OpenReadOnly(Path.Combine(tempRoot, "missing.sqlite"));
            }
            catch (SqliteException)
            {
                missingThrown = true;
            }

            ChecksContext.Check("打开不存在的数据库抛 SqliteException", missingThrown);
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }
    }

    // ------------------------------------------------------------------
    // PlaySetRepository（临时库）
    // ------------------------------------------------------------------

    private static void CheckRepositoryOnTempDatabase(IFileSystem fs, IAppLogger logger, IClock clock)
    {
        Console.WriteLine("[PlaysetChecks] PlaySetRepository（临时库）");

        var tempRoot = CreateTempRoot(fs, "repository");
        try
        {
            var gameDataDir = Path.Combine(tempRoot, "data");
            fs.EnsureDirectory(gameDataDir);

            var installRoot = Path.Combine(tempRoot, "install");
            var game = CreateGame(
                Ck3GameId,
                installRoot,
                LoadFileFormat.DlcLoadV0,
                gameDataDir,
                Path.Combine(installRoot, "game"),
                Path.Combine(gameDataDir, "mod"),
                1158310);

            var dbPath = PlaySetRepository.GetDatabasePath(game);
            ChecksContext.Equal("数据库路径 = <gameDataDir>/launcher-v2.sqlite", Path.Combine(gameDataDir, "launcher-v2.sqlite"), dbPath);

            SeedLauncherDatabase(dbPath);

            // 缩略图解析器必须注入空 Steam 库列表：种子 Mod A 的 steamId 2216623537 在本机真实
            // 存在（E:\\SteamLibrary\\steamapps\\workshop\\content\\1158310\\2216623537 带 thumbnail.png），
            // 不隔离的话「缩略图不存在时 ThumbnailPath 为 null」会依赖本机安装状态而变 flaky。
            var thumbnails = new ModThumbnailResolver(fs, logger, () => Array.Empty<string>());
            var repository = new PlaySetRepository(fs, logger, clock, thumbnails);

            var olderId = Guid.Parse("11111111-1111-1111-1111-111111111111");
            var newerId = Guid.Parse("22222222-2222-2222-2222-222222222222");
            var modAId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
            var modBId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
            var modCId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

            // --- 读取 ---
            var playsets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("GetPlaySetsAsync 数量", 2, playsets.Count);
            ChecksContext.Equal("按 createdOn ASC 排序后第 1 个名称", "旧播放集", playsets[0].Name);
            ChecksContext.Equal("按 createdOn ASC 排序后第 1 个 Id", olderId, playsets[0].Id);
            ChecksContext.Check("旧播放集未激活", !playsets[0].IsActive);
            ChecksContext.Equal("第 2 个名称（中文）", "甜蜜约会", playsets[1].Name);
            ChecksContext.Check("甜蜜约会处于激活状态", playsets[1].IsActive);

            var active = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("GetActivePlaySetAsync 返回非 null", active is not null);
            ChecksContext.Equal("激活播放集 Id", newerId, active?.Id ?? Guid.Empty);
            ChecksContext.Equal("激活播放集名称", "甜蜜约会", active?.Name ?? "<null>");

            var activeMods = repository.GetPlaySetModsAsync(game, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("激活播放集 mod 行数", 3, activeMods.Count);
            ChecksContext.Equal("第 1 行 Position", 0, activeMods[0].Position);
            ChecksContext.Equal("第 1 行 ModId", modAId, activeMods[0].ModId);
            ChecksContext.Equal("第 1 行名称", "Mod A", activeMods[0].Name);
            ChecksContext.Equal("第 2 行 Position", 1, activeMods[1].Position);
            ChecksContext.Check("第 2 行 Enabled 为 false", !activeMods[1].Enabled);
            ChecksContext.Equal("第 3 行 Position", 2, activeMods[2].Position);
            ChecksContext.Check("第 3 行 Enabled 为 true", activeMods[2].Enabled);
            ChecksContext.Check("archivePath 为 NULL 时 ArchivePath 为 null（不是空串）", activeMods[0].ArchivePath is null);
            ChecksContext.Equal("第 3 行 ArchivePath", "C:/fake/mods/Mod C.zip", activeMods[2].ArchivePath ?? "<null>");
            ChecksContext.Check("缩略图不存在时 ThumbnailPath 为 null", activeMods[0].ThumbnailPath is null);
            ChecksContext.Equal("第 1 行 Status", "ready_to_play", activeMods[0].Status);
            ChecksContext.Equal("第 1 行 RequiredVersion", "1.20.*", activeMods[0].RequiredVersion);
            ChecksContext.Equal("第 1 行 GameRegistryId", "mod/Mod A.mod", activeMods[0].GameRegistryId);

            var activeDlcs = repository.GetPlaySetDlcsAsync(game, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("激活播放集 DLC 行数", 1, activeDlcs.Count);
            ChecksContext.Equal("DLC Id", "dlc_b", activeDlcs[0].DlcId);
            ChecksContext.Check("DLC 未启用", !activeDlcs[0].Enabled);

            // --- Mod 库（全部 mod + 播放集归属，只读投影）---
            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("GetModLibraryAsync 行数 = mods 表行数", 3, library.Count);
            ChecksContext.Equal("Mod 库按 displayName 升序第 1 行", "Mod A", library[0].Name);
            ChecksContext.Equal("Mod 库按 displayName 升序第 2 行", "Mod B", library[1].Name);
            ChecksContext.Equal("Mod 库按 displayName 升序第 3 行", "Mod C", library[2].Name);
            ChecksContext.Equal("Mod 库第 1 行 ModId", modAId, library[0].ModId);
            ChecksContext.Equal("Mod 库 Mod A 的 size 读出 long", 1034853L, library[0].SizeBytes ?? -1L);
            ChecksContext.Equal(
                "Mod 库 Mod A 订阅时间（unix 秒 → UTC）",
                DateTimeOffset.FromUnixTimeSeconds(1766506698),
                library[0].SubscribedOn ?? DateTimeOffset.MinValue);
            ChecksContext.Check("Mod 库 size 为 NULL 时 SizeBytes 为 null", library[2].SizeBytes is null);
            ChecksContext.Check("Mod 库 subscribedDate 为 NULL 时 SubscribedOn 为 null", library[2].SubscribedOn is null);
            ChecksContext.Check(
                "Mod 库 Mod A 归属唯一播放集（甜蜜约会）",
                library[0].PlaySetIds.Count == 1 && library[0].PlaySetIds[0] == newerId);
            ChecksContext.Check(
                "Mod 库 Mod C 归属唯一播放集（甜蜜约会）",
                library[2].PlaySetIds.Count == 1 && library[2].PlaySetIds[0] == newerId);
            ChecksContext.Check("Mod 库 IsInPlaySet 全为 true", library.All(entry => entry.IsInPlaySet));
            ChecksContext.Check("Mod 库 Source 原样保留", library.All(entry => entry.Source == "local"));
            ChecksContext.Check("Mod 库 IsLocalOnly 与 source 一致", library.All(entry => entry.IsLocalOnly));
            ChecksContext.Equal("Mod 库 Mod A 的 GameRegistryId", "mod/Mod A.mod", library[0].GameRegistryId);
            ChecksContext.Equal("Mod 库 Mod C 保留 ArchivePath", "C:/fake/mods/Mod C.zip", library[2].ArchivePath ?? "<null>");
            ChecksContext.Equal("Mod 库 Mod A 读出 steamId", "2216623537", library[0].SteamId ?? "<null>");
            ChecksContext.Check("Mod 库 steamId 为 NULL 时 SteamId 为 null", library[1].SteamId is null);
            ChecksContext.Equal(
                "Mod 库 steamId 可拼出浏览器地址",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=2216623537",
                SteamWorkshop.GetBrowserAddress(SteamWorkshop.GetWorkshopId(library[0].SteamId, library[0].GameRegistryId)) ?? "<null>");

            // --- 写：保存 mod 列表（同时验证备份）---
            var savedMods = new List<PlaySetMod>
            {
                new(modCId, "Mod C", "C:/fake/mods/Mod C", null, "mod/Mod C.mod", null, "1.20.*", "ready_to_play", true, 0),
                new(modAId, "Mod A", "C:/fake/mods/Mod A", null, "mod/Mod A.mod", null, "1.20.*", "ready_to_play", false, 1),
            };

            repository.SavePlaySetModsAsync(game, newerId, savedMods, CancellationToken.None).GetAwaiter().GetResult();
            var backupPath = PlaySetRepository.GetBackupPath(dbPath);
            ChecksContext.Check("写操作前生成 .pdxnext.bak 备份", fs.FileExists(backupPath), backupPath);

            var afterSave = repository.GetPlaySetModsAsync(game, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("保存后 mod 行数", 2, afterSave.Count);
            ChecksContext.Equal("保存后第 1 行 ModId", modCId, afterSave[0].ModId);
            ChecksContext.Equal("保存后第 1 行 Position", 0, afterSave[0].Position);
            ChecksContext.Check("保存后第 2 行 Enabled 为 false", !afterSave[1].Enabled);
            ChecksContext.Equal("保存后第 2 行 Position", 1, afterSave[1].Position);

            // --- 写：保存 DLC 列表 ---
            var savedDlcs = new List<PlaySetDlc>
            {
                new("dlc_y", false),
                new("dlc_x", true),
            };

            repository.SavePlaySetDlcsAsync(game, newerId, savedDlcs, CancellationToken.None).GetAwaiter().GetResult();
            var afterDlcSave = repository.GetPlaySetDlcsAsync(game, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("保存后 DLC 行数", 2, afterDlcSave.Count);
            ChecksContext.Equal("DLC 按 dlcId 升序第 1 行", "dlc_x", afterDlcSave[0].DlcId);
            ChecksContext.Check("dlc_x 已启用", afterDlcSave[0].Enabled);
            ChecksContext.Equal("DLC 按 dlcId 升序第 2 行", "dlc_y", afterDlcSave[1].DlcId);

            // --- 创建 / 激活 / 重命名 / 删除 ---
            var createdResult = repository.CreatePlaySetAsync(game, "  新建播放集  ", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("CreatePlaySetAsync 成功", createdResult.IsSuccess, createdResult.ErrorMessage);
            var createdId = createdResult.Value;
            ChecksContext.Check("CreatePlaySetAsync 返回非空 Guid", createdId != Guid.Empty);
            var afterCreate = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("创建后播放集数量", 3, afterCreate.Count);
            ChecksContext.Equal("新建播放集名称已 Trim", "新建播放集", afterCreate[2].Name);
            ChecksContext.Check("新建播放集默认不激活", !afterCreate[2].IsActive);

            repository.SetActivePlaySetAsync(game, createdId, CancellationToken.None).GetAwaiter().GetResult();
            var activeAfterSwitch = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("切换激活后 Id 正确", createdId, activeAfterSwitch?.Id ?? Guid.Empty);
            var allAfterSwitch = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("切换激活后只有一个 isActive=1", 1, allAfterSwitch.Count(item => item.IsActive));

            var renamed = repository.RenamePlaySetAsync(game, createdId, "  重命名后  ", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("RenamePlaySetAsync 命中成功", renamed.IsSuccess && renamed.Value, renamed.ErrorMessage);
            var afterRename = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("重命名结果已 Trim", "重命名后", afterRename.Single(item => item.Id == createdId).Name);

            var renameEmpty = repository.RenamePlaySetAsync(game, createdId, "   ", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("RenamePlaySetAsync 空名返回 Invalid（不抛）", !renameEmpty.IsSuccess && renameEmpty.Kind == ErrorKind.Invalid, renameEmpty.ErrorMessage);
            var renameMissing = repository.RenamePlaySetAsync(game, Guid.NewGuid(), "x", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("RenamePlaySetAsync 不存在的 Id 返回 NotFound", !renameMissing.IsSuccess && renameMissing.Kind == ErrorKind.NotFound, renameMissing.ErrorMessage);

            var deleteActive = repository.DeletePlaySetAsync(game, createdId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("DeletePlaySetAsync 拒绝删除激活播放集", !deleteActive.IsSuccess && deleteActive.Kind == ErrorKind.Conflict, deleteActive.ErrorMessage);

            repository.SetActivePlaySetAsync(game, olderId, CancellationToken.None).GetAwaiter().GetResult();
            var deleteInactive = repository.DeletePlaySetAsync(game, createdId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("DeletePlaySetAsync 删除非激活播放集成功", deleteInactive.IsSuccess && deleteInactive.Value, deleteInactive.ErrorMessage);
            var afterDelete = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("删除后播放集数量", 2, afterDelete.Count);
            ChecksContext.Check("删除后 Id 不存在", afterDelete.All(item => item.Id != createdId));

            var deleteMissing = repository.DeletePlaySetAsync(game, Guid.NewGuid(), CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("DeletePlaySetAsync 不存在的 Id 返回 NotFound", !deleteMissing.IsSuccess && deleteMissing.Kind == ErrorKind.NotFound, deleteMissing.ErrorMessage);

            // --- 没有 playsets_dlcs 表（Stellaris 形态）---
            var stellarisDataDir = Path.Combine(tempRoot, "data-stellaris");
            fs.EnsureDirectory(stellarisDataDir);
            var stellarisGame = CreateGame(
                StellarisGameId,
                installRoot,
                LoadFileFormat.DlcLoadV0,
                stellarisDataDir,
                installRoot,
                Path.Combine(stellarisDataDir, "mod"),
                281990);
            SeedDatabaseWithoutDlcTable(PlaySetRepository.GetDatabasePath(stellarisGame));

            var stellarisDlcs = repository.GetPlaySetDlcsAsync(stellarisGame, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("没有 playsets_dlcs 表时 GetPlaySetDlcsAsync 返回空（不抛）", 0, stellarisDlcs.Count);

            var saveDlcsOnMissingTableThrew = false;
            try
            {
                repository.SavePlaySetDlcsAsync(stellarisGame, newerId, savedDlcs, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (SqliteException)
            {
                saveDlcsOnMissingTableThrew = true;
            }

            ChecksContext.Check("没有 playsets_dlcs 表时 SavePlaySetDlcsAsync 静默跳过", !saveDlcsOnMissingTableThrew);

            // --- 数据库不存在时读取按空结果处理，不抛 ---
            var emptyDataDir = Path.Combine(tempRoot, "data-empty");
            fs.EnsureDirectory(emptyDataDir);
            var emptyGame = CreateGame(
                Ck3GameId,
                installRoot,
                LoadFileFormat.DlcLoadV0,
                emptyDataDir,
                Path.Combine(installRoot, "game"),
                Path.Combine(emptyDataDir, "mod"),
                1158310);

            var emptyPlaysets = repository.GetPlaySetsAsync(emptyGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("库不存在时 GetPlaySetsAsync 返回空", 0, emptyPlaysets.Count);
            var emptyActive = repository.GetActivePlaySetAsync(emptyGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("库不存在时 GetActivePlaySetAsync 返回 null", emptyActive is null);
            var emptyMods = repository.GetPlaySetModsAsync(emptyGame, newerId, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("库不存在时 GetPlaySetModsAsync 返回空", 0, emptyMods.Count);
            var emptyLibrary = repository.GetModLibraryAsync(emptyGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("库不存在时 GetModLibraryAsync 返回空", 0, emptyLibrary.Count);

            // --- 旧版库没有 mods.steamId 列：Mod 库照常读出，SteamId 为 null（不能整体失败）---
            var legacyDataDir = Path.Combine(tempRoot, "data-legacy");
            fs.EnsureDirectory(legacyDataDir);
            var legacyGame = CreateGame(
                Ck3GameId,
                installRoot,
                LoadFileFormat.DlcLoadV0,
                legacyDataDir,
                Path.Combine(installRoot, "game"),
                Path.Combine(legacyDataDir, "mod"),
                1158310);
            var legacyDbPath = PlaySetRepository.GetDatabasePath(legacyGame);
            SeedDatabaseWithoutSteamIdColumn(legacyDbPath);
            var legacyLibrary = repository.GetModLibraryAsync(legacyGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("旧库缺 steamId 列时 Mod 库仍读出全部行", 1, legacyLibrary.Count);
            ChecksContext.Check("旧库缺 steamId 列时 SteamId 为 null", legacyLibrary[0].SteamId is null);
            ChecksContext.Equal(
                "旧库缺 steamId 列时回退用 gameRegistryId 拼地址",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=987654",
                SteamWorkshop.GetBrowserAddress(SteamWorkshop.GetWorkshopId(legacyLibrary[0].SteamId, legacyLibrary[0].GameRegistryId)) ?? "<null>");
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }
    }

    private static void SeedLauncherDatabase(string databasePath)
    {
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE playsets (id char(36) PRIMARY KEY, name varchar(255) NOT NULL, isActive boolean, loadOrder integer, createdOn datetime NOT NULL, updatedOn datetime)");
        connection.Execute(
            "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), thumbnailPath text, dirPath text, archivePath text, gameRegistryId text, steamId text, requiredVersion varchar(255), status text NOT NULL, source text NOT NULL, size integer, subscribedDate integer)");
        connection.Execute(
            "CREATE TABLE playsets_mods (playsetId char(36), modId char(36), enabled boolean DEFAULT '1', position integer)");
        connection.Execute(
            "CREATE TABLE playsets_dlcs (playsetId char(36), dlcId text NOT NULL, enabled boolean NOT NULL DEFAULT '0', PRIMARY KEY (playsetId, dlcId))");

        connection.Execute(
            "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, ?, ?)",
            "11111111-1111-1111-1111-111111111111",
            "旧播放集",
            0,
            1000L);
        connection.Execute(
            "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "甜蜜约会",
            1,
            2000L);

        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, archivePath, gameRegistryId, thumbnailPath, requiredVersion, status, source, size, subscribedDate, steamId) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            "Mod A",
            "C:/fake/mods/Mod A",
            null,
            "mod/Mod A.mod",
            null,
            "1.20.*",
            "ready_to_play",
            "local",
            1034853L,
            1766506698L,
            "2216623537");
        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, archivePath, gameRegistryId, thumbnailPath, requiredVersion, status, source, size, subscribedDate) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            "Mod B",
            "C:/fake/mods/Mod B",
            null,
            "mod/Mod B.mod",
            null,
            "1.20.*",
            "ready_to_play",
            "local",
            544226L,
            1754308184L);
        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, archivePath, gameRegistryId, thumbnailPath, requiredVersion, status, source) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "cccccccc-cccc-cccc-cccc-cccccccccccc",
            "Mod C",
            "C:/fake/mods/Mod C",
            "C:/fake/mods/Mod C.zip",
            "mod/Mod C.mod",
            null,
            "1.20.*",
            "ready_to_play",
            "local");

        connection.Execute(
            "INSERT INTO playsets_mods (playsetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa",
            1,
            0);
        connection.Execute(
            "INSERT INTO playsets_mods (playsetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb",
            0,
            1);
        connection.Execute(
            "INSERT INTO playsets_mods (playsetId, modId, enabled, position) VALUES (?, ?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "cccccccc-cccc-cccc-cccc-cccccccccccc",
            1,
            2);

        connection.Execute(
            "INSERT INTO playsets_dlcs (playsetId, dlcId, enabled) VALUES (?, ?, ?)",
            "11111111-1111-1111-1111-111111111111",
            "dlc_a",
            1);
        connection.Execute(
            "INSERT INTO playsets_dlcs (playsetId, dlcId, enabled) VALUES (?, ?, ?)",
            "11111111-1111-1111-1111-111111111111",
            "dlc_b",
            0);
        connection.Execute(
            "INSERT INTO playsets_dlcs (playsetId, dlcId, enabled) VALUES (?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "dlc_b",
            0);
    }

    /// <summary>模拟老版本启动器的库：mods 表还没有 steamId 列。</summary>
    private static void SeedDatabaseWithoutSteamIdColumn(string databasePath)
    {
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), dirPath text, archivePath text, gameRegistryId text, thumbnailPath text, requiredVersion varchar(255), status text NOT NULL, source text NOT NULL)");
        connection.Execute(
            "INSERT INTO mods (id, displayName, dirPath, gameRegistryId, requiredVersion, status, source) VALUES (?, ?, ?, ?, ?, ?, ?)",
            "dddddddd-dddd-dddd-dddd-dddddddddddd",
            "Legacy Mod",
            "C:/fake/mods/Legacy Mod",
            "mod/ugc_987654.mod",
            "1.19.*",
            "ready_to_play",
            "steam");
    }

    private static void SeedDatabaseWithoutDlcTable(string databasePath)
    {
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE playsets (id char(36) PRIMARY KEY, name varchar(255) NOT NULL, isActive boolean, createdOn datetime NOT NULL)");
        connection.Execute(
            "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), dirPath text, archivePath text, gameRegistryId text, thumbnailPath text, requiredVersion varchar(255), status text NOT NULL)");
        connection.Execute(
            "CREATE TABLE playsets_mods (playsetId char(36), modId char(36), enabled boolean DEFAULT '1', position integer)");
        connection.Execute(
            "INSERT INTO playsets (id, name, isActive, createdOn) VALUES (?, ?, ?, ?)",
            "22222222-2222-2222-2222-222222222222",
            "Stellaris",
            1,
            1000L);
    }

    // ------------------------------------------------------------------
    // DlcCatalog（夹具）
    // ------------------------------------------------------------------

    private static void CheckDlcCatalogOnFixtures(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[PlaysetChecks] DlcCatalog（夹具）");

        var tempRoot = CreateTempRoot(fs, "dlc");
        try
        {
            var catalog = new DlcCatalog(fs, logger);

            // --- V0：Clausewitz .dlc（CK3 夹具）---
            var v0Root = Path.Combine(tempRoot, "v0");
            var v0DlcDir = Path.Combine(v0Root, "dlc", "dlc001_preorder");
            fs.EnsureDirectory(v0DlcDir);
            var ck3Fixture = ChecksContext.FixturePath("ck3/dlc001.dlc");
            var v0Descriptor = Path.Combine(v0DlcDir, "dlc001.dlc");
            fs.WriteAllBytesAtomic(v0Descriptor, fs.ReadAllBytes(ck3Fixture));
            fs.WriteAllBytesAtomic(Path.Combine(v0DlcDir, "thumbnail.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A });

            var v0Game = CreateGame(
                Ck3GameId,
                v0Root,
                LoadFileFormat.DlcLoadV0,
                Path.Combine(tempRoot, "data-v0"),
                v0Root,
                Path.Combine(tempRoot, "data-v0", "mod"),
                1158310);

            var v0 = catalog.DiscoverAsync(v0Game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("V0 解析 .dlc 数量", 1, v0.Count);
            if (v0.Count == 1)
            {
                ChecksContext.Equal("V0 DisplayName", "Garments of the Holy Roman Empire", v0[0].DisplayName);
                ChecksContext.Equal("V0 PopsId", "ck3_dlc001_preorder", v0[0].PopsId);
                ChecksContext.Equal("V0 SteamId（带引号字符串）", "1296730", v0[0].SteamId);
                ChecksContext.Equal(
                    "V0 FilePathRelativeToDlcPath（POSIX 分隔）",
                    "dlc/dlc001_preorder/dlc001.dlc",
                    v0[0].FilePathRelativeToDlcPath);
                ChecksContext.Check("V0 ThumbnailPath 指向同目录 thumbnail.png", v0[0].ThumbnailPath.Length > 0, v0[0].ThumbnailPath);
                ChecksContext.EqualBytes("V0 RawBytes 与描述文件一致", fs.ReadAllBytes(v0Descriptor), v0[0].RawBytes);
            }

            // --- V0：Stellaris 夹具（无引号数字 steam_id + archive 键）---
            var stellarisFixtureRoot = Path.Combine(tempRoot, "stellaris-fixture");
            var stellarisDlcDir = Path.Combine(stellarisFixtureRoot, "dlc", "dlc001_symbols_of_domination");
            fs.EnsureDirectory(stellarisDlcDir);
            var stellarisDescriptor = Path.Combine(stellarisDlcDir, "dlc001.dlc");
            fs.WriteAllBytesAtomic(stellarisDescriptor, fs.ReadAllBytes(ChecksContext.FixturePath("stellaris/dlc001.dlc")));
            var stellarisFixtureGame = CreateGame(
                StellarisGameId,
                stellarisFixtureRoot,
                LoadFileFormat.DlcLoadV0,
                Path.Combine(tempRoot, "data-stellaris-fixture"),
                stellarisFixtureRoot,
                Path.Combine(tempRoot, "data-stellaris-fixture", "mod"),
                281990);

            var stellaris = catalog.DiscoverAsync(stellarisFixtureGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("Stellaris 夹具解析 .dlc 数量", 1, stellaris.Count);
            if (stellaris.Count == 1)
            {
                ChecksContext.Equal("Stellaris 夹具 DisplayName", "Symbols of Domination", stellaris[0].DisplayName);
                ChecksContext.Equal("Stellaris 夹具 PopsId", "symbols_of_domination", stellaris[0].PopsId);
                ChecksContext.Equal("Stellaris 夹具 SteamId（无引号数字）", "447680", stellaris[0].SteamId);
                ChecksContext.Check(
                    "Stellaris 夹具无同目录 thumbnail.png 时 ThumbnailPath 为空串",
                    stellaris[0].ThumbnailPath.Length == 0);
            }

            // --- V1_1：JSON 描述文件（Victoria 3 夹具）---
            var v11Root = Path.Combine(tempRoot, "v11");
            var v11DlcDir = Path.Combine(v11Root, "dlc", "dlc001_preorder");
            fs.EnsureDirectory(v11DlcDir);
            var v11Descriptor = Path.Combine(v11DlcDir, "dlc001_preorder.dlc.json");
            fs.WriteAllBytesAtomic(v11Descriptor, fs.ReadAllBytes(ChecksContext.FixturePath("victoria3/dlc001_preorder.dlc.json")));
            fs.WriteAllBytesAtomic(Path.Combine(v11DlcDir, "thumbnail.png"), new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var v11Game = CreateGame(
                Victoria3GameId,
                v11Root,
                LoadFileFormat.ContentLoadV1_1,
                Path.Combine(tempRoot, "data-v11"),
                v11Root,
                Path.Combine(tempRoot, "data-v11", "mod"),
                529340);

            var v11 = catalog.DiscoverAsync(v11Game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("V1_1 解析 .dlc.json 数量", 1, v11.Count);
            if (v11.Count == 1)
            {
                ChecksContext.Equal("V1_1 DisplayName（displayName.en）", "Victoria II Remastered Soundtrack", v11[0].DisplayName);
                ChecksContext.Equal("V1_1 PopsId（id.paradox）", "dlc001_preorder", v11[0].PopsId);
                ChecksContext.Equal("V1_1 SteamId（id.steam 数字转字符串）", "2071470", v11[0].SteamId);
                ChecksContext.Equal(
                    "V1_1 FilePathRelativeToDlcPath（POSIX 分隔）",
                    "dlc/dlc001_preorder/dlc001_preorder.dlc.json",
                    v11[0].FilePathRelativeToDlcPath);
                ChecksContext.Check("V1_1 ThumbnailPath 指向同目录 thumbnail.png", v11[0].ThumbnailPath.Length > 0, v11[0].ThumbnailPath);
                ChecksContext.EqualBytes("V1_1 RawBytes 与描述文件一致", fs.ReadAllBytes(v11Descriptor), v11[0].RawBytes);
            }

            // --- V1_1 回退：目录里只有 .dlc 时回退扫 Clausewitz ---
            var fallbackRoot = Path.Combine(tempRoot, "v11-fallback");
            var fallbackDlcDir = Path.Combine(fallbackRoot, "dlc", "dlc001_preorder");
            fs.EnsureDirectory(fallbackDlcDir);
            fs.WriteAllBytesAtomic(Path.Combine(fallbackDlcDir, "dlc001.dlc"), fs.ReadAllBytes(ck3Fixture));
            var fallbackGame = CreateGame(
                Victoria3GameId,
                fallbackRoot,
                LoadFileFormat.ContentLoadV1_1,
                Path.Combine(tempRoot, "data-fallback"),
                fallbackRoot,
                Path.Combine(tempRoot, "data-fallback", "mod"),
                529340);

            var fallback = catalog.DiscoverAsync(fallbackGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("V1_1 无 .dlc.json 时回退扫 .dlc", 1, fallback.Count);
            if (fallback.Count == 1)
            {
                ChecksContext.Equal("回退结果 PopsId", "ck3_dlc001_preorder", fallback[0].PopsId);
            }

            // --- 容错：坏描述文件跳过、不抛异常 ---
            var brokenRoot = Path.Combine(tempRoot, "broken");
            var brokenDir = Path.Combine(brokenRoot, "dlc", "broken");
            fs.EnsureDirectory(brokenDir);
            // 真正坏的 .dlc：连 name 都没有 -> 必须跳过。
            fs.WriteAllTextAtomic(Path.Combine(brokenDir, "broken.dlc"), "steam_id = \"1\"\n");
            fs.WriteAllTextAtomic(Path.Combine(brokenDir, "broken.dlc.json"), "{ 这不是合法 JSON ");
            var brokenGame = CreateGame(
                Victoria3GameId,
                brokenRoot,
                LoadFileFormat.ContentLoadV1_1,
                Path.Combine(tempRoot, "data-broken"),
                brokenRoot,
                Path.Combine(tempRoot, "data-broken", "mod"),
                529340);

            var brokenThrew = false;
            IReadOnlyList<DlcInfo> broken = Array.Empty<DlcInfo>();
            try
            {
                broken = catalog.DiscoverAsync(brokenGame, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                brokenThrew = true;
            }

            ChecksContext.Check("坏描述文件不抛异常", !brokenThrew);
            ChecksContext.Equal("坏描述文件全部跳过", 0, broken.Count);

            // --- pops_id 为空是合法的（官方 getDlcId 退化为 filePath），不能被当成坏文件跳过 ---
            var emptyPopsRoot = Path.Combine(tempRoot, "empty-pops");
            var emptyPopsDir = Path.Combine(emptyPopsRoot, "dlc", "dlc030_paragon");
            fs.EnsureDirectory(emptyPopsDir);
            fs.WriteAllTextAtomic(
                Path.Combine(emptyPopsDir, "dlc030.dlc"),
                "name = \"Galactic Paragons\"\npops_id = \"\"\nsteam_id = \"2310100\"\n");
            var emptyPopsGame = CreateGame(
                Ck3GameId,
                emptyPopsRoot,
                LoadFileFormat.DlcLoadV0,
                Path.Combine(tempRoot, "data-empty-pops"),
                emptyPopsRoot,
                Path.Combine(tempRoot, "data-empty-pops", "mod"),
                1158310);

            var emptyPopsResult = catalog.DiscoverAsync(emptyPopsGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("pops_id 为空的 .dlc 仍被收录（空 pops_id 合法）", 1, emptyPopsResult.Count);
            if (emptyPopsResult.Count == 1)
            {
                ChecksContext.Equal("空 pops_id 原样保留空串", string.Empty, emptyPopsResult[0].PopsId);
                ChecksContext.Equal("空 pops_id 的 DLC DisplayName 正常", "Galactic Paragons", emptyPopsResult[0].DisplayName);
                ChecksContext.Equal("空 pops_id 的 DLC SteamId 正常", "2310100", emptyPopsResult[0].SteamId);
                ChecksContext.Equal(
                    "空 pops_id 的 DLC 相对路径可当 id 兜底",
                    "dlc/dlc030_paragon/dlc030.dlc",
                    emptyPopsResult[0].FilePathRelativeToDlcPath.Replace('\\', '/'));
            }

            // --- 目录不存在时返回空 ---
            var missingGame = CreateGame(
                Ck3GameId,
                Path.Combine(tempRoot, "no-install"),
                LoadFileFormat.DlcLoadV0,
                Path.Combine(tempRoot, "data-missing"),
                Path.Combine(tempRoot, "no-such-dlc-dir"),
                Path.Combine(tempRoot, "data-missing", "mod"),
                1158310);
            var missing = catalog.DiscoverAsync(missingGame, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("DLC 目录不存在时返回空列表", 0, missing.Count);
        }
        finally
        {
            TryDelete(fs, tempRoot);
        }
    }

    // ------------------------------------------------------------------
    // Mod 版本兼容判断
    // ------------------------------------------------------------------

    private static void CheckModVersion()
    {
        Console.WriteLine("[PlaysetChecks] Mod 版本兼容判断");

        ChecksContext.Check("解析 \"1.13.11 (Matcha)\" 为 1.13", ModVersion.TryReadMajorMinor("1.13.11 (Matcha)", out var maj1, out var min1) && maj1 == 1 && min1 == 13);
        ChecksContext.Check("解析 \"1.20.*\" 为 1.20", ModVersion.TryReadMajorMinor("1.20.*", out var maj2, out var min2) && maj2 == 1 && min2 == 20);
        ChecksContext.Check("解析 \"Cygmus v4.5.2\" 为 4.5", ModVersion.TryReadMajorMinor("Cygmus v4.5.2", out var maj3, out var min3) && maj3 == 4 && min3 == 5);
        ChecksContext.Check("没有数字时解析失败", !ModVersion.TryReadMajorMinor("abc", out _, out _));
        ChecksContext.Check("空串解析失败", !ModVersion.TryReadMajorMinor(null, out _, out _));

        ChecksContext.Check("requiredVersion 1.11 对游戏 1.13.11 判为过旧", ModVersion.IsOutdated("1.11", "1.13.11"));
        ChecksContext.Check("requiredVersion 1.20.* 对游戏 1.20.1.2 不过旧", !ModVersion.IsOutdated("1.20.*", "1.20.1.2"));
        ChecksContext.Check("major.minor 相同时不过旧", !ModVersion.IsOutdated("1.13", "1.13.11 (Matcha)"));
        ChecksContext.Check("requiredVersion 比游戏新时不算过旧", !ModVersion.IsOutdated("1.15", "1.13.11"));
        ChecksContext.Check("requiredVersion 为空时不算过旧", !ModVersion.IsOutdated(string.Empty, "1.13.11"));
        ChecksContext.Check("游戏版本为空时不算过旧", !ModVersion.IsOutdated("1.11", null));
    }

    // ------------------------------------------------------------------
    // 真机（只读）
    // ------------------------------------------------------------------

    private static bool IsSortedByName(IReadOnlyList<ModLibraryEntry> mods)
    {
        for (var index = 1; index < mods.Count; index++)
        {
            if (string.Compare(mods[index - 1].Name, mods[index].Name, StringComparison.OrdinalIgnoreCase) > 0)
            {
                return false;
            }
        }

        return true;
    }

    private static void CheckRealMachine(IFileSystem fs, IAppLogger logger, IClock clock)
    {
        Console.WriteLine("[PlaysetChecks] 真机数据（只读打开，绝不写入）");

        IReadOnlyList<GameInstallation> installations;
        try
        {
            installations = new GameCatalog(fs, logger).Discover(CancellationToken.None);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("  skip GameCatalog 发现失败，跳过真机断言（不记为失败）：" + exception.Message);
            return;
        }

        RunRealMachineSection("CK3", () => CheckRealCk3(fs, logger, clock, installations));
        RunRealMachineSection("Victoria 3", () => CheckRealVictoria3(fs, logger, clock, installations));
        RunRealMachineSection("Stellaris", () => CheckRealStellaris(fs, logger, clock, installations));
    }

    /// <summary>
    /// 真机段落的外层兜底：官方启动器或游戏本体正开着时，launcher-v2.sqlite 会被独占，
    /// 这类读取失败不该把离线自检判成红，按 skip 处理（不记为失败）。
    /// </summary>
    private static void RunRealMachineSection(string name, Action section)
    {
        try
        {
            section();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine("  skip 真机 " + name + " 数据库被占用或读不到，跳过（不记为失败）：" + exception.Message);
        }
    }

    private static void CheckRealCk3(
        IFileSystem fs,
        IAppLogger logger,
        IClock clock,
        IReadOnlyList<GameInstallation> installations)
    {
        Console.WriteLine("[PlaysetChecks] 真机 CK3");

        var game = FindGame(installations, Ck3GameId);
        if (game is null)
        {
            Console.WriteLine("  skip 本机未发现 CK3 安装，跳过（不记为失败）");
            return;
        }

        var dbPath = PlaySetRepository.GetDatabasePath(game);
        if (!fs.FileExists(dbPath))
        {
            Console.WriteLine("  skip 未找到 launcher-v2.sqlite：" + dbPath);
            return;
        }

        var md5Before = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        var repository = new PlaySetRepository(fs, logger, clock);

        var playsets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        // 真机断言只锁结构、不锁具体数据：launcher-v2.sqlite 由官方启动器随时改写（增删播放集、切换激活项），
        // 把名称 / Id / 行数写死必然随用户操作漂移（本机就从「激活 IronyModManager」漂成了「激活 1.20」）。
        ChecksContext.Check(
            "CK3 至少有 3 个播放集",
            playsets.Count >= 3,
            playsets.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            "CK3 播放集名称全部非空",
            playsets.All(playSet => !string.IsNullOrWhiteSpace(playSet.Name)));
        ChecksContext.Check(
            "CK3 播放集 Id 唯一",
            playsets.Select(playSet => playSet.Id).Distinct().Count() == playsets.Count);
        ChecksContext.Check(
            "CK3 至多 1 个播放集处于激活状态",
            playsets.Count(playSet => playSet.IsActive) <= 1);

        var active = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("CK3 激活播放集存在", active is not null);
        if (active is not null)
        {
            ChecksContext.Check(
                "CK3 激活播放集与列表里的激活项一致",
                playsets.Any(playSet => playSet.Id == active.Id && playSet.IsActive));
            ChecksContext.Check("CK3 激活播放集名称非空", !string.IsNullOrWhiteSpace(active.Name));

            var activeMods = repository.GetPlaySetModsAsync(game, active.Id, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check(
                "CK3 激活播放集 Position 连续 0..n-1",
                activeMods.Select((mod, index) => mod.Position == index).All(flag => flag));
            ChecksContext.Check(
                "CK3 激活播放集 Status 全为 ready_to_play",
                activeMods.All(mod => mod.Status == "ready_to_play"));
            ChecksContext.Check(
                "CK3 激活播放集 GameRegistryId 全为 mod/<name>.mod",
                activeMods.All(mod => mod.GameRegistryId.StartsWith("mod/", StringComparison.Ordinal) && mod.GameRegistryId.EndsWith(".mod", StringComparison.Ordinal)));
        }

        // 内容层面的深断言挂在「按名字找到的播放集」上：名字还在就照旧校验，名字没了只跳过、不失败。
        var irony = playsets.FirstOrDefault(playSet => string.Equals(playSet.Name, "IronyModManager", StringComparison.Ordinal));
        if (irony is null)
        {
            Console.WriteLine("  info 本机已没有名为 IronyModManager 的 CK3 播放集，跳过其内容断言");
        }
        else
        {
            var mods = repository.GetPlaySetModsAsync(game, irony.Id, CancellationToken.None).GetAwaiter().GetResult();
            var ironyEnabled = mods.Count(mod => mod.Enabled);
            Console.WriteLine(
                "  info CK3 IronyModManager mod 行数 " + mods.Count.ToString(CultureInfo.InvariantCulture)
                + "，启用 " + ironyEnabled.ToString(CultureInfo.InvariantCulture)
                + " / 未启用 " + (mods.Count - ironyEnabled).ToString(CultureInfo.InvariantCulture) + "（用户在官方启动器可随时改，只锁结构与解码）");
            ChecksContext.Check("CK3 IronyModManager 至少 1 行 mod", mods.Count >= 1, mods.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("CK3 IronyModManager 至少 1 行启用", ironyEnabled >= 1, ironyEnabled.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check(
                "CK3 IronyModManager 名称全部非空且至少 1 行为非 ASCII（验证 UTF-8 解码）",
                mods.All(mod => !string.IsNullOrWhiteSpace(mod.Name)) && mods.Any(mod => mod.Name.Any(ch => ch > 127)));
            ChecksContext.Check("CK3 IronyModManager 存在「甜蜜约会」行（验证中文解码）", mods.Any(mod => mod.Name == "甜蜜约会"));
            ChecksContext.Check("CK3 IronyModManager ArchivePath 全为 null", mods.All(mod => mod.ArchivePath is null));
            ChecksContext.Check("CK3 IronyModManager RequiredVersion 全为 1.20.*", mods.All(mod => mod.RequiredVersion == "1.20.*"));
            ChecksContext.Check(
                "CK3 IronyModManager 缩略图全部解析到 dirPath/thumbnail.png",
                mods.All(mod => !string.IsNullOrEmpty(mod.ThumbnailPath)),
                string.Join(", ", mods.Select(mod => mod.Name + "=" + (mod.ThumbnailPath ?? "<null>"))));

            var dlcs = repository.GetPlaySetDlcsAsync(game, irony.Id, CancellationToken.None).GetAwaiter().GetResult();
            // 行数取决于官方 dlc_load.json，用户开关 DLC 就会变：只锁 id 的形状。
            Console.WriteLine("  info CK3 IronyModManager DLC 行数 " + dlcs.Count.ToString(CultureInfo.InvariantCulture) + "（随官方 dlc_load.json 变动）");
            ChecksContext.Check("CK3 IronyModManager DLC id 全部非空", dlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.DlcId)));
            ChecksContext.Check("CK3 IronyModManager DLC id 唯一", dlcs.Select(dlc => dlc.DlcId).Distinct().Count() == dlcs.Count);
        }

        var older = playsets.FirstOrDefault(playSet => string.Equals(playSet.Name, "1.18", StringComparison.Ordinal));
        if (older is null)
        {
            Console.WriteLine("  info 本机已没有名为 1.18 的 CK3 播放集，跳过其 DLC 断言");
        }
        else
        {
            var olderDlcs = repository.GetPlaySetDlcsAsync(game, older.Id, CancellationToken.None).GetAwaiter().GetResult();
            // 行数随官方新增 DLC 增长（本机 22 → 25），启用态也会被官方改写：只锁下限与「至少一个可用」。
            ChecksContext.Check(
                "CK3 播放集 1.18 的 DLC 行数 >= 22",
                olderDlcs.Count >= 22,
                olderDlcs.Count.ToString(CultureInfo.InvariantCulture));
            var olderEnabled = olderDlcs.Count(dlc => dlc.Enabled);
            Console.WriteLine(
                "  info CK3 播放集 1.18 的 DLC 启用 " + olderEnabled.ToString(CultureInfo.InvariantCulture)
                + "/" + olderDlcs.Count.ToString(CultureInfo.InvariantCulture) + "（官方可改写启用态，不锁具体值）");
            ChecksContext.Check("CK3 播放集 1.18 至少 1 个 DLC 启用", olderEnabled >= 1, olderEnabled.ToString(CultureInfo.InvariantCulture));
        }

        var catalog = new DlcCatalog(fs, logger);
        var dlcsAll = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  info CK3 递归 *.dlc 数量 " + dlcsAll.Count.ToString(CultureInfo.InvariantCulture) + "（官方持续新增 DLC，只锁下限）");
        ChecksContext.Check("CK3 递归 *.dlc 数量 >= 25", dlcsAll.Count >= 25, dlcsAll.Count.ToString(CultureInfo.InvariantCulture));
        CheckDlcShape("CK3", dlcsAll);
        CheckDlcAnchor(
            "CK3",
            dlcsAll,
            "ck3_dlc001_preorder",
            "Garments of the Holy Roman Empire",
            "1296730",
            "dlc/dlc001_preorder/dlc001.dlc");

        var modCatalog = new ModCatalog(fs, logger);
        var allMods = modCatalog.DiscoverAllAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        var ck3ModFiles = fs.EnumerateFiles(game.ModDir, "*.mod", recursive: false).Count();
        ChecksContext.Equal("CK3 合并后 mod 总数（磁盘 .mod 数 = 合并结果）", ck3ModFiles, allMods.Count);
        CheckModShape("CK3", allMods);
        ChecksContext.Check(
            "CK3 全部 mod 的 GameRegistryId 形如 mod/*.mod",
            allMods.All(mod => mod.GameRegistryId.StartsWith("mod/", StringComparison.Ordinal) && mod.GameRegistryId.EndsWith(".mod", StringComparison.Ordinal)));
        var ck3Thumbs = allMods.Count(mod => !string.IsNullOrEmpty(mod.ThumbnailPath));
        Console.WriteLine("  info CK3 mod 缩略图可解析 " + ck3Thumbs.ToString(CultureInfo.InvariantCulture) + "/" + allMods.Count.ToString(CultureInfo.InvariantCulture) + "（实测 373）");
        ChecksContext.Check("CK3 mod 缩略图可解析数 >= 350", ck3Thumbs >= 350, ck3Thumbs.ToString(CultureInfo.InvariantCulture));

        // Steam 创意工坊 id：.mod 的 remote_file_id / metadata.json 的 id / mods.steamId 任一来源拿到即可。
        var ck3Workshop = allMods
            .Select(mod => (Mod: mod, WorkshopId: SteamWorkshop.GetWorkshopId(mod.SteamId, mod.GameRegistryId)))
            .ToList();
        var ck3WorkshopCount = ck3Workshop.Count(item => item.WorkshopId is not null);
        Console.WriteLine("  info CK3 mod 可解析创意工坊 id " + ck3WorkshopCount.ToString(CultureInfo.InvariantCulture) + "/" + allMods.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("CK3 mod 可解析创意工坊 id 数 >= 350", ck3WorkshopCount >= 350, ck3WorkshopCount.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            "CK3 mod 创意工坊 id 全为纯数字",
            ck3Workshop.All(item => item.WorkshopId is null || item.WorkshopId.All(character => character is >= '0' and <= '9')));

        var md5After = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        ChecksContext.Equal("CK3 只读读取未修改 launcher-v2.sqlite", md5Before, md5After);
    }

    private static void CheckRealVictoria3(
        IFileSystem fs,
        IAppLogger logger,
        IClock clock,
        IReadOnlyList<GameInstallation> installations)
    {
        Console.WriteLine("[PlaysetChecks] 真机 Victoria 3");

        var game = FindGame(installations, Victoria3GameId);
        if (game is null)
        {
            Console.WriteLine("  skip 本机未发现 Victoria 3 安装，跳过（不记为失败）");
            return;
        }

        var dbPath = PlaySetRepository.GetDatabasePath(game);
        if (!fs.FileExists(dbPath))
        {
            Console.WriteLine("  skip 未找到 launcher-v2.sqlite：" + dbPath);
            return;
        }

        var md5Before = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        var repository = new PlaySetRepository(fs, logger, clock);

        // 与 CK3 段同一原则：真机断言只锁结构，不锁本机数据。launcher-v2.sqlite 由官方启动器
        // 随时改写（本机 2026-10-29 实测已变成「1 个播放集 / 24 行 mod / 仅 1 行启用」），
        // 把数量、名称、启用态写死只会随用户操作漂移，红的是断言而不是代码。
        var playsets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("V3 至少有 1 个播放集", playsets.Count >= 1, playsets.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("V3 播放集名称全部非空", playsets.All(playSet => !string.IsNullOrWhiteSpace(playSet.Name)));
        ChecksContext.Check("V3 播放集 Id 唯一", playsets.Select(playSet => playSet.Id).Distinct().Count() == playsets.Count);
        ChecksContext.Check("V3 至多 1 个播放集处于激活状态", playsets.Count(playSet => playSet.IsActive) <= 1);

        var active = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("V3 存在激活播放集", active is not null);
        if (active is not null)
        {
            ChecksContext.Check("V3 激活播放集与列表里的激活项一致", playsets.Any(playSet => playSet.Id == active.Id && playSet.IsActive));
            ChecksContext.Check("V3 激活播放集名称非空", !string.IsNullOrWhiteSpace(active.Name));
            var mods = repository.GetPlaySetModsAsync(game, active.Id, CancellationToken.None).GetAwaiter().GetResult();
            var enabledMods = mods.Count(mod => mod.Enabled);
            Console.WriteLine(
                "  info V3 激活播放集 mod 行数 " + mods.Count.ToString(CultureInfo.InvariantCulture)
                + "，启用 " + enabledMods.ToString(CultureInfo.InvariantCulture)
                + " / 未启用 " + (mods.Count - enabledMods).ToString(CultureInfo.InvariantCulture)
                + "（官方启动器随时改写启用集，行数只是本机数据，不作断言）");
            // 行数下限也不能锁：用户在官方启动器里删 mod、换播放集，行数就变（本机 2026-11-04 只剩 7 行）。
            // 能锁的是结构——Position 连续、名称非空、Status 合法、启用态与 Mod 库归属一致。
            // 官方启动器会把 mod 整批关掉（本机 2026-10-29 就只剩 1 行启用），「全部启用」不是不变量；
            // 能锁的是「至少还有一行启用」——全关意味着我方与官方两侧都读不到可启动内容。
            ChecksContext.Check("V3 激活播放集至少 1 行启用", enabledMods >= 1, enabledMods.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("V3 激活播放集 Position 连续 0..n-1", mods.Select((mod, index) => mod.Position == index).All(flag => flag));
            ChecksContext.Check("V3 激活播放集名称全部非空", mods.All(mod => !string.IsNullOrWhiteSpace(mod.Name)));
            ChecksContext.Check("V3 激活播放集 Status 全为 ready_to_play", mods.All(mod => mod.Status == "ready_to_play"));

            // --- Mod 库：mods 表全部行 + 播放集归属（只读）---
            var library = repository.GetModLibraryAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("  info V3 Mod 库行数 " + library.Count.ToString(CultureInfo.InvariantCulture) + "（mods 表全量，行数随订阅变动）");
            ChecksContext.Check("V3 Mod 库行数 >= 60", library.Count >= 60, library.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("V3 Mod 库名称全部非空", library.All(entry => !string.IsNullOrWhiteSpace(entry.Name)));
            ChecksContext.Check("V3 Mod 库按名称升序（OrdinalIgnoreCase）", IsSortedByName(library));
            ChecksContext.Check("V3 Mod 库 ModId 全部非空", library.All(entry => entry.ModId != Guid.Empty));
            var withSize = library.Count(entry => entry.SizeBytes is > 0);
            Console.WriteLine("  info V3 Mod 库有 size 的行 " + withSize.ToString(CultureInfo.InvariantCulture) + "/" + library.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("V3 Mod 库绝大多数行有 size", withSize >= library.Count - 5, withSize.ToString(CultureInfo.InvariantCulture));
            var withSubscribed = library.Count(entry => entry.SubscribedOn is not null);
            Console.WriteLine("  info V3 Mod 库可解析订阅时间的行 " + withSubscribed.ToString(CultureInfo.InvariantCulture) + "/" + library.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check(
                "V3 Mod 库绝大多数行能解析订阅时间",
                withSubscribed >= library.Count - 5,
                withSubscribed.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check(
                "V3 Mod 库订阅时间落在合理区间（2020-2035）",
                library.Where(entry => entry.SubscribedOn is not null)
                    .All(entry => entry.SubscribedOn!.Value.Year is >= 2020 and <= 2035));
            ChecksContext.Check(
                "V3 Mod 库归属行数 = 激活播放集 mod 行数",
                library.Count(entry => entry.IsInPlaySet) == mods.Count,
                library.Count(entry => entry.IsInPlaySet).ToString(CultureInfo.InvariantCulture) + " vs " + mods.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check(
                "V3 Mod 库归属全部指向唯一播放集",
                library.Where(entry => entry.IsInPlaySet).All(entry => entry.PlaySetIds.Count == 1 && entry.PlaySetIds[0] == active.Id));
            ChecksContext.Check("V3 Mod 库非归属行 PlaySetIds 为空", library.Where(entry => !entry.IsInPlaySet).All(entry => entry.PlaySetIds.Count == 0));

            var dlcs = repository.GetPlaySetDlcsAsync(game, active.Id, CancellationToken.None).GetAwaiter().GetResult();
            Console.WriteLine("  info V3 激活播放集 DLC 行数 " + dlcs.Count.ToString(CultureInfo.InvariantCulture) + "（官方侧可能开始写 DLC 开关，不锁具体行数）");
            ChecksContext.Check("V3 激活播放集 DLC id 全部非空", dlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.DlcId)));
            ChecksContext.Check("V3 激活播放集 DLC id 唯一", dlcs.Select(dlc => dlc.DlcId).Distinct().Count() == dlcs.Count);
        }

        var catalog = new DlcCatalog(fs, logger);
        var dlcsAll = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        var diskDlcs = dlcsAll.Where(IsDiskDescriptor).ToList();
        var metadataDlcs = dlcsAll.Where(dlc => !IsDiskDescriptor(dlc)).ToList();
        Console.WriteLine("  info V3 DLC 条目数 " + dlcsAll.Count.ToString(CultureInfo.InvariantCulture)
            + "（磁盘描述文件 " + diskDlcs.Count.ToString(CultureInfo.InvariantCulture)
            + " + dlc_metadata " + metadataDlcs.Count.ToString(CultureInfo.InvariantCulture) + "；官方持续新增 DLC，只锁下限）");
        Console.WriteLine("  info V3 dlc_metadata 合成条目 "
            + string.Join(" / ", metadataDlcs.Select(dlc => dlc.DisplayName + "（steam_id " + dlc.SteamId + "）")));
        ChecksContext.Check("V3 磁盘描述文件条目 >= 17", diskDlcs.Count >= 17, diskDlcs.Count.ToString(CultureInfo.InvariantCulture));
        // Victoria 3 1.15 起新 DLC 只写 game/dlc_metadata/00_dlc_metadata.txt（不再补 .dlc.json）；
        // 真机 1.15：17 个磁盘描述文件 + State and Revolution = 18。
        ChecksContext.Check("V3 DLC 条目数 >= 18（含 dlc_metadata 增量）", dlcsAll.Count >= 18, dlcsAll.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            "V3 dlc_metadata 合成条目含 State and Revolution / 4314660",
            metadataDlcs.Any(dlc => dlc.SteamId == "4314660" && dlc.DisplayName == "State and Revolution"));
        ChecksContext.Check(
            "V3 dlc_metadata 合成条目相对路径形如 dlc_metadata/<块名>",
            metadataDlcs.All(dlc => dlc.FilePathRelativeToDlcPath.StartsWith("dlc_metadata/", StringComparison.Ordinal)
                && dlc.FilePathRelativeToDlcPath.Count(ch => ch == '/') == 1));
        CheckDlcShape("V3", dlcsAll);
        CheckDlcAnchor(
            "V3",
            dlcsAll,
            "dlc001_preorder",
            "Victoria II Remastered Soundtrack",
            "2071470",
            "dlc/dlc001_preorder/dlc001_preorder.dlc.json");

        var modCatalog = new ModCatalog(fs, logger);
        var allMods = modCatalog.DiscoverAllAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  info V3 合并后 mod 总数 " + allMods.Count.ToString(CultureInfo.InvariantCulture) + "（workshop + 本地合并，数量随订阅变动）");
        ChecksContext.Check("V3 合并后 mod 总数 >= 60", allMods.Count >= 60, allMods.Count.ToString(CultureInfo.InvariantCulture));
        CheckModShape("V3", allMods);
        ChecksContext.Check("V3 全部 mod 的 GameRegistryId 为空串（只有 metadata.json）", allMods.All(mod => mod.GameRegistryId.Length == 0));
        var v3Thumbs = allMods.Count(mod => !string.IsNullOrEmpty(mod.ThumbnailPath));
        Console.WriteLine("  info V3 mod 缩略图可解析 " + v3Thumbs.ToString(CultureInfo.InvariantCulture) + "/" + allMods.Count.ToString(CultureInfo.InvariantCulture) + "（随 workshop 下载状态变动）");
        ChecksContext.Check("V3 mod 缩略图可解析数 >= 55", v3Thumbs >= 55, v3Thumbs.ToString(CultureInfo.InvariantCulture));

        var md5After = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        ChecksContext.Equal("V3 只读读取未修改 launcher-v2.sqlite", md5Before, md5After);
    }

    private static void CheckRealStellaris(
        IFileSystem fs,
        IAppLogger logger,
        IClock clock,
        IReadOnlyList<GameInstallation> installations)
    {
        Console.WriteLine("[PlaysetChecks] 真机 Stellaris");

        var game = FindGame(installations, StellarisGameId);
        if (game is null)
        {
            Console.WriteLine("  skip 本机未发现 Stellaris 安装，跳过（不记为失败）");
            return;
        }

        var dbPath = PlaySetRepository.GetDatabasePath(game);
        if (!fs.FileExists(dbPath))
        {
            Console.WriteLine("  skip 未找到 launcher-v2.sqlite：" + dbPath);
            return;
        }

        var md5Before = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        var repository = new PlaySetRepository(fs, logger, clock);

        var playsets = repository.GetPlaySetsAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        // 播放集由官方启动器随时增删改；这里只锁结构与「首个播放集仍按 createdOn 排序在首位」。
        ChecksContext.Check("Stellaris 至少有 3 个播放集", playsets.Count >= 3, playsets.Count.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check("Stellaris 至多 1 个播放集处于激活状态", playsets.Count(item => item.IsActive) <= 1);
        ChecksContext.Check("Stellaris 播放集名称全部非空", playsets.All(item => !string.IsNullOrWhiteSpace(item.Name)));
        ChecksContext.Equal("Stellaris 第 1 个播放集", "Initial playset", playsets[0].Name);
        if (playsets.Count >= 5)
        {
            ChecksContext.Equal("Stellaris 第 5 个播放集（IronyModManager）", "IronyModManager", playsets[4].Name);
        }

        var active = repository.GetActivePlaySetAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        ChecksContext.Check("Stellaris 存在激活播放集", active is not null);
        if (active is not null)
        {
            ChecksContext.Check("Stellaris 激活播放集与列表里的激活项一致", playsets.Any(item => item.Id == active.Id && item.IsActive));

            // 记录中的激活集是 IronyModManager（id 853346c4-…）；官方启动器可随时切换激活集，
            // 切换后只保留结构断言，锚点值降级为 info，不再把用户操作记成失败。
            var isIrony = string.Equals(active.Name, "IronyModManager", StringComparison.Ordinal);
            if (isIrony)
            {
                ChecksContext.Equal("Stellaris 激活播放集 Id", Guid.Parse("853346c4-20df-4f25-8ded-77b2cdf06153"), active.Id);
            }
            else
            {
                Console.WriteLine("  info Stellaris 激活播放集已改为「" + active.Name + "」，跳过 IronyModManager 专属锚点");
            }

            var mods = repository.GetPlaySetModsAsync(game, active.Id, CancellationToken.None).GetAwaiter().GetResult();
            var enabledStellaris = mods.Count(mod => mod.Enabled);
            Console.WriteLine(
                "  info Stellaris 激活播放集 mod 行数 " + mods.Count.ToString(CultureInfo.InvariantCulture)
                + "，启用 " + enabledStellaris.ToString(CultureInfo.InvariantCulture)
                + "/" + mods.Count.ToString(CultureInfo.InvariantCulture) + "（官方启动器可改写，只锁结构与下限）");
            ChecksContext.Check("Stellaris 激活播放集 mod 行数 >= 11", mods.Count >= 11, mods.Count.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("Stellaris 激活播放集至少 1 行启用", enabledStellaris >= 1, enabledStellaris.ToString(CultureInfo.InvariantCulture));
            ChecksContext.Check("Stellaris 激活播放集 Position 连续 0..n-1", mods.Select((mod, index) => mod.Position == index).All(flag => flag));
            ChecksContext.Check(
                "Stellaris 激活播放集 GameRegistryId 全为 mod/*.mod（12 个 ugc_ + 1 个本地 District 中文 mod）",
                mods.All(mod => mod.GameRegistryId.StartsWith("mod/", StringComparison.Ordinal) && mod.GameRegistryId.EndsWith(".mod", StringComparison.Ordinal)));
            var ugcMods = mods.Count(mod => mod.GameRegistryId.StartsWith("mod/ugc_", StringComparison.Ordinal));
            Console.WriteLine("  info Stellaris 激活播放集 ugc_ 前缀 " + ugcMods.ToString(CultureInfo.InvariantCulture) + "/" + mods.Count.ToString(CultureInfo.InvariantCulture) + "（实测 12，另 1 个是本地中文 mod）");

            // 上一代 P 社游戏的 sqlite dirPath 指向已搬迁的旧盘，必须靠 Steam 库重定位找回缩略图。
            var playsetThumbs = mods.Count(mod => !string.IsNullOrEmpty(mod.ThumbnailPath));
            Console.WriteLine("  info Stellaris 激活播放集缩略图可解析 " + playsetThumbs.ToString(CultureInfo.InvariantCulture) + "/" + mods.Count.ToString(CultureInfo.InvariantCulture) + "（实测 12：BPVR - City 96 Slot 的工坊目录里没有任何图片）");
            ChecksContext.Check("Stellaris 激活播放集缩略图可解析数 >= 11", playsetThumbs >= 11, playsetThumbs.ToString(CultureInfo.InvariantCulture));

            // Stellaris 库里根本没有 playsets_dlcs 表：必须返回空而不是抛 no such table。
            var dlcsThrew = false;
            IReadOnlyList<PlaySetDlc> dlcs = Array.Empty<PlaySetDlc>();
            try
            {
                dlcs = repository.GetPlaySetDlcsAsync(game, active.Id, CancellationToken.None).GetAwaiter().GetResult();
            }
            catch (SqliteException)
            {
                dlcsThrew = true;
            }

            ChecksContext.Check("Stellaris 没有 playsets_dlcs 表时不抛异常", !dlcsThrew);
            ChecksContext.Equal("Stellaris 无 playsets_dlcs 表时 DLC 列表为空", 0, dlcs.Count);
        }

        var catalog = new DlcCatalog(fs, logger);
        var dlcsAll = catalog.DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  info Stellaris 递归 *.dlc 数量 " + dlcsAll.Count.ToString(CultureInfo.InvariantCulture) + "（官方持续新增 DLC，只锁下限）");
        ChecksContext.Check("Stellaris 递归 *.dlc 数量 >= 31", dlcsAll.Count >= 31, dlcsAll.Count.ToString(CultureInfo.InvariantCulture));
        // 真机：31 个 .dlc 里有 4 个 pops_id 是空字符串（合法，官方 getDlcId 会退化为 filePath）。
        CheckDlcShape("Stellaris", dlcsAll, requireUniquePopsId: false, expectedEmptyPopsId: 4);
        var emptyPopsDlcs = dlcsAll.Where(dlc => string.IsNullOrWhiteSpace(dlc.PopsId)).ToList();
        // 只要求记录中的 4 个仍在集合里（用 filePath 当 id 兜底），官方新增空 pops_id 的 DLC 不记为失败。
        var emptyPopsFiles = emptyPopsDlcs.Select(dlc => dlc.FilePathRelativeToDlcPath.Replace('\\', '/')).ToHashSet(StringComparer.Ordinal);
        ChecksContext.Check(
            "Stellaris pops_id 为空的 DLC 的 FilePathRelativeToDlcPath 与真机记录一致（记录中的 4 个都还在）",
            new[]
            {
                "dlc/dlc030_paragon/dlc030.dlc",
                "dlc/dlc031_astral_planes/dlc031.dlc",
                "dlc/dlc035_rick_the_cube/dlc035.dlc",
                "dlc/dlc039_stargazer/dlc039.dlc",
            }.All(expected => emptyPopsFiles.Contains(expected)),
            string.Join(", ", emptyPopsDlcs.Select(dlc => dlc.FilePathRelativeToDlcPath)));
        ChecksContext.Check(
            "Stellaris pops_id 为空的 DLC 的 DisplayName 全部非空（Galactic Paragons / Astral Planes / Rick The Cube / Stargazer）",
            emptyPopsDlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.DisplayName)));
        ChecksContext.Check(
            "Stellaris pops_id 为空的 DLC 的 SteamId 全部非空",
            emptyPopsDlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.SteamId)));
        CheckDlcAnchor(
            "Stellaris",
            dlcsAll,
            "symbols_of_domination",
            "Symbols of Domination",
            "447680",
            "dlc/dlc001_symbols_of_domination/dlc001.dlc");

        var modCatalog = new ModCatalog(fs, logger);
        var allMods = modCatalog.DiscoverAllAsync(game, CancellationToken.None).GetAwaiter().GetResult();
        Console.WriteLine("  info Stellaris 合并后 mod 总数 " + allMods.Count.ToString(CultureInfo.InvariantCulture) + "（磁盘 .mod 与 sqlite 合并，数量随订阅变动）");
        ChecksContext.Check("Stellaris 合并后 mod 总数 >= 250", allMods.Count >= 250, allMods.Count.ToString(CultureInfo.InvariantCulture));
        CheckModShape("Stellaris", allMods);
        ChecksContext.Check(
            "Stellaris 全部 mod 的 GameRegistryId 形如 mod/*.mod",
            allMods.All(mod => mod.GameRegistryId.StartsWith("mod/", StringComparison.Ordinal) && mod.GameRegistryId.EndsWith(".mod", StringComparison.Ordinal)));
        var stellarisThumbs = allMods.Count(mod => !string.IsNullOrEmpty(mod.ThumbnailPath));
        Console.WriteLine("  info Stellaris mod 缩略图可解析 " + stellarisThumbs.ToString(CultureInfo.InvariantCulture) + "/" + allMods.Count.ToString(CultureInfo.InvariantCulture) + "（修好库重定位后实测 266）");
        ChecksContext.Check("Stellaris mod 缩略图可解析数 >= 250", stellarisThumbs >= 250, stellarisThumbs.ToString(CultureInfo.InvariantCulture));

        var md5After = ChecksContext.Md5(fs.ReadAllBytes(dbPath));
        ChecksContext.Equal("Stellaris 只读读取未修改 launcher-v2.sqlite", md5Before, md5After);
    }

    /// <summary>
    /// DLC 列表的形状断言。
    /// <para>expectedEmptyPopsId：真机上 pops_id 为空的 DLC 个数。官方 getDlcId = getPopsId() || getFilePath() || getUserContentId()，
    /// 所以 pops_id 为空是合法情况（Stellaris 有 4 个 "pops_id = \"\"" 的 .dlc），此时调用方会用 FilePathRelativeToDlcPath 当 id。</para>
    /// </summary>
    private static void CheckDlcShape(
        string label,
        IReadOnlyList<DlcInfo> dlcs,
        bool requireUniquePopsId = true,
        int expectedEmptyPopsId = 0)
    {
        var emptyPopsIdCount = dlcs.Count(dlc => string.IsNullOrWhiteSpace(dlc.PopsId));
        // 官方新增 DLC 可能带来新的空 pops_id（或把旧的补上），只锁下限与「还有非空行」。
        ChecksContext.Check(
            label + "：DLC PopsId 为空的个数 >= " + expectedEmptyPopsId.ToString(CultureInfo.InvariantCulture) + "（空是合法的，官方用 filePath 兜底）",
            emptyPopsIdCount >= expectedEmptyPopsId,
            emptyPopsIdCount.ToString(CultureInfo.InvariantCulture));
        ChecksContext.Check(
            label + "：DLC PopsId 非空的个数 > 0",
            dlcs.Count - emptyPopsIdCount > 0,
            (dlcs.Count - emptyPopsIdCount).ToString(CultureInfo.InvariantCulture));
        if (expectedEmptyPopsId > 0)
        {
            var emptyOnes = dlcs.Where(dlc => string.IsNullOrWhiteSpace(dlc.PopsId)).ToList();
            ChecksContext.Check(
                label + "：PopsId 为空的 DLC 都有非空 FilePathRelativeToDlcPath（可当 id 兜底）",
                emptyOnes.All(dlc => dlc.FilePathRelativeToDlcPath.Length > 0));
            ChecksContext.Check(
                label + "：PopsId 为空的 DLC 都有非空 DisplayName",
                emptyOnes.All(dlc => !string.IsNullOrWhiteSpace(dlc.DisplayName)));
            ChecksContext.Check(
                label + "：PopsId 为空的 DLC 都有非空 RawBytes",
                emptyOnes.All(dlc => dlc.RawBytes.Length > 0));
            foreach (var dlc in emptyOnes)
            {
                Console.WriteLine("  info " + label + "：pops_id 为空（合法） " + dlc.DisplayName + " -> " + dlc.FilePathRelativeToDlcPath);
            }
        }

        ChecksContext.Check(label + "：DLC DisplayName 全部非空", dlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.DisplayName)));
        ChecksContext.Check(label + "：DLC SteamId 全部非空", dlcs.All(dlc => !string.IsNullOrWhiteSpace(dlc.SteamId)));
        ChecksContext.Check(label + "：DLC ThumbnailPath 全部非空（同目录 thumbnail.png 存在）", dlcs.All(dlc => !string.IsNullOrEmpty(dlc.ThumbnailPath)));
        // dlc_metadata（Victoria 3 1.15 起）合成的条目没有磁盘描述文件，RawBytes 为空以保证
        // dlc_signature 不变；只要求真正来自 .dlc/.dlc.json 的条目带原始字节。
        ChecksContext.Check(
            label + "：磁盘描述文件（.dlc/.dlc.json）的 DLC RawBytes 全部非空",
            dlcs.Where(IsDiskDescriptor).All(dlc => dlc.RawBytes.Length > 0));
        ChecksContext.Check(
            label + "：dlc_metadata 合成条目没有 RawBytes（不污染 dlc_signature）",
            dlcs.Where(dlc => !IsDiskDescriptor(dlc)).All(dlc => dlc.RawBytes.Length == 0));
        ChecksContext.Check(
            label + "：DLC FilePathRelativeToDlcPath 全部是 POSIX 相对路径",
            dlcs.All(dlc => dlc.FilePathRelativeToDlcPath.Length > 0
                && !dlc.FilePathRelativeToDlcPath.Contains('\\')
                && !Path.IsPathRooted(dlc.FilePathRelativeToDlcPath)));
        if (requireUniquePopsId)
        {
            ChecksContext.Check(
                label + "：DLC 非空 PopsId 无重复",
                dlcs.Select(dlc => dlc.PopsId)
                    .Where(popsId => !string.IsNullOrWhiteSpace(popsId))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count() == dlcs.Count - emptyPopsIdCount);
        }
        else
        {
            Console.WriteLine("  info " + label + "：DLC PopsId 允许重复（真机存在 3 个重复 pops_id，跳过唯一性断言）");
        }
    }

    /// <summary>条目是否来自磁盘上的 .dlc / .dlc.json 描述文件（而不是 dlc_metadata 合成）。</summary>
    private static bool IsDiskDescriptor(DlcInfo dlc)
    {
        var path = dlc.FilePathRelativeToDlcPath;
        return path.EndsWith(".dlc", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".dlc.json", StringComparison.OrdinalIgnoreCase);
    }

    private static void CheckDlcAnchor(
        string label,
        IReadOnlyList<DlcInfo> dlcs,
        string popsId,
        string expectedDisplayName,
        string expectedSteamId,
        string expectedRelativePath)
    {
        var anchor = dlcs.FirstOrDefault(dlc => string.Equals(dlc.PopsId, popsId, StringComparison.OrdinalIgnoreCase));
        ChecksContext.Check(label + "：DLC 锚点 " + popsId + " 存在", anchor is not null);
        if (anchor is null)
        {
            return;
        }

        ChecksContext.Equal(label + "：锚点 DisplayName", expectedDisplayName, anchor.DisplayName);
        ChecksContext.Equal(label + "：锚点 SteamId", expectedSteamId, anchor.SteamId);
        ChecksContext.Check(
            label + "：锚点相对路径",
            string.Equals(
                anchor.FilePathRelativeToDlcPath.Replace('\\', '/'),
                expectedRelativePath,
                StringComparison.OrdinalIgnoreCase),
            anchor.FilePathRelativeToDlcPath);
        ChecksContext.Check(label + "：锚点缩略图存在", anchor.ThumbnailPath.Length > 0, anchor.ThumbnailPath);
    }

    private static void CheckModShape(string label, IReadOnlyList<PlaySetMod> mods)
    {
        ChecksContext.Check(label + "：mod 名称全部非空", mods.All(mod => !string.IsNullOrWhiteSpace(mod.Name)));
        ChecksContext.Check(
            label + "：mod Position 连续 0..n-1",
            mods.Select((mod, index) => mod.Position == index).All(flag => flag));
        ChecksContext.Check(label + "：mod ModId 无重复", mods.Select(mod => mod.ModId).Distinct().Count() == mods.Count);
        ChecksContext.Check(label + "：目录扫描得到的 mod 全部 Enabled（启用状态需结合播放集读取）", mods.All(mod => mod.Enabled));
        ChecksContext.Check(label + "：mod Status 全部非空", mods.All(mod => !string.IsNullOrWhiteSpace(mod.Status)));
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static GameInstallation? FindGame(IReadOnlyList<GameInstallation> installations, string gameId)
    {
        return installations.FirstOrDefault(game => string.Equals(game.GameId, gameId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>构造一个完整的 GameInstallation（其它自检类复用）。</summary>
    internal static GameInstallation CreateGame(
        string gameId,
        string installRoot,
        LoadFileFormat format,
        string gameDataDir,
        string dlcPath,
        string modDir,
        int? steamAppId)
    {
        return new GameInstallation(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: format,
            GameDataDir: gameDataDir,
            DlcPath: dlcPath,
            ModDir: modDir,
            ExePath: Path.Combine(installRoot, "binaries", gameId + ".exe"),
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
            SteamAppId: steamAppId,
            Support: GameSupport.Full);
    }

    /// <summary>在系统临时目录建一个一次性根目录（其它自检类复用）。</summary>
    internal static string CreateTempRoot(IFileSystem fs, string label)
    {
        var tempRoot = Path.Combine(
            Path.GetTempPath(),
            "pdxnext-playset-" + label + "-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(tempRoot);
        return tempRoot;
    }

    /// <summary>尽力删除一次性临时目录，失败只打日志（其它自检类复用）。</summary>
    internal static void TryDelete(IFileSystem fs, string path)
    {
        try
        {
            if (fs.DirectoryExists(path))
            {
                fs.Delete(path);
            }
        }
        catch (IOException exception)
        {
            Console.WriteLine("  warn 清理临时目录失败：" + exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            Console.WriteLine("  warn 清理临时目录无权限：" + exception.Message);
        }
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
