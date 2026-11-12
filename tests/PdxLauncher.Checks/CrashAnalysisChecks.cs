namespace PdxLauncher.Checks;

using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.CrashAnalysis;
using PdxLauncher.Core.Infrastructure;
using PdxLauncher.Core.LoadFiles;

/// <summary>
/// 「崩溃分析」页的静态自检 + 分析器行为自检。
/// 行为 fixture 全部写到临时目录，不使用本机真实游戏日志。
/// </summary>
public static class CrashAnalysisChecks
{
    private static readonly string[] AutomationIds =
    [
        "pageCrashAnalysis",
        "lstCrashSections",
        "navCrashOverview",
        "navCrashCrashes",
        "navCrashLogs",
        "navCrashLaunch",
        "btnCrashRefresh",
        "btnCrashCopy",
        "btnCrashOpenCrash",
        "btnCrashOpenLogs",
        "infCrashCritical",
        "infCrashWarning",
        "infCrashClear",
        "infCrashNoGame",
        "infCrashNoReport",
        "lstCrashSummary",
        "lstCrashFindings",
        "lstCrashMods",
        "expCrashMods",
        "lstCrashBundles",
        "lstCrashLaunch",
        "lstCrashGroups",
        "cmbCrashLogFile",
        "tglCrashOnlyErrors",
        "txtCrashLogTail",
        "txtCrashStatus",
    ];

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[CrashAnalysisChecks] 崩溃分析页");

        var root = FindRepositoryRoot();
        if (root is null)
        {
            ChecksContext.Check("找到仓库根目录（version.props）", false);
            return 1;
        }

        CheckStaticFiles(root);
        CheckAnalyzerFixture(fs);
        CheckNoCrashFallback(fs);
        CheckCorruptSettings(fs);
        CheckLogReader(fs);
        CheckCrossLogDedupe();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 静态检查

