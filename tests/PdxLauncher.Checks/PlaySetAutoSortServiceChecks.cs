namespace PdxLauncher.Checks;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 自动排序服务（PlaySetAutoSortService）的回归护栏：
/// 覆盖预览产物（足迹/冲突/边/结果/重排行）、库声明依赖 mods_dependencies 与
/// descriptor 依赖的边映射、缺库降级不抛、重排行保持原行内容并重编位置。
/// 临时库与临时目录用例全部在系统临时目录里跑，绝不碰真机 launcher-v2.sqlite。
/// </summary>
public static class PlaySetAutoSortServiceChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckTierReorder(fs);
        CheckDeclaredDependencyOverridesTier(fs);
        CheckDescriptorDependencyBySteamId(fs);
        CheckConflictGraphInPreview(fs);
        CheckMissingDatabaseDegrades(fs);
        CheckReorderedRowsPreserveContent(fs);
        CheckInputSignatureAndStaleness();
        CheckProgressAndCancellation(fs);

        return ChecksContext.Failures - before;
    }

    /// <summary>层位重排：中等改动在前、小修随后、翻译最后，重排行位置连续 0..n-1。</summary>
    private static void CheckTierReorder(IFileSystem fs)
    {
        var root = NewTempRoot("tier");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt", "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt",
                "common/v4.txt", "common/v5.txt", "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt",
                "common/v10.txt", "common/v11.txt");

            var tweak = NewMod(fs, root, "m-tweak", "小调整", "common/base.txt");
            var medium = NewMod(fs, root, "m-medium", "中等内容",
                "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt", "common/v5.txt",
                "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt", "common/v11.txt");
            var loc = NewMod(fs, root, "m-loc", "CN Text Pack", "localization/simp_chinese/x.yml");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { tweak, medium, loc }, new SilentLogger());

            ChecksContext.Equal("层位重排后的顺序", "中等内容,小调整,CN Text Pack", Names(preview.ReorderedRows));
            ChecksContext.Equal("重排行位置连续", "0,1,2", string.Join(",", preview.ReorderedRows.Select(r => r.Position)));
            ChecksContext.Equal("足迹条数与 mod 数一致", 3, preview.Footprints.Count);
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>库声明依赖是硬边：内容包声明依赖框架时，框架必须先于内容包加载（压过层级先后）。</summary>
    private static void CheckDeclaredDependencyOverridesTier(IFileSystem fs)
    {
        var root = NewTempRoot("dep");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt");

            var framework = NewMod(fs, root, "m-fw", "框架", "common/extra_a.txt");
            var pack = NewMod(fs, root, "m-pack", "内容包",
                "common/base.txt", "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt",
                "common/v5.txt", "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt",
                "common/v11.txt");

            CreateDependencyTable(fs, game);
            InsertDependencyRow(fs, game, pack.ModId, framework.ModId, "框架依赖");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { pack, framework }, new SilentLogger());

            ChecksContext.Equal("声明依赖压过层级", "框架,内容包", Names(preview.ReorderedRows));
            var edge = preview.Edges.FirstOrDefault(e => e.Kind == SortEdgeKind.DeclaredDependency);
            ChecksContext.Check("声明依赖边存在", edge is not null, "未找到 DeclaredDependency 边");
            if (edge is not null)
            {
                ChecksContext.Check("依赖在前、被依赖在后", edge.Before == framework.ModId && edge.After == pack.ModId,
                    "Before=" + edge.Before + " After=" + edge.After);
            }
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>descriptor 的 dependencies 块按 SteamId 匹配成 DeclaredDependency 边。</summary>
    private static void CheckDescriptorDependencyBySteamId(IFileSystem fs)
    {
        var root = NewTempRoot("desc");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt");

            var framework = NewMod(fs, root, "m-fw", "框架", "common/extra_a.txt");
            framework = framework with { SteamId = "777666" };
            var pack = NewMod(fs, root, "m-pack", "内容包",
                "common/base.txt", "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt",
                "common/v5.txt", "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt",
                "common/v11.txt");
            fs.WriteAllTextAtomic(Path.Combine(pack.DirPath, "descriptor.mod"),
                "name=\"内容包\"" + "\n" + "dependencies={" + "\n" + "    workshop_item_id = 777666" + "\n" + "}" + "\n");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { pack, framework }, new SilentLogger());

            ChecksContext.Equal("descriptor 依赖压过层级", "框架,内容包", Names(preview.ReorderedRows));
            var edge = preview.Edges.FirstOrDefault(e => e.Kind == SortEdgeKind.DeclaredDependency);
            ChecksContext.Check("descriptor 依赖边存在", edge is not null, "未找到 DeclaredDependency 边");
            if (edge is not null)
            {
                ChecksContext.Check("descriptor 边指向正确", edge.Before == framework.ModId && edge.After == pack.ModId,
                    "Before=" + edge.Before + " After=" + edge.After);
            }
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>共享新增文件且无法判定基底时进冲突列表（SharedAddedFileAmbiguous），足迹随预览返回。</summary>
    private static void CheckConflictGraphInPreview(IFileSystem fs)
    {
        var root = NewTempRoot("conflict");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt");

            var alpha = NewMod(fs, root, "m-a", "甲", "common/shared/x.txt");
            var beta = NewMod(fs, root, "m-b", "乙", "common/shared/x.txt");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { alpha, beta }, new SilentLogger());

            ChecksContext.Equal("冲突条数", 1, preview.Conflicts.Count);
            if (preview.Conflicts.Count == 1)
            {
                ChecksContext.Equal("冲突类型为共享新增歧义", AutoSortConflictKind.SharedAddedFileAmbiguous, preview.Conflicts[0].Kind);
                ChecksContext.Equal("共享路径计数", 1, preview.Conflicts[0].SharedPathCount);
            }

            ChecksContext.Check("足迹含两条", preview.Footprints.Count == 2, "实际 " + preview.Footprints.Count);
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>缺 launcher-v2.sqlite 时声明依赖降级为空，不抛异常，排序照常产出。</summary>
    private static void CheckMissingDatabaseDegrades(IFileSystem fs)
    {
        var root = NewTempRoot("nodb");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt");

            var tweak = NewMod(fs, root, "m-tweak", "小调整", "common/base.txt");
            var medium = NewMod(fs, root, "m-medium", "中等内容",
                "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt", "common/v5.txt",
                "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt", "common/v11.txt");
            WriteVanilla(fs, game,
                "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt", "common/v5.txt",
                "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt", "common/v11.txt");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { tweak, medium }, new SilentLogger());

            ChecksContext.Equal("缺库时无声明边", 0, preview.Edges.Count);
            ChecksContext.Equal("缺库时仍按层位排序", "中等内容,小调整", Names(preview.ReorderedRows));
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>重排行保留原行内容（名称/启用态），只是顺序与位置更新。</summary>
    private static void CheckReorderedRowsPreserveContent(IFileSystem fs)
    {
        var root = NewTempRoot("rows");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt", "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt",
                "common/v4.txt", "common/v5.txt", "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt",
                "common/v10.txt", "common/v11.txt");

            var tweak = NewMod(fs, root, "m-tweak", "小调整", "common/base.txt");
            tweak = tweak with { Enabled = false };
            var medium = NewMod(fs, root, "m-medium", "中等内容",
                "common/v0.txt", "common/v1.txt", "common/v2.txt", "common/v3.txt", "common/v4.txt", "common/v5.txt",
                "common/v6.txt", "common/v7.txt", "common/v8.txt", "common/v9.txt", "common/v10.txt", "common/v11.txt");

            var preview = PlaySetAutoSortService.BuildPreview(fs, game, new[] { tweak, medium }, new SilentLogger());

            ChecksContext.Equal("重排行条数不变", 2, preview.ReorderedRows.Count);
            var reorderedTweak = preview.ReorderedRows.FirstOrDefault(r => r.ModId == tweak.ModId);
            ChecksContext.Check("重排行仍在集合内", reorderedTweak is not null, "找不到小调整的重排行");
            if (reorderedTweak is not null)
            {
                ChecksContext.Equal("启用态保留", false, reorderedTweak.Enabled);
                ChecksContext.Equal("名称保留", "小调整", reorderedTweak.Name);
            }

            ChecksContext.Equal("ModId 集合不变",
                string.Join(",", preview.ReorderedRows.Select(r => r.ModId).OrderBy(id => id)),
                string.Join(",", new[] { tweak.ModId, medium.ModId }.OrderBy(id => id)));
        }
        finally
        {
            fs.Delete(root);
        }
    }

    /// <summary>输入签名只看身份/位置/启用；改名不算过期，无签名的预览一律视为过期。</summary>
    private static void CheckInputSignatureAndStaleness()
    {
        var a = Row(Guid.NewGuid(), "甲", 0, true);
        var b = Row(Guid.NewGuid(), "乙", 1, false);
        var rows = new[] { a, b };
        var signature = PlaySetAutoSortService.ComputeInputSignature(rows);

        ChecksContext.Equal("同样输入的签名一致", signature, PlaySetAutoSortService.ComputeInputSignature(new[]
        {
            Row(a.ModId, "甲", 0, true),
            Row(b.ModId, "乙", 1, false),
        }));
        ChecksContext.Equal("只改名不影响签名", signature, PlaySetAutoSortService.ComputeInputSignature(new[]
        {
            Row(a.ModId, "甲改名", 0, true),
            Row(b.ModId, "乙", 1, false),
        }));
        ChecksContext.Check("位置互换后签名不同", signature != PlaySetAutoSortService.ComputeInputSignature(new[]
        {
            Row(a.ModId, "甲", 1, true),
            Row(b.ModId, "乙", 0, false),
        }));
        ChecksContext.Check("启用态变化后签名不同", signature != PlaySetAutoSortService.ComputeInputSignature(new[]
        {
            Row(a.ModId, "甲", 0, false),
            Row(b.ModId, "乙", 1, false),
        }));

        var preview = new AutoSortPreview(
            Array.Empty<ModFootprint>(),
            Array.Empty<SortEdge>(),
            Array.Empty<AutoSortConflict>(),
            new AutoSortResult(Array.Empty<AutoSortEntry>(), Array.Empty<AutoSortWarning>()),
            rows,
            Guid.NewGuid(),
            signature);

        ChecksContext.Check("列表未变时预览不过期", !preview.IsStaleFor(rows));
        ChecksContext.Check("位置变了预览过期", preview.IsStaleFor(new[]
        {
            Row(a.ModId, "甲", 1, true),
            Row(b.ModId, "乙", 0, false),
        }));
        ChecksContext.Check("没有播放集 ID 的预览视为过期", (preview with { PlaySetId = null }).IsStaleFor(rows));
        ChecksContext.Check("没有签名的预览视为过期", (preview with { InputSignature = null }).IsStaleFor(rows));
    }

    /// <summary>扫描进度按相位上报（原版索引/逐 mod/建图），取消能中断扫描并抛 OperationCanceledException。</summary>
    private static void CheckProgressAndCancellation(IFileSystem fs)
    {
        var root = NewTempRoot("progress");
        try
        {
            var game = NewGame(fs, root);
            WriteVanilla(fs, game, "common/base.txt", "common/v0.txt", "common/v1.txt", "common/v2.txt");
            var first = NewMod(fs, root, "m-first", "甲", "common/base.txt");
            var second = NewMod(fs, root, "m-second", "乙", "common/v0.txt", "common/v1.txt");
            var mods = new[] { first, second };

            var recorder = new RecordingProgress();
            PlaySetAutoSortService.BuildPreview(fs, game, mods, new SilentLogger(), progress: recorder);

            ChecksContext.Check("进度报过原版索引相位", recorder.Phases.Contains(AutoSortPhase.VanillaIndex));
            ChecksContext.Check("进度报过逐 mod 扫描相位", recorder.Phases.Contains(AutoSortPhase.ScanningMods));
            ChecksContext.Check("进度报过建图相位", recorder.Phases.Contains(AutoSortPhase.Graph));
            ChecksContext.Equal("扫描相位完成数达到 mod 总数", 2, recorder.MaxScanCompleted);
            ChecksContext.Check("原版索引相位有实际计数", recorder.MaxVanillaScanned > 0);

            using var cts = new CancellationTokenSource();
            var canceled = false;
            try
            {
                PlaySetAutoSortService.BuildPreview(
                    fs,
                    game,
                    mods,
                    new SilentLogger(),
                    cts.Token,
                    new CancellingProgress(cts));
            }
            catch (OperationCanceledException)
            {
                canceled = true;
            }

            ChecksContext.Check("取消后抛 OperationCanceledException", canceled);
        }
        finally
        {
            fs.Delete(root);
        }
    }

    private static PlaySetMod Row(Guid id, string name, int position, bool enabled) => new(
        id, name, string.Empty, null, string.Empty, null, string.Empty, string.Empty, enabled, position);

    private sealed class RecordingProgress : IProgress<AutoSortProgress>
    {
        public HashSet<AutoSortPhase> Phases { get; } = new();

        public int MaxScanCompleted { get; private set; }

        public int MaxVanillaScanned { get; private set; }

        public void Report(AutoSortProgress value)
        {
            Phases.Add(value.Phase);
            if (value.Phase == AutoSortPhase.ScanningMods)
            {
                MaxScanCompleted = Math.Max(MaxScanCompleted, value.Completed);
            }
            else if (value.Phase == AutoSortPhase.VanillaIndex)
            {
                MaxVanillaScanned = Math.Max(MaxVanillaScanned, value.Completed);
            }
        }
    }

    private sealed class CancellingProgress : IProgress<AutoSortProgress>
    {
        private readonly CancellationTokenSource _cts;

        public CancellingProgress(CancellationTokenSource cts) => _cts = cts;

        public void Report(AutoSortProgress value)
        {
            if (value.Phase == AutoSortPhase.ScanningMods && value.Completed >= 1)
            {
                _cts.Cancel();
            }
        }
    }

    private static string Names(IReadOnlyList<PlaySetMod> rows) => string.Join(",", rows.Select(r => r.Name));

    private static string NewTempRoot(string tag)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdxnext-autosort-" + tag + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static GameInstallation NewGame(IFileSystem fs, string root)
    {
        var installRoot = Path.Combine(root, "game-root");
        var gameDataDir = Path.Combine(root, "gamedata");
        fs.EnsureDirectory(Path.Combine(installRoot, "game"));
        fs.EnsureDirectory(gameDataDir);

        return new GameInstallation(
            GameId: "test",
            DisplayName: "Test Game",
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher", "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: gameDataDir,
            DlcPath: Path.Combine(installRoot, "dlc"),
            ModDir: Path.Combine(gameDataDir, "mod"),
            ExePath: Path.Combine(installRoot, "test.exe"),
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
            SteamAppId: null,
            Support: GameSupport.Full);
    }

    private static void WriteVanilla(IFileSystem fs, GameInstallation game, params string[] relativePaths)
    {
        var contentRoot = Path.Combine(game.InstallRoot, "game");
        foreach (var relativePath in relativePaths)
        {
            fs.WriteAllTextAtomic(Path.Combine(contentRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)), "vanilla");
        }
    }

    private static PlaySetMod NewMod(IFileSystem fs, string root, string folder, string name, params string[] relativePaths)
    {
        var dirPath = Path.Combine(root, "mods", folder);
        foreach (var relativePath in relativePaths)
        {
            fs.WriteAllTextAtomic(Path.Combine(dirPath, relativePath.Replace('/', Path.DirectorySeparatorChar)), "mod");
        }

        return new PlaySetMod(
            ModId: Guid.NewGuid(),
            Name: name,
            DirPath: dirPath,
            ArchivePath: null,
            GameRegistryId: "",
            ThumbnailPath: null,
            RequiredVersion: "",
            Status: "",
            Enabled: true,
            Position: 0,
            SteamId: null,
            Source: "test");
    }

    private static void CreateDependencyTable(IFileSystem fs, GameInstallation game)
    {
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        fs.EnsureDirectory(Path.GetDirectoryName(databasePath) ?? game.GameDataDir);
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "CREATE TABLE IF NOT EXISTS mods_dependencies (modId char(36) not null, modVersion varchar(255) not null, " +
            "dependencyType text not null, dependencyId char(36) not null, dependencyVersion varchar(255), " +
            "dependencyName varchar(255), primary key (modId, modVersion, dependencyId))");
    }

    private static void InsertDependencyRow(IFileSystem fs, GameInstallation game, Guid modId, Guid dependencyId, string dependencyName)
    {
        var databasePath = PlaySetRepository.GetDatabasePath(game);
        using var connection = SqliteConnection.OpenOrCreate(databasePath);
        connection.Execute(
            "INSERT INTO mods_dependencies (modId, modVersion, dependencyType, dependencyId, dependencyVersion, dependencyName) " +
            "VALUES (?, ?, ?, ?, ?, ?)",
            modId.ToString(), "1", "mod", dependencyId.ToString(), null, dependencyName);
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
