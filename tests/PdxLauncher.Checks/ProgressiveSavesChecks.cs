namespace PdxLauncher.Checks;

using System.Collections.ObjectModel;

using PdxLauncher.App.ViewModels;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Saves;

/// <summary>
/// 自检：两段式存档加载。第一段只枚举文件（不读内容），第二段按批原位补细节。
/// <para>
/// 这里的样本全部在临时目录里程序化生成（每个 ≤ 8 KB），绝不复制或读取真机存档。
/// 三条最关键的断言能随变异变红：
/// <list type="number">
///   <item>第一段先把 N 行铺出来（若把解析前置，行数会停在 0）；</item>
///   <item>第二段补完后行对象引用不变（若整表重建，引用比较失败）；</item>
///   <item>已填好的行拒绝二次覆盖（若允许覆盖，Ok 会被后面的 Degraded 回退）。</item>
/// </list>
/// </para>
/// </summary>
public static class ProgressiveSavesChecks
{
    private const int SampleCount = 6;

    /// <summary>自检入口（由 Program.cs 反射发现）。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        var root = PlaysetChecks.CreateTempRoot(fs, "saves-progressive");
        try
        {
            CheckFileEntry();
            CheckBatching();
            CheckRowState();

            var documents = Path.Combine(root, "Documents");
            var catalog = new SaveLocationCatalog(fs, documents, Path.Combine(root, "Local"), Path.Combine(root, "LocalLow"));
            CheckFirstPhaseDoesNotReadContent(fs, catalog, documents);
            CheckSecondPhaseReusesParser(fs, catalog, documents);
            CheckViewModelProgressiveFill();
            CheckCancellation();
        }
        finally
        {
            PlaysetChecks.TryDelete(fs, root);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckFileEntry()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 粗略行规范化");
        var created = SaveFileEntry.TryCreate(
            "ck3",
            Path.Combine("saves", "Realm.ck3"),
            1024,
            new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            SaveContainer.TextMeta);
        ChecksContext.Equal("游戏 id 归一", "ck3", created?.GameId ?? "<null>");
        ChecksContext.Equal("文件名为纯文件名", "Realm.ck3", created?.FileName ?? "<null>");
        ChecksContext.Equal("大小原样保留", 1024L, created?.FileSize ?? -1);
        ChecksContext.Equal("时间原样保留", new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), created?.LastWriteUtc ?? DateTimeOffset.MinValue);

