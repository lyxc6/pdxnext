namespace PdxLauncher.Checks;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;

/// <summary>
/// T2 自检：<see cref="DlcCatalog"/> 对 <c>game/dlc_metadata/*.txt</c>（Victoria 3 1.15 起的新数据源）的增量解析。
/// <para>
/// 覆盖：只有 metadata 时能出条目、与 <c>.dlc.json</c> 按 steam_id / 块名去重、坏文件与缺键跳过且不抛、
/// 名字清理（中文 + <c>#</c> 标记 + <c>$</c>）、没有 metadata 目录时条数不变、VIC3 17+1=18 形态。
/// </para>
/// <para>全部用一次性临时目录自造 fixture，不碰真机安装目录。</para>
/// </summary>
public static class DlcMetadataChecks
{
    private const string Victoria3GameId = "victoria3";
    private const string Ck3GameId = "ck3";

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckMetadataOnly(fs);
        CheckDedupeBySteamId(fs);
        CheckDedupeByBlockKey(fs);
        CheckBrokenMetadata(fs);
        CheckNameCleanup(fs);
        CheckNoMetadataDirectory(fs);
        CheckSiblingLayout(fs);
        CheckVictoria3Shape(fs);
        return ChecksContext.Failures - before;
    }

    // ------------------------------------------------------------------
    // 只有 metadata 时也能出条目（新 DLC 不再补 .dlc.json）
    // ------------------------------------------------------------------

    private static void CheckMetadataOnly(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 只有 dlc_metadata 时出条目");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-only");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            fs.EnsureDirectory(dlcPath);
            WriteMetadata(
                fs,
                Path.Combine(gameRoot, "dlc_metadata"),
                "00_dlc_metadata.txt",
                "dlc019 = {\n\tname = \"State and Revolution\"\n\tsteam_id = \"4314660\"\n\ttype = minor\n}\n");

            var iconDir = Path.Combine(gameRoot, "gfx", "interface", "icons", "dlc_icons");
            fs.EnsureDirectory(iconDir);
            var icon = Path.Combine(iconDir, "dlc019.dds");
            fs.WriteAllBytesAtomic(icon, new byte[] { 1, 2, 3, 4 });

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("只有 metadata 时出 1 条", 1, items.Count);
            if (items.Count == 1)
            {
                var dlc = items[0];
                ChecksContext.Equal("metadata DisplayName 取 name=", "State and Revolution", dlc.DisplayName);
                ChecksContext.Equal("metadata SteamId 取 steam_id=", "4314660", dlc.SteamId);
                ChecksContext.Equal("metadata PopsId 取块名", "dlc019", dlc.PopsId);
                ChecksContext.Equal("metadata 合成相对路径", "dlc_metadata/dlc019", dlc.FilePathRelativeToDlcPath);
                ChecksContext.Equal("metadata 图标取 dlc_icons/<块名>.dds", icon, dlc.ThumbnailPath);
                ChecksContext.Equal("metadata Description 为空", string.Empty, dlc.Description);
                ChecksContext.Equal("metadata RawBytes 为空（不污染 dlc_signature）", 0, dlc.RawBytes.Length);
                ChecksContext.Equal("metadata 有效 id 可写进 content_load", "dlc019", DlcCatalog.GetEffectiveDlcId(dlc));
                ChecksContext.Check("metadata 条目可被 content_load 禁用", DlcCatalog.CanBeDisabledInContentLoad(dlc));
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 与已有 .dlc.json 按 steam_id 去重（.dlc.json 优先）
    // ------------------------------------------------------------------

    private static void CheckDedupeBySteamId(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 与 .dlc.json 按 steam_id 去重");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-dupe-steam");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            var jsonDir = Path.Combine(dlcPath, "dlc", "dlc018_ep2");
            fs.EnsureDirectory(jsonDir);
            fs.WriteAllTextAtomic(
                Path.Combine(jsonDir, "dlc018_ep2.dlc.json"),
                "{\"id\":{\"steam\":\"4314650\",\"paradox\":\"dlc018_ep2\"},"
                + "\"displayName\":{\"en\":\"The Great Wave\"},"
                + "\"description\":{\"en\":\"Victoria 3: The Great Wave\"},"
                + "\"thumbnailPath\":\"thumbnail.png\",\"thirdPartyContent\":false}");

            WriteMetadata(
                fs,
                Path.Combine(gameRoot, "dlc_metadata"),
                "00_dlc_metadata.txt",
                "dlc018 = {\n\tname = \"The Great Wave\"\n\tsteam_id = \"4314650\"\n\ttype = major\n}\n\n"
                + "dlc019 = {\n\tname = \"State and Revolution\"\n\tsteam_id = \"4314660\"\n\ttype = minor\n}\n");

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("重复 steam_id 只保留 .dlc.json，另加新条目", 2, items.Count);
            var withSteam = items.Where(x => x.SteamId == "4314650").ToList();
            ChecksContext.Equal("steam 4314650 只出现一次", 1, withSteam.Count);
            if (withSteam.Count == 1)
            {
                ChecksContext.Equal(
                    ".dlc.json 的条目优先（路径仍是 dlc/...）",
                    "dlc/dlc018_ep2/dlc018_ep2.dlc.json",
                    withSteam[0].FilePathRelativeToDlcPath);
                ChecksContext.Check(".dlc.json 条目保留 RawBytes", withSteam[0].RawBytes.Length > 0);
            }

            var newDlc = items.Where(x => x.SteamId == "4314660").ToList();
            ChecksContext.Equal("新 DLC 4314660 出现一次", 1, newDlc.Count);
            if (newDlc.Count == 1)
            {
                ChecksContext.Equal("新 DLC 名字", "State and Revolution", newDlc[0].DisplayName);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 与已有条目按块名去重（steam_id 可缺失）
    // ------------------------------------------------------------------

    private static void CheckDedupeByBlockKey(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 与已有条目按块名去重");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-dupe-id");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            var jsonDir = Path.Combine(dlcPath, "dlc", "dlc019_ep3");
            fs.EnsureDirectory(jsonDir);
            // 故意不给 id.steam：只能靠 id.paradox（块名）识别重复。
            fs.WriteAllTextAtomic(
                Path.Combine(jsonDir, "dlc019.dlc.json"),
                "{\"id\":{\"paradox\":\"dlc019\"},\"displayName\":{\"en\":\"Edge Case DLC\"}}");

            WriteMetadata(
                fs,
                Path.Combine(gameRoot, "dlc_metadata"),
                "00_dlc_metadata.txt",
                "dlc019 = {\n\tname = \"State and Revolution\"\n\tsteam_id = \"4314660\"\n}\n\n"
                + "dlc019 = {\n\tname = \"Duplicate Block\"\n\tsteam_id = \"9999999\"\n}\n");

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("块名已存在时不再补，同文件重复块也只算一次", 1, items.Count);
            if (items.Count == 1)
            {
                ChecksContext.Equal("保留 .dlc.json 的名字", "Edge Case DLC", items[0].DisplayName);
                ChecksContext.Equal("保留 .dlc.json 的路径", "dlc/dlc019_ep3/dlc019.dlc.json", items[0].FilePathRelativeToDlcPath);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 坏文件、缺 name/steam_id：跳过并记 Warn，不抛
    // ------------------------------------------------------------------

    private static void CheckBrokenMetadata(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 坏 metadata 跳过且不抛");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-broken");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            fs.EnsureDirectory(dlcPath);
            var metadataDir = Path.Combine(gameRoot, "dlc_metadata");
            WriteMetadata(
                fs,
                metadataDir,
                "00_dlc_metadata.txt",
                "dlc020 = {\n\tsteam_id = \"1\"\n}\n\n"
                + "dlc021 = {\n\tname = \"No Steam\"\n}\n\n"
                + "dlc022 = {\n\tname = \"Good One\"\n\tsteam_id = \"22\"\n}\n");
            WriteMetadata(fs, metadataDir, "zz_garbage.txt", "}}} 这不是 Clausewitz { name = }");
            // .md 是官方文档，必须忽略（只扫 *.txt）。
            WriteMetadata(fs, metadataDir, "dlc_metadata.md", "dlc099 = { name = \"From Docs\" steam_id = \"99\" }");

            var logger = new RecordingLogger();
            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var threw = false;
            IReadOnlyList<DlcInfo> items = Array.Empty<DlcInfo>();
            try
            {
                items = Discover(fs, game, logger);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                threw = true;
            }

            ChecksContext.Check("坏 metadata 不抛异常", !threw);
            ChecksContext.Equal("缺 name/steam_id 的块被跳过", 1, items.Count);
            if (items.Count == 1)
            {
                ChecksContext.Equal("合法块照常收录", "Good One", items[0].DisplayName);
            }

            ChecksContext.Check("缺 name 记 Warn", logger.CountOf("缺少 name") >= 1);
            ChecksContext.Check("缺 steam_id 记 Warn", logger.CountOf("缺少 steam_id") >= 1);
            ChecksContext.Check("文档 .md 不被当成 DLC 数据", items.All(x => x.DisplayName != "From Docs"));
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 名字清理：中文原样保留，# 标记去掉，$ 变量不误伤
    // ------------------------------------------------------------------

    private static void CheckNameCleanup(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 名字清理（中文 + # 标记 + $）");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-name");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            fs.EnsureDirectory(dlcPath);
            WriteMetadata(
                fs,
                Path.Combine(gameRoot, "dlc_metadata"),
                "00_dlc_metadata.txt",
                "dlc030 = {\n\tname = \"#bold 中文 DLC $PRICE$ #!\"\n\tsteam_id = \"30\"\n}\n");

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("名字清理后仍出 1 条", 1, items.Count);
            if (items.Count == 1)
            {
                ChecksContext.Equal("去掉 #bold/#!，保留中文与 $PRICE$", "中文 DLC $PRICE$", items[0].DisplayName);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 没有 dlc_metadata 目录：其它游戏的条数不受影响
    // ------------------------------------------------------------------

    private static void CheckNoMetadataDirectory(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] 无 metadata 目录时条数不变（CK3）");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-none");
        try
        {
            var dlcPath = Path.Combine(root, "game", "dlc");
            var dlcDir = Path.Combine(dlcPath, "dlc001");
            fs.EnsureDirectory(dlcDir);
            fs.WriteAllTextAtomic(
                Path.Combine(dlcDir, "dlc001.dlc"),
                "name = \"Symbols of Domination\"\npath = \"dlc/dlc001\"\npops_id = \"symbols_of_domination\"\nsteam_id = \"447680\"\n");

            var game = CreateGame(Ck3GameId, root, dlcPath, LoadFileFormat.DlcLoadV0);
            var items = Discover(fs, game);

            ChecksContext.Equal("CK3 无 metadata 目录时仍是 1 条", 1, items.Count);
            if (items.Count == 1)
            {
                ChecksContext.Equal("CK3 条目名不变", "Symbols of Domination", items[0].DisplayName);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // dlcPath 指向 game/dlc 时，metadata 在上一级（兼容布局）
    // ------------------------------------------------------------------

    private static void CheckSiblingLayout(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] metadata 位于 dlcPath 的上一级");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-sibling");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = Path.Combine(gameRoot, "dlc");
            fs.EnsureDirectory(dlcPath);
            WriteMetadata(
                fs,
                Path.Combine(gameRoot, "dlc_metadata"),
                "00_dlc_metadata.txt",
                "dlc040 = {\n\tname = \"Sibling Layout\"\n\tsteam_id = \"40\"\n}\n");

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("dlcPath=game/dlc 时也能读到上一级 metadata", 1, items.Count);
            if (items.Count == 1)
            {
                ChecksContext.Equal("上一级 metadata 名字", "Sibling Layout", items[0].DisplayName);
            }
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // VIC3 形态：17 个 .dlc.json + 12 条 metadata（11 条重复 + dlc019）= 18
    // ------------------------------------------------------------------

    private static void CheckVictoria3Shape(IFileSystem fs)
    {
        Console.WriteLine("[DlcMetadataChecks] VIC3 形态 17+1=18");

        var root = PlaysetChecks.CreateTempRoot(fs, "dlcmeta-vic3");
        try
        {
            var gameRoot = Path.Combine(root, "game");
            var dlcPath = gameRoot;
            var steamIds = new[]
            {
                "4314650", "3450170", "2411231", "3562890", "3562730", "3174360",
                "2282100", "2348450", "2411230", "2591240", "3562810",
                "900001", "900002", "900003", "900004", "900005", "900006",
            };

            for (var i = 0; i < steamIds.Length; i++)
            {
                var id = "dlc" + i.ToString("D3", CultureInfo.InvariantCulture) + "_pack";
                var dir = Path.Combine(dlcPath, "dlc", id);
                fs.EnsureDirectory(dir);
                fs.WriteAllTextAtomic(
                    Path.Combine(dir, id + ".dlc.json"),
                    "{\"id\":{\"steam\":\"" + steamIds[i] + "\",\"paradox\":\"" + id + "\"},"
                    + "\"displayName\":{\"en\":\"Fixture DLC " + i.ToString(CultureInfo.InvariantCulture) + "\"},"
                    + "\"thumbnailPath\":\"thumbnail.png\",\"thirdPartyContent\":false}");
            }

            var metadata = new System.Text.StringBuilder();
            for (var i = 0; i < 12; i++)
            {
                var id = "dlc" + i.ToString("D3", CultureInfo.InvariantCulture);
                metadata.Append(id).Append(" = {\n\tname = \"Fixture DLC ")
                    .Append(i.ToString(CultureInfo.InvariantCulture))
                    .Append("\"\n\tsteam_id = \"").Append(steamIds[i]).Append("\"\n}\n\n");
            }

            metadata.Append("dlc019 = {\n\tname = \"State and Revolution\"\n\tsteam_id = \"4314660\"\n\ttype = minor\n}\n");
            WriteMetadata(fs, Path.Combine(gameRoot, "dlc_metadata"), "00_dlc_metadata.txt", metadata.ToString());

            var game = CreateGame(Victoria3GameId, root, dlcPath, LoadFileFormat.ContentLoadV1_1);
            var items = Discover(fs, game);

            ChecksContext.Equal("17 磁盘 + dlc019 = 18", 18, items.Count);
            ChecksContext.Equal(
                "18 条里 SteamId 无重复",
                18,
                items.Select(x => x.SteamId).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count());

            var newDlc = items.Where(x => x.SteamId == "4314660").ToList();
            ChecksContext.Equal("4314660 只出现一次", 1, newDlc.Count);
            if (newDlc.Count == 1)
            {
                ChecksContext.Equal("dlc019 名字", "State and Revolution", newDlc[0].DisplayName);
            }

            ChecksContext.Equal("dlc019 在结尾（按相对路径排序）", "dlc019", items[^1].PopsId);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }
    }

    // ------------------------------------------------------------------
    // 辅助
    // ------------------------------------------------------------------

    private static DlcCatalog CreateCatalog(IFileSystem fs, IAppLogger? logger = null)
        => new(fs, logger ?? new NullLogger());

    private static IReadOnlyList<DlcInfo> Discover(IFileSystem fs, GameInstallation game, IAppLogger? logger = null)
        => CreateCatalog(fs, logger).DiscoverAsync(game, CancellationToken.None).GetAwaiter().GetResult();

    private static GameInstallation CreateGame(string gameId, string installRoot, string dlcPath, LoadFileFormat format)
        => PlaysetChecks.CreateGame(
            gameId,
            installRoot,
            format,
            Path.Combine(installRoot, "documents", gameId),
            dlcPath,
            Path.Combine(installRoot, "documents", gameId, "mod"),
            steamAppId: 529340);

    private static void WriteMetadata(IFileSystem fs, string metadataDir, string fileName, string content)
    {
        fs.EnsureDirectory(metadataDir);
        fs.WriteAllTextAtomic(Path.Combine(metadataDir, fileName), content);
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

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Infos { get; } = new();

        public List<string> Warnings { get; } = new();

        public void Info(string message) => Infos.Add(message);

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);

        public int CountOf(string fragment) =>
            Infos.Count(m => m.Contains(fragment, StringComparison.Ordinal))
            + Warnings.Count(m => m.Contains(fragment, StringComparison.Ordinal));
    }
}
