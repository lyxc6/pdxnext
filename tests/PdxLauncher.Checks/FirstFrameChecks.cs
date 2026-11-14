namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// 首帧性能的自检：背景解码宽度策略、背景预热快照往返、启动里程碑的内部埋点约定，
/// 以及「主页只取按钮外观、背景按窗口尺寸解码」的源码契约。
/// 这些写错了不会崩，只会让启动悄悄变慢（或背景变糊），所以口径在这里钉住。
/// </summary>
public static class FirstFrameChecks
{
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        CheckDecodeWidth();
        CheckSnapshotRoundTrip(fileSystem);
        CheckInternalMarks();
        CheckSourceContracts();

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------ 解码宽度策略

    private static void CheckDecodeWidth()
    {
        ChecksContext.Equal("首帧: 1200x800 @100% 解 1280", 1280, BackdropDecodeWidth.Resolve(1200, 800));
        ChecksContext.Equal("首帧: 1800x1200 @150% 解 1920", 1920, BackdropDecodeWidth.Resolve(1800, 1200));
        ChecksContext.Equal("首帧: 3840x2160 不超过 2560", 2560, BackdropDecodeWidth.Resolve(3840, 2160));
        ChecksContext.Equal("首帧: 小窗不低于 1280", 1280, BackdropDecodeWidth.Resolve(600, 400));
        ChecksContext.Equal("首帧: 竖窗按长边算", 1920, BackdropDecodeWidth.Resolve(1200, 1800));
        ChecksContext.Equal("首帧: 非法尺寸退回下限", 1280, BackdropDecodeWidth.Resolve(double.NaN, 0));

        ChecksContext.Check(
            "首帧: 窗口明显变大才重解（+15% 阈值）",
            BackdropDecodeWidth.NeedsUpgrade(1280, 1920, 1080),
            "1280 -> 1920");
        ChecksContext.Check(
            "首帧: 轻微变大不重解",
            !BackdropDecodeWidth.NeedsUpgrade(1280, 1300, 800),
            "1280 -> 1300");
        ChecksContext.Check(
            "首帧: 没有已上屏宽度时要求重解",
            BackdropDecodeWidth.NeedsUpgrade(0, 1200, 800),
            "0 -> 1280");
    }

    // ------------------------------------------------------------ 快照往返

