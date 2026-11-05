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
            CheckSnapshotMerge(fs, logger, catalog, reader);
            CheckCs2Scan(fs, reader, catalog, localAppDataLow);
            CheckCs1Scan(fs, reader, catalog, localAppData);
            CheckCap(fs, logger, catalog, reader, documents);
            CheckCancellation(fs, logger, catalog, reader);
            CheckCancellationDuringScan(fs, logger, catalog, reader, documents);
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
        var missing = catalog.GetLocations("ck3");
        ChecksContext.Equal("未建目录时 GetLocations 仍登记一条", 1, missing.Count);
        ChecksContext.Check("未建目录时 Exists=false", missing.Count == 1 && !missing[0].Exists);
        ChecksContext.Equal(
            "CK3 落点来源为 Documents",
            SaveLocationSource.Documents,
            missing.Count > 0 ? missing[0].Source : SaveLocationSource.LocalAppData);
        ChecksContext.Equal("未登记的游戏 GetLocations 为空", 0, catalog.GetLocations("no_such_game").Count);
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

    private static void CheckSnapshotMerge(
        IFileSystem fs,
        NullLogger logger,
        SaveLocationCatalog catalog,
        SavContainerReader reader)
    {
        Console.WriteLine("[SaveGameScannerChecks] 分次扫描按 gameId 合并快照");
        var scanner = new SaveGameScanner(fs, logger, catalog, reader);
        var v3 = Await(scanner.ScanAsync(["victoria3"], CancellationToken.None));
        ChecksContext.Equal("先扫 Victoria 3", 3, v3.Count);

        var merged = Await(scanner.ScanAsync(["stellaris"], CancellationToken.None));
        ChecksContext.Equal("合并快照保留未参与本次扫描的游戏", 5, merged.Count);
        ChecksContext.Equal("合并后 Victoria 3 仍在", 3, scanner.GetByGame("victoria3").Count);
        ChecksContext.Equal("合并后 Stellaris 为 2", 2, scanner.GetByGame("stellaris").Count);
        ChecksContext.Equal("GetAll 与合并结果一致", merged.Count, scanner.GetAll().Count);
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
        ChecksContext.Equal("CS1 没有元数据时为 Degraded", SaveParseState.Degraded, first?.ParseState ?? SaveParseState.Ok);
        ChecksContext.Check("CS1 降级原因非空", !string.IsNullOrWhiteSpace(first?.DegradeReason), first?.DegradeReason);
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

    private static void CheckCancellationDuringScan(
        IFileSystem fs,
        NullLogger logger,
        SaveLocationCatalog catalog,
        SavContainerReader reader,
        string documents)
    {
        Console.WriteLine("[SaveGameScannerChecks] 扫描途中取消");
        var directory = Path.Combine(documents, "Paradox Interactive", "Europa Universalis V", "save games");
        fs.EnsureDirectory(directory);
        for (var index = 0; index < 60; index++)
        {
            WriteText(fs, Path.Combine(directory, "bulk_" + index.ToString("00", System.Globalization.CultureInfo.InvariantCulture) + ".eu5"), "SAV0\n");
        }

        using var cancellation = new CancellationTokenSource();
        var cancelingFileSystem = new CancelAfterFileSystem(fs, cancellation, cancelAfter: 5);
        var scanner = new SaveGameScanner(cancelingFileSystem, logger, catalog, reader);
        var task = scanner.ScanAsync(["eu5"], cancellation.Token);

        var canceled = false;
        try
        {
            task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            canceled = true;
        }

        ChecksContext.Check("扫描途中取消按契约抛 OperationCanceledException", canceled);
        ChecksContext.Check("扫描途中取消后任务为 Canceled", task.IsCanceled);
        ChecksContext.Equal("取消的扫描不发布半成品快照", 0, scanner.GetAll().Count);
    }

    private static IReadOnlyList<SaveGameMeta> Await(Task<IReadOnlyList<SaveGameMeta>> task)
    {
        // 自检入口是同步签名（Program.cs 只认 static int Run(IFileSystem)），
        // 这里必须同步等待；生产调用方始终 await ScanAsync。
        return task.GetAwaiter().GetResult();
    }

    private static void WriteText(IFileSystem fs, string path, string text) => fs.WriteAllTextAtomic(path, text);

    /// <summary>枚举到第 N 个文件时触发取消，用来模拟「扫描途中取消」。</summary>
    private sealed class CancelAfterFileSystem : IFileSystem
    {
        private readonly IFileSystem _inner;
        private readonly CancellationTokenSource _cancellation;
        private readonly int _cancelAfter;

        public CancelAfterFileSystem(IFileSystem inner, CancellationTokenSource cancellation, int cancelAfter)
        {
            _inner = inner;
            _cancellation = cancellation;
            _cancelAfter = cancelAfter;
        }

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive)
        {
            var count = 0;
            foreach (var file in _inner.EnumerateFiles(directory, searchPattern, recursive))
            {
                count++;
                if (count >= _cancelAfter && !_cancellation.IsCancellationRequested)
                {
                    _cancellation.Cancel();
                }

                yield return file;
            }
        }

        public bool FileExists(string path) => _inner.FileExists(path);

        public long GetFileSize(string path) => _inner.GetFileSize(path);

        public bool DirectoryExists(string path) => _inner.DirectoryExists(path);

        public string ReadAllText(string path) => _inner.ReadAllText(path);

        public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);

        public Stream OpenRead(string path) => _inner.OpenRead(path);

        public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data) => _inner.WriteAllBytesAtomic(path, data);

        public void WriteAllTextAtomic(string path, string text) => _inner.WriteAllTextAtomic(path, text);

        public IEnumerable<string> EnumerateDirectories(string directory) => _inner.EnumerateDirectories(directory);

        public string Combine(params string[] parts) => _inner.Combine(parts);

        public string GetFullPath(string path) => _inner.GetFullPath(path);

        public string? GetDirectoryName(string path) => _inner.GetDirectoryName(path);

        public string GetFileName(string path) => _inner.GetFileName(path);

        public void EnsureDirectory(string path) => _inner.EnsureDirectory(path);

        public void Delete(string path) => _inner.Delete(path);

        public void Copy(string source, string destination, bool overwrite) => _inner.Copy(source, destination, overwrite);

        public void Move(string source, string destination, bool overwrite) => _inner.Move(source, destination, overwrite);

        public void MoveDirectory(string source, string destination) => _inner.MoveDirectory(source, destination);
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
