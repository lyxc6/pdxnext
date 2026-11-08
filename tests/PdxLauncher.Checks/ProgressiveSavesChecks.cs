namespace PdxLauncher.Checks;

using System.Collections.ObjectModel;

using PdxLauncher.App.Services;
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
            CheckAutomationWatcher();
            CheckCancellationDuringDetails();
            CheckConcurrencyLimit();
            CheckOldGenerationDropped();
            CheckProgressCountsSuccessesOnly();
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

        // 时间触发：小库只有 1 条时不能死等满 4 条；满一批或后台完成也应立即回填。
        ChecksContext.Check("0 条待回填不触发", !SaveScanBatching.ShouldFlush(0, TimeSpan.FromSeconds(1), false));
        ChecksContext.Check("满 4 条立即回填", SaveScanBatching.ShouldFlush(4, TimeSpan.Zero, false));
        ChecksContext.Check("不足 4 条且未到 50 ms 时等待", !SaveScanBatching.ShouldFlush(3, TimeSpan.FromMilliseconds(49), false));
        ChecksContext.Check("不足 4 条但到 50 ms 时回填", SaveScanBatching.ShouldFlush(3, TimeSpan.FromMilliseconds(50), false));
        ChecksContext.Check("后台完成时立即回填剩余前缀", SaveScanBatching.ShouldFlush(1, TimeSpan.Zero, true));

        var intervalThrew = false;
        try
        {
            SaveScanBatching.ShouldFlush(1, TimeSpan.Zero, false, flushIntervalMilliseconds: -1);
        }
        catch (ArgumentOutOfRangeException)
        {
            intervalThrew = true;
        }

        ChecksContext.Check("负刷新间隔抛 ArgumentOutOfRangeException", intervalThrew);
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
        var viewModel = new SavesViewModel(index, NoBackupService.Instance, new NullLogger());

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
        var viewModel = new SavesViewModel(new HangingIndex(), NoBackupService.Instance, new NullLogger());
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

    /// <summary>读屏名监听：第二段补细节后 ListItem 必须被重写成新名字；退订后不再回调。</summary>
    private static void CheckAutomationWatcher()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 细节补齐后读屏名同步到列表项");
        var file = Entry("sample0.ck3");
        var row = new SaveGameRowViewModel(file, 0);
        var names = new List<string>();
        var watcher = new SaveRowAutomationWatcher();

        watcher.Track(row, names.Add);
        ChecksContext.Equal("跟踪后立即写一次读屏名", 1, names.Count);
        ChecksContext.Check("骨架读屏名包含文件名", names[0].Contains("sample0.ck3", StringComparison.Ordinal), names[0]);
        var skeletonName = names[0];

        row.ApplyDetail(Meta(file, "Detail Realm", SaveParseState.Ok));
        ChecksContext.Equal("细节回填触发第二次命名", 2, names.Count);
        ChecksContext.Check("读屏名已从骨架更新为含细节", names[1] != skeletonName, names[1]);
        ChecksContext.Check("新读屏名包含细节", row.HasDetails && names[1].Length > skeletonName.Length, names[1]);

        watcher.Untrack(row);
        ChecksContext.Equal("退订后跟踪表清空", 0, watcher.Count);
        var nameCountAfterUntrack = names.Count;
        row.ApplyDetail(Meta(file, "迟到的细节", SaveParseState.Degraded));
        ChecksContext.Equal("退订后不再回调", nameCountAfterUntrack, names.Count);
    }

    /// <summary>细节阶段取消：半截骨架不能留在页面上，进度与忙碌态一起收尾。</summary>
    private static void CheckCancellationDuringDetails()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 细节阶段取消后清空半截行");
        var files = Files(SampleCount);
        var index = new GateIndex(new SaveFileScanResult(files, [Location()], false));
        var viewModel = new SavesViewModel(index, NoBackupService.Instance, new NullLogger());
        using var cancellation = new CancellationTokenSource();

        var load = viewModel.LoadAsync(Game("C:\\game"), cancellation.Token);
        var appeared = SpinWait.SpinUntil(() => viewModel.Saves.Count == SampleCount, TimeSpan.FromSeconds(10));
        ChecksContext.Check("取消前骨架已铺出", appeared, "Saves=" + viewModel.Saves.Count);

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
        finally
        {
            index.Release();
        }

        ChecksContext.Check("细节阶段取消抛出 OperationCanceledException", cancelled);
        ChecksContext.Equal("取消后不残留骨架行", 0, viewModel.Saves.Count);
        ChecksContext.Equal("取消后进度归零", 0, viewModel.ProcessedCount);
        ChecksContext.Check("取消后不显示忙碌态", !viewModel.IsBusy);
    }

    /// <summary>第二段并发峰值不超过 4，且确实并行（至少 2 条同时解析）。</summary>
    private static void CheckConcurrencyLimit()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 第二段并发峰值不超过 4");
        var files = Files(16);
        var index = new ConcurrencyIndex(new SaveFileScanResult(files, [Location()], false));
        var viewModel = new SavesViewModel(index, NoBackupService.Instance, new NullLogger());

        viewModel.LoadAsync(Game("C:\\game"), CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Check("并发峰值不超过上限 4", index.MaxActive <= 4, "peak=" + index.MaxActive);
        ChecksContext.Check("确实发生了并行解析", index.MaxActive >= 2, "peak=" + index.MaxActive);
        ChecksContext.Equal("16 条全部解析", 16, index.ParseCalls);
        ChecksContext.Equal("16 条全部回填", 16, viewModel.ProcessedCount);
    }

    /// <summary>切游戏后旧代次的第一段结果必须整批丢弃，不能覆盖新列表。</summary>
    private static void CheckOldGenerationDropped()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 旧代次结果整批丢弃");
        var index = new GenerationIndex();
        var viewModel = new SavesViewModel(index, NoBackupService.Instance, new NullLogger());

        var first = viewModel.LoadAsync(Game("C:\\game"), CancellationToken.None);
        var second = viewModel.LoadAsync(Game("C:\\game"), CancellationToken.None);
        second.GetAwaiter().GetResult();

        ChecksContext.Equal("第二代先完成并出 2 行", 2, viewModel.Saves.Count);
        ChecksContext.Equal("列表属于第二代", "second0.ck3", viewModel.Saves[0].FileName);

        index.ReleaseFirst();
        first.GetAwaiter().GetResult();

        ChecksContext.Equal("旧代次完成后行数不变", 2, viewModel.Saves.Count);
        ChecksContext.Equal("旧代次没有覆盖第一行", "second0.ck3", viewModel.Saves[0].FileName);
        ChecksContext.Equal("旧代次没有覆盖第二行", "second1.ck3", viewModel.Saves[1].FileName);
        ChecksContext.Check("旧代次 finally 没有留下忙碌态", !viewModel.IsBusy);
        ChecksContext.Check("状态仍是第二代的完成态", viewModel.StatusText.StartsWith("saves.status.lastScan", StringComparison.Ordinal), viewModel.StatusText);
    }

    /// <summary>解析失败的行不算进度：5 条成功时显示 5 / 6，失败行保持骨架。</summary>
    private static void CheckProgressCountsSuccessesOnly()
    {
        Console.WriteLine("[ProgressiveSavesChecks] 进度只统计解析成功的行");
        var files = Files(SampleCount);
        var index = new FlakyIndex(new SaveFileScanResult(files, [Location()], false), "sample2.ck3");
        var viewModel = new SavesViewModel(index, NoBackupService.Instance, new NullLogger());

        viewModel.LoadAsync(Game("C:\\game"), CancellationToken.None).GetAwaiter().GetResult();

        ChecksContext.Equal("失败行仍保留骨架", SampleCount, viewModel.Saves.Count);
        ChecksContext.Equal("进度只记 5 条成功", SampleCount - 1, viewModel.ProcessedCount);
        ChecksContext.Equal("总数仍是 6", SampleCount, viewModel.TotalCount);
        ChecksContext.Equal("成功行补上游戏内日期", "1066.9.15", viewModel.Saves[0].DateText);
        ChecksContext.Check("失败行保留文件时间回退", viewModel.Saves[2].DateText != "1066.9.15", viewModel.Saves[2].DateText);
        ChecksContext.Check("完成后仍是完成态", !viewModel.IsBusy);
    }

    private static SaveFileEntry Entry(string fileName, long size = 100) => SaveFileEntry.TryCreate(
        "ck3",
        "C:\\saves\\" + fileName,
        size,
        new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
        SaveContainer.TextMeta)!;

    private static SaveFileEntry[] Files(int count) =>
        Enumerable.Range(0, count).Select(i => Entry("sample" + i + ".ck3", 100 + i)).ToArray();

    private static SaveLocationInfo Location() => new("ck3", "C:\\saves", true, SaveLocationSource.Documents);

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

    /// <summary>第一段立即返回，细节解析全部卡在闸门上；用于取消时验证半截行清理。</summary>
    private sealed class GateIndex(SaveFileScanResult result) : ISaveGameFileIndex
    {
        private readonly ManualResetEventSlim _gate = new(false);

        public Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public SaveGameMeta ParseFile(SaveFileEntry entry)
        {
            _gate.Wait();
            return Meta(entry, "Detail " + entry.FileName, SaveParseState.Ok);
        }

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => result.Locations;

        public void Release() => _gate.Set();
    }

    /// <summary>记录并发峰值；每条 sleep 一小会儿，让并行度确实跑满。</summary>
    private sealed class ConcurrencyIndex(SaveFileScanResult result) : ISaveGameFileIndex
    {
        private readonly object _sync = new();
        private int _active;
        private int _maxActive;
        private int _parseCalls;

        public int MaxActive
        {
            get
            {
                lock (_sync)
                {
                    return _maxActive;
                }
            }
        }

        public int ParseCalls => Volatile.Read(ref _parseCalls);

        public Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public SaveGameMeta ParseFile(SaveFileEntry entry)
        {
            Interlocked.Increment(ref _parseCalls);
            lock (_sync)
            {
                _active++;
                _maxActive = Math.Max(_maxActive, _active);
            }

            try
            {
                Thread.Sleep(20);
                return Meta(entry, "Detail " + entry.FileName, SaveParseState.Ok);
            }
            finally
            {
                lock (_sync)
                {
                    _active--;
                }
            }
        }

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => result.Locations;
    }

    /// <summary>第一次扫描挂在闸门上，第二次立即返回第二组文件。</summary>
    private sealed class GenerationIndex : ISaveGameFileIndex
    {
        private readonly TaskCompletionSource<SaveFileScanResult> _first =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        private readonly SaveFileScanResult _second = new(
            [Entry("second0.ck3"), Entry("second1.ck3")],
            [Location()],
            false);

        private int _calls;

        public Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _calls) == 1)
            {
                return _first.Task;
            }

            return Task.FromResult(_second);
        }

        public SaveGameMeta ParseFile(SaveFileEntry entry) =>
            Meta(entry, "Second Detail " + entry.FileName, SaveParseState.Ok);

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => [Location()];

        public void ReleaseFirst() => _first.TrySetResult(
            new SaveFileScanResult([Entry("first0.ck3"), Entry("first1.ck3")], [Location()], false));
    }

    /// <summary>指定文件名解析失败，其余成功。</summary>
    private sealed class FlakyIndex(SaveFileScanResult result, string failingFileName) : ISaveGameFileIndex
    {
        public Task<SaveFileScanResult> ScanFilesAsync(IEnumerable<string> gameIds, CancellationToken cancellationToken) =>
            Task.FromResult(result);

        public SaveGameMeta ParseFile(SaveFileEntry entry)
        {
            if (string.Equals(entry.FileName, failingFileName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("坏档");
            }

            return Meta(entry, "Detail " + entry.FileName, SaveParseState.Ok);
        }

        public IReadOnlyList<SaveLocationInfo> GetLocations(string gameId) => result.Locations;
    }

    /// <summary>两段式加载的自检只走「读」路径，备份段留空；写操作由 SavesActionsChecks 覆盖。</summary>
    private sealed class NoBackupService : ISaveGameBackupService
    {
        public static readonly NoBackupService Instance = new();

        public string BackupRootDirectory => Path.Combine(Path.GetTempPath(), "pdx-checks-saves-backups");

        public SaveBackupPolicy Policy { get; private set; } = SaveBackupPolicy.Default;

        public void ApplyPolicy(SaveBackupPolicy policy) => Policy = policy;

        public Task<SaveBackupResult> BackupAsync(
            string gameId,
            string sourcePath,
            SaveGameMeta? meta,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SaveBackupResult(SaveBackupStatus.Failed, "自检未使用", null, null));

        public Task<SaveRestoreResult> RestoreAsync(
            SaveBackupEntry entry,
            bool overwrite,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SaveRestoreResult(SaveRestoreStatus.Failed, "自检未使用", null, null));

        public Task<SaveDeleteResult> DeleteBackupAsync(
            SaveBackupEntry entry,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SaveDeleteResult(SaveDeleteStatus.Failed, "自检未使用", false, null));

        public Task<SaveDeleteResult> DeleteSaveAsync(
            string gameId,
            string savePath,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SaveDeleteResult(SaveDeleteStatus.Failed, "自检未使用", false, null));

        public Task<SaveBackupUsage> GetUsageAsync(CancellationToken cancellationToken) =>
            Task.FromResult(SaveBackupUsage.Empty);

        public Task<IReadOnlyList<SaveBackupEntry>> ListBackupsAsync(
            string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SaveBackupEntry>>([]);

        public Task<SaveBackupPruneResult> PruneAsync(
            string gameId,
            int keepNewest,
            CancellationToken cancellationToken) =>
            Task.FromResult(new SaveBackupPruneResult(
                SaveBackupStatus.Failed,
                "自检未使用",
                0,
                0,
                SaveBackupUsage.Empty));
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