    private static void CheckSnapshotRoundTrip(IFileSystem fileSystem)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-firstframe-" + Guid.NewGuid().ToString("N"));
        var snapshotPath = Path.Combine(root, "frame-snapshot.json");
        var logger = new SilentLogger();
        try
        {
            fileSystem.EnsureDirectory(root);

            ChecksContext.Check(
                "首帧快照: 文件不存在时返回 null",
                FrameSnapshotStore.Load(fileSystem, logger, snapshotPath) is null);

            var saved = new FrameSnapshot(
                FrameSnapshotStore.CurrentVersion,
                "ck3",
                @"C:\games\ck3\launcher\assets\bg.png",
                DateTimeOffset.UtcNow);
            FrameSnapshotStore.Save(fileSystem, logger, saved, snapshotPath);

            var loaded = FrameSnapshotStore.Load(fileSystem, logger, snapshotPath);
            ChecksContext.Check("首帧快照: 写入后可以读回", loaded is not null);
            if (loaded is not null)
            {
                ChecksContext.Equal("首帧快照: gameId 往返", "ck3", loaded.GameId);
                ChecksContext.Equal("首帧快照: 路径往返", saved.BackgroundPath, loaded.BackgroundPath);
            }

            // 版本不认识：整份当不存在，不能让旧格式悄悄生效。
            fileSystem.WriteAllTextAtomic(
                snapshotPath,
                "{\"version\":99,\"gameId\":\"ck3\",\"backgroundPath\":\"x\",\"savedAtUtc\":\"2026-01-01T00:00:00Z\"}");
            ChecksContext.Check(
                "首帧快照: 版本不符时返回 null",
                FrameSnapshotStore.Load(fileSystem, logger, snapshotPath) is null);

            // 坏 JSON：返回 null 且不抛。
            fileSystem.WriteAllTextAtomic(snapshotPath, "{not json");
            ChecksContext.Check(
                "首帧快照: 坏 JSON 返回 null",
                FrameSnapshotStore.Load(fileSystem, logger, snapshotPath) is null);
        }
        finally
        {
            try
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
            catch (IOException)
            {
                // 临时目录清不掉不影响结论。
            }
        }
    }

    // ------------------------------------------------------------ 内部里程碑只进日志

    private static void CheckInternalMarks()
    {
        StartupTimeline.Reset();
        StartupTimeline.Mark(StartupTimeline.AppStart);
        StartupTimeline.MarkInternal(StartupTimeline.FirstFrame);

        ChecksContext.Equal("首帧里程碑: 内部埋点不进诊断页", 1, StartupTimeline.Snapshot().Count);
        ChecksContext.Equal("首帧里程碑: 内部埋点在全量快照里", 2, StartupTimeline.SnapshotAll().Count);

        var described = StartupTimeline.DescribeAll();
        ChecksContext.Check(
            "首帧里程碑: 明细日志包含内部键",
            described.Contains(StartupTimeline.FirstFrame + "=", StringComparison.Ordinal),
            described);
        ChecksContext.Check(
            "首帧里程碑: 明细日志包含公开键",
            described.Contains(StartupTimeline.AppStart + "=", StringComparison.Ordinal),
            described);

        StartupTimeline.Reset();
    }

    // ------------------------------------------------------------ 源码契约

    private static void CheckSourceContracts()
    {
        var root = FindRepositoryRoot();
        if (!ChecksContext.Check("首帧: 找到仓库根", root is not null, root ?? "未找到"))
        {
            return;
        }

        var homePage = ReadRepositoryFile(root!, "src", "PdxLauncher.App", "Views", "HomePage.xaml.cs");
        if (homePage is not null)
        {
            ChecksContext.Check(
                "首帧: 主页只取按钮外观（ButtonChrome）",
                homePage.Contains("ThemeAssetScope.ButtonChrome", StringComparison.Ordinal),
                "HomePage.xaml.cs 是否给 CreateAsync 传 ButtonChrome");
            ChecksContext.Check(
                "首帧: 主页记录了首帧里程碑",
                homePage.Contains("StartupTimeline.MarkInternal(StartupTimeline.FirstFrame)", StringComparison.Ordinal),
                "HomePage.xaml.cs 是否在 Rendering 首次回调里打点");
        }

        var applier = ReadRepositoryFile(root!, "src", "PdxLauncher.App", "Services", "ThemeApplier.cs");
        if (applier is not null)
        {
            var start = applier.IndexOf("public async Task<ThemeVisuals> CreateAsync", StringComparison.Ordinal);
            var end = applier.IndexOf("private ThemeVisuals Build", StringComparison.Ordinal);
            if (ChecksContext.Check(
                "首帧: 找到 ThemeApplier.CreateAsync",
                start >= 0 && end > start,
                start + "/" + end))
            {
                var body = applier[start..end];
                ChecksContext.Check(
                    "首帧: 整幅背景只在 All 范围才读",
                    body.Contains("full ? ReadAssetBytes(spec.Background) : null", StringComparison.Ordinal),
                    "CreateAsync 体内背景读取是否受 scope 控制");
                ChecksContext.Check(
                    "首帧: 字标只在 All 范围才读",
                    body.Contains("full ? ReadAssetBytes(spec.Logo) : null", StringComparison.Ordinal),
                    "CreateAsync 体内字标读取是否受 scope 控制");
            }
        }

        var mainWindow = ReadRepositoryFile(root!, "src", "PdxLauncher.App", "MainWindow.xaml.cs");
        if (mainWindow is not null)
        {
            ChecksContext.Check(
                "首帧: 背景按窗口尺寸解码（不再固定 2560）",
                mainWindow.Contains("Services.BackdropDecodeWidth.Resolve(", StringComparison.Ordinal),
                "MainWindow 是否用解码宽度策略");
            ChecksContext.Check(
                "首帧: 启动时按快照预解码背景",
                mainWindow.Contains("TakePreloadedBackdropAsync(", StringComparison.Ordinal)
                    && mainWindow.Contains("StartSpeculativeBackdropPreload()", StringComparison.Ordinal),
                "MainWindow 是否有预解码与复用");
            ChecksContext.Check(
                "首帧: 窗口变大后按需提升解码宽度",
                mainWindow.Contains("BackdropDecodeWidth.NeedsUpgrade(", StringComparison.Ordinal),
                "MainWindow 是否挂了尺寸变化后的重解判定");
        }
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

    private static string? ReadRepositoryFile(string root, params string[] relative)
    {
        var path = Path.Combine(new[] { root }.Concat(relative).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : null;
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
