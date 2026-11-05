namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Saves;

/// <summary>
/// 自检：存档扫描的目录落点、递归深度、并行上限、中文文件名、排序稳定性、候选截断与取消。
/// <para>全部在系统临时目录里造结构，绝不扫描或写入真机的 <c>Documents\Paradox Interactive</c>。</para>
/// </summary>
public static class SaveGameScannerChecks
{
    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var logger = new NullLogger();
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-scan");
        try
        {
            var documents = Path.Combine(root, "Documents");
            var localAppData = Path.Combine(root, "Local");
            var localAppDataLow = Path.Combine(root, "LocalLow");
            var catalog = new SaveLocationCatalog(fs, documents, localAppData, localAppDataLow);
            var reader = new SavContainerReader(fs, logger);
            var scanner = new SaveGameScanner(fs, logger, catalog, reader);

            CheckConstants();
            CheckMissingDirectory(fs, scanner, catalog, documents);
            CheckVictoria3Scan(fs, logger, catalog, reader, documents);
            CheckStellarisDepth(fs, reader, catalog, documents);
            CheckCs2Scan(fs, reader, catalog, localAppDataLow);
            CheckCs1Scan(fs, reader, catalog, localAppData);
            CheckCap(fs, logger, catalog, reader, documents);
            CheckCancellation(fs, logger, catalog, reader);
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckConstants()
    {
        Console.WriteLine("[SaveGameScannerChecks] 常量");
        ChecksContext.Equal("默认候选上限 2000", 2000, SaveGameScanner.DefaultMaxFiles);
        ChecksContext.Equal("解析并行度 4", 4, SaveGameScanner.MaxDegreeOfParallelism);
    }

    private static void CheckMissingDirectory(
        IFileSystem fs,
        SaveGameScanner scanner,
        SaveLocationCatalog catalog,
        string documents)
    {
        Console.WriteLine("[SaveGameScannerChecks] 目录不存在返回空集合");
        ChecksContext.Equal("未建目录时 Resolve 为空", 0, catalog.Resolve("ck3").Count);
        var metas = Await(scanner.ScanAsync(["ck3"], CancellationToken.None));
        ChecksContext.Equal("未建目录时扫描为空", 0, metas.Count);
        ChecksContext.Equal("未扫描的游戏按游戏取为空", 0, scanner.GetByGame("ck3").Count);
        ChecksContext.Equal("未扫描时 GetAll 为空", 0, scanner.GetAll().Count);
        var expected = Path.Combine(
            documents,
            "Paradox Interactive",
            "Crusader Kings III",
            "save games");
        ChecksContext.Check("Resolve 不会顺手创建存档目录", !fs.DirectoryExists(expected), expected);
    }

    private static void CheckVictoria3Scan(
        IFileSystem fs,
        NullLogger logger,
        SaveLocationCatalog catalog,
        SavContainerReader reader,
        string documents)
    {
        Console.WriteLine("[SaveGameScannerChecks] Victoria 3 平铺扫描与稳定排序");
        var directory = Path.Combine(documents, "Paradox Interactive", "Victoria 3", "save games");
        fs.EnsureDirectory(directory);
        WriteText(fs, Path.Combine(directory, "b.v3"), "not a real save");
        WriteText(fs, Path.Combine(directory, "a.v3"), "not a real save");
        WriteText(fs, Path.Combine(directory, "c.v3"), "not a real save");
        WriteText(fs, Path.Combine(directory, "ignored.txt"), "extension is not whitelisted");

        var scanner = new SaveGameScanner(fs, logger, catalog, reader);
        var first = Await(scanner.ScanAsync(["victoria3"], CancellationToken.None));
        ChecksContext.Equal("只收集白名单扩展名", 3, first.Count);
        ChecksContext.Equal(
            "按文件名 Ordinal 稳定排序",
            "a.v3|b.v3|c.v3",
            string.Join("|", first.Select(static meta => meta.FileName)));
        ChecksContext.Equal("扫描结果全部属于 victoria3", 3, scanner.GetByGame("victoria3").Count);
        ChecksContext.Equal("GetByGame 大小写不敏感", 3, scanner.GetByGame("VICTORIA3").Count);
        ChecksContext.Equal("GetByGame 未知游戏为空", 0, scanner.GetByGame("no_such_game").Count);
        ChecksContext.Equal("GetAll 与最近一次扫描一致", 3, scanner.GetAll().Count);

        var second = Await(scanner.ScanAsync(["victoria3"], CancellationToken.None));
        ChecksContext.Equal(
            "重复扫描顺序不变",
            string.Join("|", first.Select(static meta => meta.FileName)),
            string.Join("|", second.Select(static meta => meta.FileName)));
        ChecksContext.Equal("Resolve 给出平铺深度", 0, catalog.Resolve("victoria3")[0].MaxDepth);
    }

    private static void CheckStellarisDepth(
        IFileSystem fs,
        SavContainerReader reader,
        SaveLocationCatalog catalog,
        string documents)
    {
        Console.WriteLine("[SaveGameScannerChecks] Stellaris 子目录递归与中文文件名");
        var directory = Path.Combine(documents, "Paradox Interactive", "Stellaris", "save games");
        var nested = Path.Combine(directory, "2_1757314770", "nested");
        fs.EnsureDirectory(nested);
        var source = ChecksContext.FixturePath("saves/stellaris/凯尔赞枢纽_2210_06_17.sav");
        fs.WriteAllBytesAtomic(Path.Combine(nested, "凯尔赞枢纽.sav"), File.ReadAllBytes(source));
        fs.WriteAllBytesAtomic(Path.Combine(directory, "根目录存档.sav"), File.ReadAllBytes(source));

        var scanner = new SaveGameScanner(fs, new NullLogger(), catalog, reader);
        var metas = Await(scanner.ScanAsync(["stellaris"], CancellationToken.None));
        ChecksContext.Equal("Stellaris 深度 2 收集两份", 2, metas.Count);
        var nestedMeta = metas.FirstOrDefault(static meta => meta.FileName == "凯尔赞枢纽.sav");
        ChecksContext.Check("中文文件名原样保留", nestedMeta is not null, string.Join("|", metas.Select(static meta => meta.FileName)));
        ChecksContext.Equal("子目录里的存档名", "凯尔赞枢纽 2", nestedMeta?.SaveName ?? "<none>");
        ChecksContext.Equal("子目录里的 zip 版本", "Corvus v4.2.2", nestedMeta?.GameVersion ?? "<none>");
        ChecksContext.Equal("Stellaris 落点深度为 2", 2, catalog.Resolve("stellaris")[0].MaxDepth);
    }

    private static void CheckCs2Scan(
        IFileSystem fs,
        SavContainerReader reader,
        SaveLocationCatalog catalog,
        string localAppDataLow)
    {
        Console.WriteLine("[SaveGameScannerChecks] CS2 LocalLow 落点");
        var directory = Path.Combine(
            localAppDataLow,
            "Colossal Order",
            "Cities Skylines II",
            "Saves",
            "76561198000000000");
        fs.EnsureDirectory(directory);
        var cokSource = ChecksContext.FixturePath("saves/cs2/阿伦德尔 2.cok");
        var cidSource = ChecksContext.FixturePath("saves/cs2/阿伦德尔 2.cid");
        fs.WriteAllBytesAtomic(Path.Combine(directory, "阿伦德尔 2.cok"), File.ReadAllBytes(cokSource));
        fs.WriteAllBytesAtomic(Path.Combine(directory, "阿伦德尔 2.cid"), File.ReadAllBytes(cidSource));

        var scanner = new SaveGameScanner(fs, new NullLogger(), catalog, reader);
        var metas = Await(scanner.ScanAsync(["cities_skylines_2"], CancellationToken.None));
        ChecksContext.Equal("每个 .cok 只算一份，不把 .cid 当存档", 1, metas.Count);
        var first = metas.Count > 0 ? metas[0] : null;
        ChecksContext.Equal("CS2 中文存档名", "阿伦德尔 2", first?.SaveName ?? "<none>");
        ChecksContext.Equal("CS2 落点深度为 1", 1, catalog.Resolve("cities_skylines_2")[0].MaxDepth);
    }

    private static void CheckCs1Scan(
        IFileSystem fs,
        SavContainerReader reader,
        SaveLocationCatalog catalog,
        string localAppData)
    {
        Console.WriteLine("[SaveGameScannerChecks] CS1 Local 落点");
        var directory = Path.Combine(localAppData, "Colossal Order", "Cities_Skylines", "Saves");
        fs.EnsureDirectory(directory);
        fs.WriteAllTextAtomic(Path.Combine(directory, "我的城市.crp"), "not a real save");

        var scanner = new SaveGameScanner(fs, new NullLogger(), catalog, reader);
        var metas = Await(scanner.ScanAsync(["cities_skylines"], CancellationToken.None));
        ChecksContext.Equal("CS1 .crp 按文件名收录", 1, metas.Count);
        var first = metas.Count > 0 ? metas[0] : null;
        ChecksContext.Equal("CS1 中文存档名", "我的城市", first?.SaveName ?? "<none>");
        ChecksContext.Equal("CS1 按文件名解析仍为 Ok", SaveParseState.Ok, first?.ParseState ?? SaveParseState.Degraded);
        ChecksContext.Equal("CS1 落点深度为 0", 0, catalog.Resolve("cities_skylines")[0].MaxDepth);
    }

    private static void CheckCap(
        IFileSystem fs,
        NullLogger logger,
        SaveLocationCatalog catalog,
        SavContainerReader reader,
        string documents)
    {
        Console.WriteLine("[SaveGameScannerChecks] 候选上限截断");
        var directory = Path.Combine(documents, "Paradox Interactive", "Victoria 3", "save games");
        var scanner = new SaveGameScanner(fs, logger, catalog, reader, maxFiles: 2);
        var metas = Await(scanner.ScanAsync(["victoria3"], CancellationToken.None));
        ChecksContext.Equal("超过上限时只返回上限条数", 2, metas.Count);
    }

    private static void CheckCancellation(
        IFileSystem fs,
        NullLogger logger,
        SaveLocationCatalog catalog,
        SavContainerReader reader)
    {
        Console.WriteLine("[SaveGameScannerChecks] 取消");
        var scanner = new SaveGameScanner(fs, logger, catalog, reader);
        var canceled = scanner.ScanAsync(["victoria3"], new CancellationToken(canceled: true));
        ChecksContext.Check("预取消的 token 让扫描任务直接 Canceled", canceled.IsCanceled);
    }

    private static IReadOnlyList<SaveGameMeta> Await(Task<IReadOnlyList<SaveGameMeta>> task)
    {
        // 自检入口是同步签名（Program.cs 只认 static int Run(IFileSystem)），
        // 这里必须同步等待；生产调用方始终 await ScanAsync。
        return task.GetAwaiter().GetResult();
    }

    private static void WriteText(IFileSystem fs, string path, string text) => fs.WriteAllTextAtomic(path, text);

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