    private static void CheckStaticFiles(string root)
    {
        var xaml = Read(root, Path.Combine("src", "PdxLauncher.App", "Views", "CrashAnalysisPage.xaml"));
        var codeBehind = Read(root, Path.Combine("src", "PdxLauncher.App", "Views", "CrashAnalysisPage.xaml.cs"));
        var viewModel = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "CrashAnalysisViewModel.cs"));
        var rows = Read(root, Path.Combine("src", "PdxLauncher.App", "ViewModels", "CrashAnalysisRows.cs"));
        var navigation = Read(root, Path.Combine("src", "PdxLauncher.App", "Services", "NavigationService.cs"));
        var mainWindow = Read(root, Path.Combine("src", "PdxLauncher.App", "MainWindow.xaml"));
        var settings = Read(root, Path.Combine("src", "PdxLauncher.Core", "Configuration", "AppSettings.cs"));
        var zhTable = I18nResw.Read(root, "zh-CN");
        var enTable = I18nResw.Read(root, "en-US");
        var logReader = Read(root, Path.Combine("src", "PdxLauncher.Core", "CrashAnalysis", "CrashLogReader.cs"));

        ChecksContext.Check("页面 XAML 存在", xaml.Length > 0);
        ChecksContext.Check("页面代码后置存在", codeBehind.Length > 0);
        ChecksContext.Check("视图模型存在", viewModel.Length > 0);
        ChecksContext.Check("行模型与文案映射存在", rows.Length > 0);

        ChecksContext.Check("NavigationService 定义 crashanalysis 标签",
            navigation.Contains("CrashAnalysisTag = \"crashanalysis\"", StringComparison.Ordinal));
        ChecksContext.Check("NavigationService 映射到 CrashAnalysisPage",
            navigation.Contains("PdxLauncher.App.Views.CrashAnalysisPage", StringComparison.Ordinal));
        ChecksContext.Check("页脚有崩溃分析入口",
            mainWindow.Contains("AutomationProperties.AutomationId=\"navCrashAnalysis\"", StringComparison.Ordinal));
        ChecksContext.Check("页脚入口用 Tag=\"crashanalysis\"",
            mainWindow.Contains("Tag=\"crashanalysis\"", StringComparison.Ordinal));

        var crashIndex = mainWindow.IndexOf("navCrashAnalysis", StringComparison.Ordinal);
        var diagnosticsIndex = mainWindow.IndexOf("AutomationProperties.AutomationId=\"navDiagnostics\"", StringComparison.Ordinal);
        ChecksContext.Check("崩溃分析排在性能与日志之前", crashIndex >= 0 && diagnosticsIndex > crashIndex);

        foreach (var automationId in AutomationIds)
        {
            ChecksContext.Check(
                "页面包含自动化 id " + automationId,
                xaml.Contains("AutomationId=\"" + automationId + "\"", StringComparison.Ordinal));
        }

        string[] sections = ["secOverview", "secCrashes", "secLogs", "secLaunch"];
        foreach (var section in sections)
        {
            ChecksContext.Check("页面包含分区 " + section,
                xaml.Contains("x:Name=\"" + section + "\"", StringComparison.Ordinal));
        }

        var tagOrder = new[] { "Tag=\"0\"", "Tag=\"1\"", "Tag=\"2\"", "Tag=\"3\"" }
            .Select(tag => xaml.IndexOf(tag, StringComparison.Ordinal))
            .ToArray();
        ChecksContext.Check("四个左栏导航项按 0..3 排列",
            tagOrder.All(index => index >= 0) && tagOrder.SequenceEqual(tagOrder.Order()));

        ChecksContext.Check("页面 XAML 无硬编码中文（注释除外）",
            !Regex.IsMatch(StripComments(xaml), @"[\p{IsCJKUnifiedIdeographs}]"));

        var modPanel = SliceBetween(
            xaml,
            "<ItemsControl x:Name=\"lstCrashMods\"",
            "</ItemsControl>");
        ChecksContext.Check("Mod 卡片用 PdxWrapPanel 自动换行",
            modPanel.Contains("<controls:PdxWrapPanel Spacing=\"8\"", StringComparison.Ordinal));
        ChecksContext.Check("Mod 卡片固定宽度以参与换行",
            SliceBetween(xaml, "<DataTemplate x:Key=\"CrashModRowTemplate\"", "</DataTemplate>")
                .Contains("Width=\"320\"", StringComparison.Ordinal));
        ChecksContext.Check("Mod 卡片行距交给 WrapPanel（不再用 Margin 堆叠）",
            !SliceBetween(xaml, "<DataTemplate x:Key=\"CrashModRowTemplate\"", "</DataTemplate>")
                .Contains("Margin=\"0,0,0,8\"", StringComparison.Ordinal));

        ChecksContext.Check("日志选择器使用统一下拉 PdxDropDown",
            xaml.Contains("<controls:PdxDropDown", StringComparison.Ordinal)
            && !xaml.Contains("<ComboBox", StringComparison.Ordinal));
        ChecksContext.Check("日志选择器绑定日志列表",
            xaml.Contains("ItemsSource=\"{x:Bind ViewModel.LogFiles, Mode=OneWay}\"", StringComparison.Ordinal));
        ChecksContext.Check("日志选择器双向绑定当前日志项",
            xaml.Contains("SelectedItem=\"{x:Bind ViewModel.SelectedLogFile, Mode=TwoWay}\"", StringComparison.Ordinal));
        ChecksContext.Check("视图模型暴露 SelectedLogFile",
            viewModel.Contains("public CrashLogFileRowViewModel? SelectedLogFile", StringComparison.Ordinal));
        ChecksContext.Check("代码后置不再手动同步 ComboBox",
            !codeBehind.Contains("ComboBox", StringComparison.Ordinal)
            && !codeBehind.Contains("SyncLogSelection", StringComparison.Ordinal));

        ChecksContext.Check("页面用 ShellLauncher 打开目录",
            codeBehind.Contains("ShellLauncher.TryStart", StringComparison.Ordinal));
        ChecksContext.Check("页面用 Clipboard 复制报告",
            codeBehind.Contains("Clipboard.SetContent", StringComparison.Ordinal));
        ChecksContext.Check("页面切换游戏后重新分析",
            codeBehind.Contains("CurrentGameChanged", StringComparison.Ordinal));
        ChecksContext.Check("视图模型在后台线程分析",
            viewModel.Contains("Task.Run", StringComparison.Ordinal));
        ChecksContext.Check("视图模型从 AppServices 取分析器",
            viewModel.Contains("AppServices.CrashAnalysis", StringComparison.Ordinal));
        ChecksContext.Check("视图模型切语言重建文案",
            viewModel.Contains("Relocalize", StringComparison.Ordinal));

        ChecksContext.Check("AppSettings 记录崩溃分析页分节",
            settings.Contains("LastCrashAnalysisSection", StringComparison.Ordinal));

        ChecksContext.Check("崩溃日志读取只用 OpenRead",
            logReader.Contains("OpenRead", StringComparison.Ordinal));
        ChecksContext.Check("崩溃日志读取不写文件",
            !logReader.Contains("WriteAll", StringComparison.Ordinal)
            && !logReader.Contains("Delete(", StringComparison.Ordinal));

        ChecksContext.Check("zh-CN 有 crash 区域", CountCrashKeys(zhTable) > 0);
        ChecksContext.Check("en-US 有 crash 区域", CountCrashKeys(enTable) > 0);
        ChecksContext.Equal("两侧 crash 键数一致", CountCrashKeys(zhTable), CountCrashKeys(enTable));
        ChecksContext.Check("shell 文案含崩溃分析导航键",
            zhTable.ContainsKey("ShellNavCrashAnalysis"));
        ChecksContext.Check("生成表含页面标题键",
            zhTable.ContainsKey("CrashPageTitle"));
        ChecksContext.Check("生成表含页脚导航键",
            zhTable.ContainsKey("ShellNavCrashAnalysis"));
        ChecksContext.Check("生成表含 create_building 结论键",
            zhTable.ContainsKey("CrashFindingCreateBuildingAutoPlacedTitle"));
    }

    // ------------------------------------------------------------ 行为 fixture

    private static void CheckAnalyzerFixture(IFileSystem fs)
    {
        var root = CreateTempRoot("pdxnext-crash-fixture");
        try
        {
            var dataDir = Path.Combine(root, "game-data");
            var logsDir = Path.Combine(dataDir, "logs");
            var crashDir = Path.Combine(dataDir, "crashes", "victoria3_01261007_205533");
            var crashLogs = Path.Combine(crashDir, "logs");
            var modRoot = Path.Combine(root, "mods");
            var cheatMod = Path.Combine(modRoot, "cheat-menu-pro");
            var oldMod = Path.Combine(modRoot, "old-mod");
            var nonAsciiMod = Path.Combine(modRoot, "中文mod");
            var missingMod = Path.Combine(modRoot, "missing-mod");
            var gameDir = Path.Combine(root, "game");
            var exePath = Path.Combine(gameDir, "victoria3.exe");
            var launcherLog = Path.Combine(root, "launcher.log");

            fs.EnsureDirectory(logsDir);
            fs.EnsureDirectory(crashLogs);
            fs.EnsureDirectory(Path.Combine(cheatMod, "common", "scripted_effects"));
            fs.EnsureDirectory(Path.Combine(cheatMod, ".metadata"));
            fs.EnsureDirectory(Path.Combine(oldMod, ".metadata"));
            fs.EnsureDirectory(Path.Combine(nonAsciiMod, ".metadata"));
            fs.EnsureDirectory(gameDir);

            fs.WriteAllTextAtomic(exePath, "x");
            fs.WriteAllTextAtomic(Path.Combine(cheatMod, "common", "scripted_effects", "sakuya_cheat_b6_effects.txt"), "x");
            fs.WriteAllTextAtomic(
                Path.Combine(cheatMod, ".metadata", "metadata.json"),
                "{\"name\":\"Cheat Menu Pro\",\"version\":\"1.0\",\"supported_game_version\":\"1.15.*\"}");
            fs.WriteAllTextAtomic(
                Path.Combine(oldMod, ".metadata", "metadata.json"),
                "{\"name\":\"Old Mod\",\"version\":\"1.0\",\"supported_game_version\":\"1.13.*\"}");
            fs.WriteAllTextAtomic(
                Path.Combine(nonAsciiMod, ".metadata", "metadata.json"),
                "{\"name\":\"本地中文 Mod\",\"version\":\"1.0\",\"supported_game_version\":\"1.15.*\"}");

            WriteLoadFile(fs, dataDir, [cheatMod, oldMod, nonAsciiMod, missingMod]);
            WriteMainFixtureLogs(fs, crashLogs);
            fs.WriteAllTextAtomic(
                Path.Combine(dataDir, "pdx_settings.json"),
                "{\"Graphics\":{\"renderer\":\"Vulkan\"}}");

            var exceptionText = """
                Application: Victoria 3
                Version: 1.15.0
                Date/Time: 2026-11-07 20:55:35

                Unhandled Exception C0000005 (EXCEPTION_ACCESS_VIOLATION) at address 0x00007FF62C9F4D38

                Stack Trace:
                  1 victoria3.exe (function-name not available) (+ 0)
                """;
            fs.WriteAllTextAtomic(Path.Combine(crashDir, "exception.txt"), exceptionText);
            fs.WriteAllTextAtomic(
                Path.Combine(crashDir, "meta.yml"),
                """
                # Crash Information
                AppName: Victoria 3
                AppVersion: 1.15.0
                GPUName: NVIDIA GeForce RTX 3080
                SystemMemory: 65328
                OperatingSystem: Windows 11 (Build 26300)
                Mod_1: Cheat Menu Pro
                """);
            fs.WriteAllTextAtomic(Path.Combine(crashDir, "minidump.dmp"), "not-a-real-dump");

            fs.WriteAllTextAtomic(
                launcherLog,
                """
                2026-11-07 20:54:00.000 [INFO] 启动游戏 victoria3：D:\game\victoria3.exe
                2026-11-07 20:54:00.100 [ERROR] 启动失败：找不到可执行文件 D:\game\victoria3.exe
                """);

            var game = MakeGame(root, dataDir, modRoot, exePath);
            var logger = new SilentLogger();
            var analyzer = new GameCrashAnalyzer(fs, new LoadFileWriter(fs, logger), logger, SystemClock.Instance);
            var report = analyzer.AnalyzeAsync(game, launcherLog).GetAwaiter().GetResult();

            ChecksContext.Equal("找到 1 个崩溃包", 1, report.Crashes.Count);
            ChecksContext.Equal("异常码 C0000005", "C0000005", report.LatestCrash?.ExceptionCode);
            ChecksContext.Equal("崩溃时间 20:55:35", 20, report.LatestCrash?.Timestamp?.Hour ?? -1);
            ChecksContext.Equal("启动到崩溃 101 秒", 101, (int)(report.LaunchChain.Duration?.TotalSeconds ?? -1));
            ChecksContext.Check("崩溃包内日志被优先使用", report.LaunchChain.UsedCrashLogs);
            ChecksContext.Equal("启用的 Mod 读取到 4 个", 4, report.Mods.Count);
            ChecksContext.Check("加载文件可读", report.ModListAvailable);

            var scriptFinding = report.Findings.FirstOrDefault(f => f.Code == CrashFindingCode.ModScriptErrorsNearCrash);
            ChecksContext.Check("识别崩溃前脚本错误", scriptFinding is not null);
            ChecksContext.Equal("脚本错误严重度为高风险", CrashSeverity.Critical, scriptFinding?.Severity ?? CrashSeverity.Info);
            ChecksContext.Equal("脚本错误计数去重后为 2", 2, scriptFinding?.Count ?? -1);
            ChecksContext.Equal("脚本错误归属 Cheat Menu Pro", "Cheat Menu Pro", scriptFinding?.ModName);
            ChecksContext.Check("脚本错误来源路径正确",
                scriptFinding?.SourcePath == "common/scripted_effects/sakuya_cheat_b6_effects.txt");

            var createFinding = report.Findings.FirstOrDefault(f => f.Code == CrashFindingCode.CreateBuildingAutoPlaced);
            ChecksContext.Check("识别 create_building 自动放置建筑", createFinding is not null);
            ChecksContext.Equal("create_building 计数为 1", 1, createFinding?.Count ?? -1);
            ChecksContext.Equal("create_building 归属 Cheat Menu Pro", "Cheat Menu Pro", createFinding?.ModName);

            ChecksContext.Check("识别未设置变量",
                report.Findings.Any(f => f.Code == CrashFindingCode.UnsetScriptVariables));
            ChecksContext.Check("识别孤立事件",
                report.Findings.Any(f => f.Code == CrashFindingCode.OrphanedEvents));
            ChecksContext.Check("识别不兼容 modifier",
                report.Findings.Any(f => f.Code == CrashFindingCode.IncompatibleModifierType));
            ChecksContext.Check("识别地图 locator 警告",
                report.Findings.Any(f => f.Code == CrashFindingCode.MapLocatorIncomplete));
            ChecksContext.Check("识别启动阶段崩溃",
                report.Findings.Any(f => f.Code == CrashFindingCode.StartupCrash));
            ChecksContext.Check("识别失效 Mod 路径",
                report.Findings.Any(f => f.Code == CrashFindingCode.MissingModPath));
            ChecksContext.Check("识别 Victoria 3 非 ASCII 路径",
                report.Findings.Any(f => f.Code == CrashFindingCode.NonAsciiModPath));
            ChecksContext.Check("识别 Mod 版本不匹配",
                report.Findings.Any(f => f.Code == CrashFindingCode.ModVersionMismatch));
            ChecksContext.Check("识别启动器启动失败",
                report.Findings.Any(f => f.Code == CrashFindingCode.LauncherLaunchFailed));
            ChecksContext.Check("有效 pdx_settings 不误报损坏",
                report.Findings.All(f => f.Code != CrashFindingCode.CorruptGameSettings));

            ChecksContext.Equal("结论按严重度排序", CrashSeverity.Critical, report.Findings.FirstOrDefault()?.Severity ?? CrashSeverity.Info);
            ChecksContext.Check("日志文件包含 error.log",
                report.LogFiles.Any(file => string.Equals(file.Name, "error.log", StringComparison.OrdinalIgnoreCase)));
            ChecksContext.Check("错误聚类关联到 Cheat Menu Pro",
                report.Groups.Any(group => group.ModName == "Cheat Menu Pro"));

            var top = report.Findings.FirstOrDefault();
            ChecksContext.Check("首条结论是脚本错误", top?.Code == CrashFindingCode.ModScriptErrorsNearCrash);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CheckNoCrashFallback(IFileSystem fs)
    {
        var root = CreateTempRoot("pdxnext-crash-nofallback");
        try
        {
            var dataDir = Path.Combine(root, "game-data");
            var logsDir = Path.Combine(dataDir, "logs");
            fs.EnsureDirectory(logsDir);
            fs.WriteAllTextAtomic(
                Path.Combine(logsDir, "system.log"),
                "[20:00:00][game_setup.cpp:225]: Exe Git Version: release/1.15.0\n");
            fs.WriteAllTextAtomic(
                Path.Combine(logsDir, "error.log"),
                "[20:00:05][jomini_script_system.cpp:247]: Script system error!\n");

            var game = MakeGame(root, dataDir, Path.Combine(root, "mods"), Path.Combine(root, "game", "victoria3.exe"));
            var logger = new SilentLogger();
            var analyzer = new GameCrashAnalyzer(fs, new LoadFileWriter(fs, logger), logger, SystemClock.Instance);
            var report = analyzer.AnalyzeAsync(game, launcherLogPath: null).GetAwaiter().GetResult();

            ChecksContext.Check("没有崩溃包时不伪造崩溃", !report.HasCrash);
            ChecksContext.Check("识别没有崩溃转储",
                report.Findings.Any(f => f.Code == CrashFindingCode.NoCrashBundle));
            ChecksContext.Check("无崩溃时不计算启动时长", report.LaunchChain.Duration is null);
            ChecksContext.Check("仍读取到实时日志", report.LogFiles.Count > 0);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CheckCorruptSettings(IFileSystem fs)
    {
        var root = CreateTempRoot("pdxnext-crash-bad-settings");
        try
        {
            var dataDir = Path.Combine(root, "game-data");
            fs.EnsureDirectory(dataDir);
            fs.WriteAllTextAtomic(Path.Combine(dataDir, "pdx_settings.json"), "{ not json");

            var game = MakeGame(root, dataDir, Path.Combine(root, "mods"), Path.Combine(root, "game", "victoria3.exe"));
            var logger = new SilentLogger();
            var analyzer = new GameCrashAnalyzer(fs, new LoadFileWriter(fs, logger), logger, SystemClock.Instance);
            var report = analyzer.AnalyzeAsync(game, launcherLogPath: null).GetAwaiter().GetResult();

            ChecksContext.Check("识别损坏的 pdx_settings.json",
                report.Findings.Any(f => f.Code == CrashFindingCode.CorruptGameSettings));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CheckLogReader(IFileSystem fs)
    {
        var root = CreateTempRoot("pdxnext-crash-reader");
        try
        {
            var path = Path.Combine(root, "error.log");
            fs.WriteAllTextAtomic(
                path,
                "HEAD-MARKER\n" + string.Concat(Enumerable.Repeat("filler line\n", 200)) + "TAIL-MARKER\n");
            var tail = CrashLogReader.ReadTail(fs, path, maxBytes: 128);
            ChecksContext.Check("只读取日志尾部（不含开头标记）", !tail.Contains("HEAD-MARKER", StringComparison.Ordinal));
            ChecksContext.Check("尾部包含最后一行的标记", tail.Contains("TAIL-MARKER", StringComparison.Ordinal));
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    private static void CheckCrossLogDedupe()
    {
        var block =
            "[20:55:22][jomini_script_system.cpp:247]: Script system error!\n"
            + "  Error: create_building effect [ Attempting to manually create auto-placed building ]\n"
            + "  Script location: common/scripted_effects/test.txt:1\n"
            + "\n"
            + "[20:55:22][jomini_effect.cpp:142]: PostValidate of effect 'create_building' returned false at common/scripted_effects/test.txt:1\n";

        var inputs = new[]
        {
            new CrashLogInput("error.log", "error.log", block, block.Length, true),
            new CrashLogInput("game.log", "game.log", block, block.Length, true),
        };
        var signals = CrashSignals.Collect(inputs, new DateTimeOffset(2026, 11, 7, 20, 55, 35, TimeSpan.FromHours(8)));
        ChecksContext.Equal("同一错误写进两份日志只算一次脚本错误", 2, signals.ScriptErrorBlocks);
        ChecksContext.Equal("同一错误写进两份日志只算一次 create_building", 1, signals.CreateBuildingAutoPlaced);
    }

    // ------------------------------------------------------------ fixture 辅助

    private static void WriteMainFixtureLogs(IFileSystem fs, string directory)
    {
        fs.WriteAllTextAtomic(
            Path.Combine(directory, "system.log"),
            """
            [20:53:54][game_setup.cpp:225]: Exe Git Version: release/1.15.0 : 6338547985
            [20:53:55][systemsettings.cpp:205]: CPU Model: 12th Gen Intel(R) Core(TM) i5-12600KF
            [20:54:08][gameapplication.cpp:1387]: Loading cursors...
            """);

        fs.WriteAllTextAtomic(
            Path.Combine(directory, "error.log"),
            """
            [20:55:22][jomini_script_system.cpp:247]: Script system error!
              Error: create_building effect [ Attempting to manually create auto-placed building ]
              Script location: common/scripted_effects/sakuya_cheat_b6_effects.txt:2461

            [20:55:22][jomini_effect.cpp:142]: PostValidate of effect 'create_building' returned false at common/scripted_effects/sakuya_cheat_b6_effects.txt:2461
            """);

        fs.WriteAllTextAtomic(
            Path.Combine(directory, "game.log"),
            """
            [20:55:22][jomini_script_system.cpp:247]: Script system error!
              Error: create_building effect [ Attempting to manually create auto-placed building ]
              Script location: common/scripted_effects/sakuya_cheat_b6_effects.txt:2461

            [20:55:30][jomini_effect.cpp:1144]: Variable 'sakuya_test' is used but is never set. Setting it in an unused scripted trigger or effect does not count
            """);

        fs.WriteAllTextAtomic(
            Path.Combine(directory, "debug.log"),
            """
            [20:55:25][jomini_eventmanager.cpp:376]: Event sakuya_test_event.1 is orphaned
            [20:55:26][modifier_type_definition_database.cpp:43]: Modifier type definition fake_standard_of_living_add is defined in script but not in code, it should either be added to code or flagged script_only.
            """);

        fs.WriteAllTextAtomic(
            Path.Combine(directory, "graphics.log"),
            """
            [20:55:29][game_object_locators.cpp:433]: map object locator "wood" is incomplete.
            [20:55:29][coastal_graphics.cpp:65]: Cannot find a port node for harbor in 'STATE_SARGASSO_SEA' (727)
            """);
    }

    private static void WriteLoadFile(IFileSystem fs, string dataDir, IReadOnlyList<string> modPaths)
    {
        var payload = new
        {
            enabledUGC = Array.Empty<string>(),
            disabledDLC = Array.Empty<string>(),
            enabledMods = modPaths.Select(path => new { path }).ToArray(),
        };
        fs.WriteAllTextAtomic(
            Path.Combine(dataDir, "content_load.json"),
            JsonSerializer.Serialize(payload));
    }

    private static GameInstallation MakeGame(
        string root,
        string dataDir,
        string modDir,
        string exePath) =>
        new(
            GameId: "victoria3",
            DisplayName: "Victoria 3",
            Version: "1.15.0",
            InstallRoot: Path.Combine(root, "game"),
            LauncherSettingsPath: Path.Combine(root, "game", "launcher", "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1_1,
            GameDataDir: dataDir,
            DlcPath: Path.Combine(root, "game", "dlc"),
            ModDir: modDir,
            ExePath: exePath,
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
            SteamAppId: 529340,
            Support: GameSupport.Full);

    private static string CreateTempRoot(string prefix)
    {
        var root = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    // ------------------------------------------------------------ 读文件辅助

    private static int CountCrashKeys(Dictionary<string, string> table) =>
        table.Keys.Count(key => key.StartsWith("Crash", StringComparison.Ordinal));

    private static string SliceBetween(string text, string start, string end)
    {
        var from = text.IndexOf(start, StringComparison.Ordinal);
        if (from < 0)
        {
            return string.Empty;
        }

        var to = text.IndexOf(end, from, StringComparison.Ordinal);
        return to < 0 ? text[from..] : text[from..(to + end.Length)];
    }

    private static string StripComments(string text) =>
        Regex.Replace(text, "<!--.*?-->", string.Empty, RegexOptions.Singleline);

    private static string Read(string root, string relativePath)
    {
        var path = Path.Combine(root, relativePath);
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string? FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "version.props")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private sealed class SilentLogger : IAppLogger
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