        var clamped = SaveFileEntry.TryCreate("ck3", "a.ck3", -5, DateTimeOffset.MinValue, SaveContainer.TextMeta);
        ChecksContext.Equal("负大小按 0 处理", 0L, clamped?.FileSize ?? -1);
        ChecksContext.Check("空路径返回 null", SaveFileEntry.TryCreate("ck3", "  ", 1, DateTimeOffset.MinValue, SaveContainer.TextMeta) is null);
        ChecksContext.Check("空游戏 id 返回 null", SaveFileEntry.TryCreate(" ", "a.ck3", 1, DateTimeOffset.MinValue, SaveContainer.TextMeta) is null);
    }

    private static void CheckBatching()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 分批分组是纯函数");
        var items = Enumerable.Range(0, SampleCount).ToArray();
        var batches = SaveScanBatching.Split(items);
        ChecksContext.Equal("6 条按 4 条一批分成 2 批", 2, batches.Count);
        ChecksContext.Equal("第一批起始下标 0", 0, batches[0].StartIndex);
        ChecksContext.Equal("第二批起始下标 4", 4, batches[1].StartIndex);
        ChecksContext.Equal("第一批 4 条", 4, batches[0].Items.Count);
        ChecksContext.Equal("第二批 2 条", 2, batches[1].Items.Count);
        ChecksContext.Equal("批次展开后与原序一致", "0,1,2,3,4,5", string.Join(",", batches.SelectMany(static batch => batch.Items)));

        var empty = SaveScanBatching.Split(Array.Empty<int>());
        ChecksContext.Equal("空列表不产生批次", 0, empty.Count);

        var threw = false;
        try
        {
            SaveScanBatching.Split(items, 0);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        ChecksContext.Check("批大小 0 抛 ArgumentOutOfRangeException", threw);
    }

    private static void CheckRowState()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 一行只从「无细节」补到「有细节」一次");
        var file = SaveFileEntry.TryCreate("ck3", "a.ck3", 10, DateTimeOffset.MinValue, SaveContainer.TextMeta)!;
        var state = SaveRowState.FromFile(file);
        ChecksContext.Check("初始没有细节", !state.HasDetail);

        var first = Meta(file, "打开存档", SaveParseState.Ok);
        var filled = state.WithDetail(first);
        ChecksContext.Check("首次补细节生效", filled.HasDetail);
        ChecksContext.Equal("首次细节被保留", "打开存档", filled.Detail?.SaveName ?? "<null>");

        var second = Meta(file, "迟到的降级结果", SaveParseState.Degraded);
        var afterSecond = filled.WithDetail(second);
        ChecksContext.Equal("二次补细节不覆盖成功结果", "打开存档", afterSecond.Detail?.SaveName ?? "<null>");
        ChecksContext.Equal("二次补细节不回退解析状态", SaveParseState.Ok, afterSecond.Detail?.ParseState ?? SaveParseState.Degraded);
        ChecksContext.Check("二次补细节返回同一实例", afterSecond.Equals(filled));
    }

    private static void CheckFirstPhaseDoesNotReadContent(IFileSystem fs, SaveLocationCatalog catalog, string documents)
    {
        Console.WriteLine("[ProgressiveSavesChecks] 第一段只枚举，不读文件内容");
        var directory = Path.Combine(documents, "Paradox Interactive", "Crusader Kings III", "save games");
        fs.EnsureDirectory(directory);
        for (var i = 0; i < SampleCount; i++)
        {
            fs.WriteAllTextAtomic(Path.Combine(directory, "sample" + i + ".ck3"), SampleText("Realm " + i));
        }

        fs.WriteAllTextAtomic(Path.Combine(directory, "ignored.txt"), "wrong extension");
        var guard = new NoContentReadFileSystem(fs);
        var scanner = new SaveGameScanner(guard, new NullLogger(), catalog);

        SaveFileScanResult result;
        try
        {
            result = Await(scanner.ScanFilesAsync(["ck3"], CancellationToken.None));
        }
        catch (Exception exception)
        {
            ChecksContext.Check("第一段不应打开文件内容", false, exception.GetType().Name + ": " + exception.Message);
            return;
        }

        ChecksContext.Equal("第一段只收集白名单扩展名", SampleCount, result.Files.Count);
        ChecksContext.Check("第一段的落点存在", result.Locations.Count == 1 && result.Locations[0].Exists);
        ChecksContext.Check("第一段没有截断", !result.Truncated);
        ChecksContext.Equal("第一段按文件名稳定排序", "sample0.ck3", result.Files[0].FileName);
        ChecksContext.Check("第一段拿到非零文件大小", result.Files[0].FileSize > 0);
        ChecksContext.Check(
            "第一段拿到文件修改时间",
            result.Files[0].LastWriteUtc != DateTimeOffset.MinValue,
            result.Files[0].LastWriteUtc.ToString("O"));
    }

    private static void CheckSecondPhaseReusesParser(IFileSystem fs, SaveLocationCatalog catalog, string documents)
    {
        Console.WriteLine("[ProgressiveSavesChecks] 第二段复用现有解析器");
        var reader = new SavContainerReader(fs, new NullLogger());
        var scanner = new SaveGameScanner(fs, new NullLogger(), catalog, reader);
        var result = Await(scanner.ScanFilesAsync(["ck3"], CancellationToken.None));
        ChecksContext.Equal("第二段输入仍为 6 条", SampleCount, result.Files.Count);

        var parsed = scanner.ParseFile(result.Files[0]);
        ChecksContext.Equal("解析出存档名", "Test Realm", parsed.SaveName ?? "<null>");
        ChecksContext.Equal("解析出国家", "England", parsed.PlayerCountry ?? "<null>");
        ChecksContext.Equal("解析出游戏内日期", "1066.9.15", parsed.GameDate ?? "<null>");
        ChecksContext.Equal("解析出游戏版本", "1.13.0", parsed.GameVersion ?? "<null>");
        ChecksContext.Equal("解析成功而非降级", SaveParseState.Ok, parsed.ParseState);
        ChecksContext.Equal("解析器保留枚举到的大小", result.Files[0].FileSize, parsed.FileSize);
    }

    private static void CheckViewModelProgressiveFill()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 第一段先铺行，第二段原位补细节");
        var files = Enumerable.Range(0, SampleCount)
            .Select(i => SaveFileEntry.TryCreate(
                "ck3",
                "C:\\saves\\sample" + i + ".ck3",
                100 + i,
                DateTimeOffset.MinValue,
                SaveContainer.TextMeta)!)
            .ToArray();
        var index = new BlockingIndex(new SaveFileScanResult(files, [new SaveLocationInfo("ck3", "C:\\saves", true, SaveLocationSource.Documents)], false));
        var viewModel = new SavesViewModel(index, new NullLogger());

        var load = viewModel.LoadAsync(Game("C:\\game"), CancellationToken.None);
        var appeared = SpinWait.SpinUntil(
            () => viewModel.Saves.Count == SampleCount &&
                  viewModel.StatusText.StartsWith("saves.status.reading", StringComparison.Ordinal),
            TimeSpan.FromSeconds(10));
        ChecksContext.Check("细节被阻塞时第一段已铺出全部行", appeared, "Saves=" + viewModel.Saves.Count);
        ChecksContext.Check("此时显示「正在读取」进度", viewModel.StatusText.StartsWith("saves.status.reading", StringComparison.Ordinal), viewModel.StatusText);
        ChecksContext.Check("此时不显示假空状态", !viewModel.ShowEmptyState);
        ChecksContext.Equal("第一段显示文件名", "sample0.ck3", viewModel.Saves.Count > 0 ? viewModel.Saves[0].FileName : "<none>");
        ChecksContext.Check("第一段显示文件大小回退", viewModel.Saves.Count > 0 && viewModel.Saves[0].SizeText.Length > 0);

        var rowsBefore = viewModel.Saves.ToArray();
        index.Release();
        load.GetAwaiter().GetResult();

        ChecksContext.Equal("完成后仍是 6 行", SampleCount, viewModel.Saves.Count);
        var sameRows = true;
        for (var i = 0; i < SampleCount; i++)
        {
            sameRows &= ReferenceEquals(rowsBefore[i], viewModel.Saves[i]);
        }

        ChecksContext.Check("第二段原位回填、未整表重建", sameRows);
        ChecksContext.Equal("细节补上了游戏内日期", "1066.9.15", viewModel.Saves[0].DateText);
        ChecksContext.Equal("细节补上了版本", "saves.version:1.13.0", viewModel.Saves[0].VersionText);
        ChecksContext.Check("完成后不再是忙碌态", !viewModel.IsBusy);
        ChecksContext.Check("完成后显示上次扫描时间", viewModel.StatusText.StartsWith("saves.status.lastScan", StringComparison.Ordinal), viewModel.StatusText);
        ChecksContext.Equal("每行只解析一次", SampleCount, index.ParseCalls);
    }

    private static void CheckCancellation()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 切游戏/取消时旧任务不写回");
        using var cancellation = new CancellationTokenSource();
        var viewModel = new SavesViewModel(new HangingIndex(), new NullLogger());
        var load = viewModel.LoadAsync(Game("C:\\game"), cancellation.Token);
        cancellation.Cancel();

        var cancelled = false;
        try
        {
            load.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        ChecksContext.Check("第一段被取消时抛出 OperationCanceledException", cancelled);
        ChecksContext.Equal("取消后不留下半截行", 0, viewModel.Saves.Count);
        ChecksContext.Check("取消后不显示忙碌态", !viewModel.IsBusy);
    }

    private static string SampleText(string title) =>
        "SAV0\nmeta_data={\n" +
        "version=\"1.13.0\"\n" +
        "meta_date=\"1066.9.15\"\n" +
        "meta_title_name=\"Test Realm\"\n" +
        "meta_country=\"England\"\n" +
        "meta_player_name=\"Player\"\n" +
        "}\n" +
        "gamestate={title=\"" + title + "\"}\n";

    private static SaveGameMeta Meta(SaveFileEntry file, string saveName, SaveParseState state) => new(
        file.FilePath,
        file.FileName,
        file.GameId,
        file.Container,
        saveName,
        "1066.9.15",
        "1.13.0",
        "Player",
        false,
        "England",
        file.FileSize,
        file.LastWriteUtc,
        state,
        state == SaveParseState.Degraded ? "test" : null);

    private static GameInstallation Game(string installRoot) => new(
        GameId: "ck3",
        DisplayName: "Crusader Kings III",
        Version: "test",
        InstallRoot: installRoot,
        LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: Path.Combine(installRoot, "game"),
        DlcPath: Path.Combine(installRoot, "dlc"),
        ModDir: Path.Combine(installRoot, "mod"),
        ExePath: Path.Combine(installRoot, "binaries", "ck3.exe"),
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
        SteamAppId: 1158310,
        Support: GameSupport.LaunchOnly);

    private static T Await<T>(Task<T> task) => task.GetAwaiter().GetResult();

    /// <summary>委托全部元数据操作，但任何内容读取都直接失败：用来证明第一段零内容读取。</summary>
    private sealed class NoContentReadFileSystem(IFileSystem inner) : IFileSystem
    {
        public bool FileExists(string path) => inner.FileExists(path);

        public long GetFileSize(string path) => inner.GetFileSize(path);

        public bool DirectoryExists(string path) => inner.DirectoryExists(path);

        public string ReadAllText(string path) => throw new InvalidOperationException("第一段不应读文本内容: " + path);

        public byte[] ReadAllBytes(string path) => throw new InvalidOperationException("第一段不应读字节内容: " + path);

        public Stream OpenRead(string path) => throw new InvalidOperationException("第一段不应打开文件流: " + path);

        public void WriteAllBytesAtomic(string path, ReadOnlySpan<byte> data) => inner.WriteAllBytesAtomic(path, data);

        public void WriteAllTextAtomic(string path, string text) => inner.WriteAllTextAtomic(path, text);

        public IEnumerable<string> EnumerateFiles(string directory, string searchPattern, bool recursive) =>
            inner.EnumerateFiles(directory, searchPattern, recursive);

        public IEnumerable<string> EnumerateDirectories(string directory) => inner.EnumerateDirectories(directory);

        public string Combine(params string[] parts) => inner.Combine(parts);

        public string GetFullPath(string path) => inner.GetFullPath(path);

        public string? GetDirectoryName(string path) => inner.GetDirectoryName(path);

        public string GetFileName(string path) => inner.GetFileName(path);

        public void EnsureDirectory(string path) => inner.EnsureDirectory(path);

        public void Delete(string path) => inner.Delete(path);

        public void Copy(string source, string destination, bool overwrite) => inner.Copy(source, destination, overwrite);

        public void Move(string source, string destination, bool overwrite) => inner.Move(source, destination, overwrite);

        public void MoveDirectory(string source, string destination) => inner.MoveDirectory(source, destination);
    }

    /// <summary>第一段立即返回，细节解析卡在闸门上；闸门放开前 UI 应已看到全部粗略行。</summary>
    private sealed class BlockingIndex(SaveFileScanResult result) : ISaveGameFileIndex
    {
        private readonly TaskCompletionSource<bool> _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _parseCalls;

        public int ParseCalls => Volatile.Read(ref _parseCalls);

        public Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public SaveGameMeta ParseFile(SaveFileEntry entry)
        {
            Interlocked.Increment(ref _parseCalls);
            _gate.Task.GetAwaiter().GetResult();
            return Meta(entry, "Detail " + entry.FileName, SaveParseState.Ok);
        }

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => result.Locations;

        public void Release() => _gate.TrySetResult(true);
    }

    /// <summary>第一段永远不完成，只响应取消。</summary>
    private sealed class HangingIndex : ISaveGameFileIndex
    {
        public async Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken).ConfigureAwait(false);
            return new SaveFileScanResult([], [], false);
        }

        public SaveGameMeta ParseFile(SaveFileEntry entry) => throw new InvalidOperationException("不应解析");

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => [];
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
