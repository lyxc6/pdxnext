namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 自检：创意工坊 mod 的工坊 id 解析。
/// <para>
/// 真机教训：metadata.json 的 <c>"id"</c> 不是创意工坊 id，官方启动器把它写在 <c>mods.metadataId</c> 里
/// （"akagilnc@gmail.com-daoyu-cheat-113"、"tech.res"）。工坊 id 只能来自工坊目录名，
/// 拿不到就会退化成「本地 mod」，详情页 / Steam / 浏览器三个按钮全灰。
/// </para>
/// </summary>
public static class ModCatalogChecks
{
    private const string WorkshopAppId = "529340";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckWorkshopFolderNameWins(fs, logger);
        CheckModVersionFromDescriptor(fs, logger);
        CheckMetadataIdFallback(fs, logger);
        CheckLocalModDoesNotInventSteamId(fs, logger);
        CheckComposePrefersNumericSqliteId(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------
    // 用例
    // ------------------------------------------------------------------

    /// <summary>工坊目录名是数字时，PDX 侧 metadata id 不能把它顶掉。</summary>
    private static void CheckWorkshopFolderNameWins(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModCatalogChecks] 工坊目录名优先");
        var root = PlaysetChecks.CreateTempRoot(fs, "modcatalog-folder");
        try
        {
            var game = CreateGame(fs, root);
            var modDirectory = Path.Combine(
                root,
                "library",
                "steamapps",
                "workshop",
                "content",
                WorkshopAppId,
                "3742679779");
            WriteMetadata(fs, modDirectory, "Daoyu Cheat", "akagilnc@gmail.com-daoyu-cheat-113", "1.13.*", "0.3dev");

            var mod = DiscoverSingle(fs, logger, game, "Daoyu Cheat");
            ChecksContext.Equal("工坊目录名是数字时压过 PDX 侧 metadata id", "3742679779", mod.SteamId ?? "<null>");
            ChecksContext.Equal(
                "工坊 mod 能拼出创意工坊页地址",
                "https://steamcommunity.com/sharedfiles/filedetails/?id=3742679779",
                SteamWorkshop.GetBrowserAddress(SteamWorkshop.GetWorkshopId(mod.SteamId, mod.GameRegistryId)) ?? "<null>");
            ChecksContext.Check("工坊 mod 的 requiredVersion 来自 supported_game_version", mod.RequiredVersion == "1.13.*", mod.RequiredVersion);
            // Vic3 大量 mod 没写 supported_game_version，但 metadata.json 的 version 往往有值：
            // 这一个是「作者版本」，与支持的游戏版本分开保存、分开展示。
            ChecksContext.Equal("工坊 mod 的 modVersion 来自 metadata.json 的 version", "0.3dev", mod.ModVersion ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>
    /// legacy .mod 描述文件的 version 是作者版本，supported_version 才是支持的游戏版本；
    /// 两者都要读出来，不能把 version 丢掉（Mod 库与添加模组页要分开显示）。
    /// </summary>
    private static void CheckModVersionFromDescriptor(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModCatalogChecks] .mod 的 version 与 supported_version");
        var root = PlaysetChecks.CreateTempRoot(fs, "modcatalog-descriptor-version");
        try
        {
            var game = CreateGame(fs, root);
            fs.EnsureDirectory(Path.Combine(game.ModDir, "Legacy Mod"));
            fs.WriteAllTextAtomic(
                Path.Combine(game.ModDir, "Legacy Mod.mod"),
                "name=\"Legacy Mod\"\nversion=\"2.4.1\"\nsupported_version=\"1.19.*\"\npath=\"mod/Legacy Mod\"\n");

            var mod = DiscoverSingle(fs, logger, game, "Legacy Mod");
            ChecksContext.Equal(".mod 的 supported_version 进 RequiredVersion", "1.19.*", mod.RequiredVersion);
            ChecksContext.Equal(".mod 的 version 进 ModVersion", "2.4.1", mod.ModVersion ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>目录名不是数字（解压安装的工坊副本）时，退回纯数字的 metadata id。</summary>
    private static void CheckMetadataIdFallback(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModCatalogChecks] metadata id 兜底");
        var root = PlaysetChecks.CreateTempRoot(fs, "modcatalog-fallback");
        try
        {
            var game = CreateGame(fs, root);
            var modDirectory = Path.Combine(
                root,
                "library",
                "steamapps",
                "workshop",
                "content",
                WorkshopAppId,
                "manual-port");
            WriteMetadata(fs, modDirectory, "Manual Port", "2879823725", "1.13.*");

            var mod = DiscoverSingle(fs, logger, game, "Manual Port");
            ChecksContext.Equal("目录名非数字时用纯数字 metadata id 兜底", "2879823725", mod.SteamId ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>本地 mod 目录（&lt;gameDataDir&gt;\mod\&lt;dir&gt;）不拿目录名编造工坊 id。</summary>
    private static void CheckLocalModDoesNotInventSteamId(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModCatalogChecks] 本地 mod 不编造工坊 id");
        var root = PlaysetChecks.CreateTempRoot(fs, "modcatalog-local");
        try
        {
            var game = CreateGame(fs, root);

            var pdxLocal = Path.Combine(game.ModDir, "MyLocalMod");
            WriteMetadata(fs, pdxLocal, "My Local Mod", "tech.res", null);

            var numericLocal = Path.Combine(game.ModDir, "2879823726");
            WriteMetadata(fs, numericLocal, "Numeric Local Mod", "2879823726", null);

            var mods = Discover(fs, logger, game);
            var pdxMod = mods.Single(static mod => mod.Name == "My Local Mod");
            ChecksContext.Check(
                "本地 mod 目录名是数字也不当工坊 id（目录名不参与）",
                pdxMod.SteamId is null,
                pdxMod.SteamId ?? "<null>");

            var numericMod = mods.Single(static mod => mod.Name == "Numeric Local Mod");
            ChecksContext.Equal(
                "本地 mod 的 metadata id 本身是数字时仍可做工坊 id",
                "2879823726",
                numericMod.SteamId ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    /// <summary>扫描值不是数字时，Compose 必须让库里已登记的数字 steamId 顶上。</summary>
    private static void CheckComposePrefersNumericSqliteId(IFileSystem fs, NullLogger logger)
    {
        Console.WriteLine("[ModCatalogChecks] 合并时数字 steamId 优先");
        var root = PlaysetChecks.CreateTempRoot(fs, "modcatalog-compose");
        try
        {
            var game = CreateGame(fs, root);
            var modDirectory = Path.Combine(game.ModDir, "PdxStyleLocal");
            WriteMetadata(fs, modDirectory, "Pdx Style Local", "com.example.pdx-style", null);

            // 库里这一行是官方启动器登记的：steamId 是数字，dirPath 指向同一个目录。
            SeedModsTable(fs, PlaySetRepository.GetDatabasePath(game), modDirectory);

            var mod = DiscoverSingle(fs, logger, game, "Pdx Style Local");
            ChecksContext.Equal("扫描值不是数字时保留库里登记的数字 steamId", "3742679779", mod.SteamId ?? "<null>");
            ChecksContext.Check(
                "库里已登记的行沿用库里的 Guid",
                mod.ModId == Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                mod.ModId.ToString("D"));
            // metadata.json 没写 version 时，官方库的 metadataVersion 要顶上。
            ChecksContext.Equal("库里的 metadataVersion 补成 modVersion", "1.6'", mod.ModVersion ?? "<null>");
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static GameInstallation CreateGame(IFileSystem fs, string root)
    {
        // <root>/library/steamapps/common/Victoria 3 → workshop 从 ../../workshop/content/<appid> 推导。
        var installRoot = Path.Combine(root, "library", "steamapps", "common", "Victoria 3");
        var gameDataDir = Path.Combine(root, "documents", "Victoria 3");
        var modDir = Path.Combine(gameDataDir, "mod");
        fs.EnsureDirectory(installRoot);
        fs.EnsureDirectory(gameDataDir);
        fs.EnsureDirectory(modDir);

        return PlaysetChecks.CreateGame(
            "victoria3",
            installRoot,
            LoadFileFormat.ContentLoadV1_1,
            gameDataDir,
            Path.Combine(installRoot, "game", "dlc"),
            modDir,
            int.Parse(WorkshopAppId, System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IReadOnlyList<PlaySetMod> Discover(IFileSystem fs, NullLogger logger, GameInstallation game)
        => new ModCatalog(fs, logger).DiscoverAllAsync(game, CancellationToken.None).GetAwaiter().GetResult();

    private static PlaySetMod DiscoverSingle(IFileSystem fs, NullLogger logger, GameInstallation game, string name)
        => Discover(fs, logger, game).Single(mod => string.Equals(mod.Name, name, StringComparison.Ordinal));

    private static void WriteMetadata(
        IFileSystem fs,
        string modDirectory,
        string name,
        string metadataId,
        string? supportedGameVersion,
        string? modVersion = null)
    {
        var metadataDirectory = Path.Combine(modDirectory, ".metadata");
        fs.EnsureDirectory(metadataDirectory);

        var json = "{\"name\":\"" + name + "\",\"id\":\"" + metadataId + "\"";
        if (!string.IsNullOrWhiteSpace(supportedGameVersion))
        {
            json += ",\"supported_game_version\":\"" + supportedGameVersion + "\"";
        }

        if (!string.IsNullOrWhiteSpace(modVersion))
        {
            json += ",\"version\":\"" + modVersion + "\"";
        }

        json += "}";
        fs.WriteAllTextAtomic(Path.Combine(metadataDirectory, "metadata.json"), json);
    }

    /// <summary>造一个只有 mods 表的临时库，模拟官方启动器已经登记过的行。</summary>
    private static void SeedModsTable(IFileSystem fs, string databasePath, string dirPath)
    {
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE mods (id char(36) PRIMARY KEY, displayName varchar(255), thumbnailPath text, dirPath text, archivePath text, gameRegistryId text, steamId text, requiredVersion varchar(255), metadataVersion varchar(255), status text NOT NULL, source text NOT NULL)");
        connection.Execute(
            "INSERT INTO mods (id, displayName, thumbnailPath, dirPath, archivePath, gameRegistryId, steamId, requiredVersion, metadataVersion, status, source) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)",
            "cccccccc-cccc-cccc-cccc-cccccccccccc",
            "Pdx Style Local",
            null,
            dirPath,
            null,
            string.Empty,
            "3742679779",
            null,
            "1.6'",
            "ready_to_play",
            "steam");
    }

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
