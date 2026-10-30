namespace PdxLauncher.Checks;

using System.Text;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Launching;
using PdxLauncher.Core.LoadFiles;
using PdxLauncher.Core.Signatures;

/// <summary>
/// T3 core-loadlaunch 自检：三格式加载文件序列化（逐字节 golden）、加载文件读写往返、
/// GameLauncher 路径/参数、ProcessLauncher 行为、ContinueGameReader、DlcSignatureService。
/// </summary>
public static class LoadFileChecks
{
    private const string Ck3DlcLoadMd5 = "d44d673a81c5d8bfb62d0a0bbd0364da";
    private const string StellarisDlcLoadMd5 = "45494607b40e9dd60667fe29deb73f43";
    private const string Victoria3ContentLoadMd5 = "fdef3e859c807021eb6aa8c66d2720d8";

    private const string Ck3ContinueTitle = "芒斯特小国王，穆尔哈德_1066_09_29";
    private const string Ck3ContinueRawVersion = "1.19.0.6";
    private const string Victoria3ContinueTitle = "autosave";
    private const string StellarisContinueTitle = "save games/2_1757314770/2273.12.13";
    private const string StellarisContinueDescription = "凯尔赞枢纽 2";

    /// <summary>测试用 MachineGuid（真机值，用于可复现的签名断言）。</summary>
    private const string TestMachineGuid = "96e94400-f07f-440d-9979-6a1e82468b15";

    /// <summary>两个 fixture DLC 文件按 name 排序后的确定性签名（机器无关）。</summary>
    private const string FixtureDlcSignature = "ecadb59484d6680d966b894c428b5f72";

    /// <summary>空 DLC 列表 + 真实 MachineGuid 的签名（验证 255 字节缓冲补齐）。</summary>
    private const string EmptyDlcSignatureWithRealMachineGuid = "56e614cf5454369d1194e1b2243b4d99";

    /// <summary>两个合成 DLC（A、B，按 name 升序）的确定性签名。</summary>
    private const string SyntheticDlcSignature = "e5945a911889df5dcdbab770303d7c80";

    /// <summary>本机 Stellaris 的 Steam appmanifest（用于真机签名断言，缺失则跳过）。</summary>
    private const string StellarisAppManifestPath = @"F:\SteamLibrary\steamapps\appmanifest_281990.acf";

    /// <summary>本机 Stellaris 的 DLC 目录（用于真机签名断言，缺失则跳过）。</summary>
    private const string StellarisDlcRoot = @"F:\SteamLibrary\steamapps\common\Stellaris\dlc";

    /// <summary>本机 Stellaris 的 dlc_signature 文件（33 字节，尾部带 NUL）。</summary>
    private const string StellarisSignaturePath =
        @"C:\Users\17147\Documents\Paradox Interactive\Stellaris\dlc_signature";

    /// <summary>空 DLC 列表 + 空 machineId 的签名。</summary>
    private const string EmptyDlcSignatureWithoutMachineGuid = "ea00a9bca84d7ce5745104abe1834263";

    /// <summary>运行全部断言；返回 0 表示全部通过。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckGoldenFiles(fs, logger);
        CheckV0RoundTrip(fs, logger);
        CheckV1_1ModGrouping(fs, logger);
        CheckLoadFilePlan(fs, logger);
        CheckWriteBackupAndAtomic(fs, logger);
        CheckReadTolerance(fs, logger);
        CheckGameLauncher(fs, logger);
        CheckContinueGameReader(fs, logger);
        CheckDlcSignature(fs);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- golden

    /// <summary>
    /// 硬指标：三个真机加载文件必须先被解析成 LoadFileContent，再由 Serialize 重新生成，
    /// 结果与磁盘上的原文件逐字节相等。
    /// </summary>
    private static void CheckGoldenFiles(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] 三格式加载文件逐字节 golden");

        CheckGolden(fs, logger, "ck3/dlc_load.json", LoadFileFormat.DlcLoadV0, Ck3DlcLoadMd5);
        CheckGolden(fs, logger, "stellaris/dlc_load.json", LoadFileFormat.DlcLoadV0, StellarisDlcLoadMd5);
        CheckGolden(
            fs,
            logger,
            "victoria3/content_load.json",
            LoadFileFormat.ContentLoadV1_1,
            Victoria3ContentLoadMd5);
    }

    private static void CheckGolden(
        IFileSystem fs,
        IAppLogger logger,
        string fixtureRelative,
        LoadFileFormat format,
        string expectedMd5)
    {
        var fixturePath = ChecksContext.FixturePath(fixtureRelative);
        var original = fs.ReadAllBytes(fixturePath);
        if (original.Length == 0)
        {
            ChecksContext.Check("读取 fixture " + fixtureRelative, false, fixturePath);
            return;
        }

        // fixture 直接放在临时目录里，让 ReadAsync/Serialize 走真实的磁盘键序逻辑。
        var tempRoot = NewTempRoot("golden");
        var gameDataDir = fs.Combine(tempRoot, "data");
        try
        {
            fs.EnsureDirectory(gameDataDir);
            var fileName = LoadFileWriter.GetFileName(format);
            fs.WriteAllBytesAtomic(fs.Combine(gameDataDir, fileName), original);

            var game = MakeGame(format, gameDataDir);
            var writer = new LoadFileWriter(fs, logger);
            var content = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            var json = writer.Serialize(game, content);
            var actual = Encoding.UTF8.GetBytes(json);

            ChecksContext.EqualBytes("golden " + fixtureRelative, original, actual);
            ChecksContext.Equal("golden md5 " + fixtureRelative, expectedMd5, ChecksContext.Md5(actual));

            // 额外检查：中文不转义、无 BOM、无换行、无 formatVersion 键。
            ChecksContext.Check(
                "golden " + fixtureRelative + " 不含转义中文",
                !json.Contains("\\u", StringComparison.Ordinal),
                json.Length > 120 ? json.Substring(0, 120) : json);
            ChecksContext.Check("golden " + fixtureRelative + " 不含换行", json.IndexOf('\n') < 0 && json.IndexOf('\r') < 0);
            ChecksContext.Check(
                "golden " + fixtureRelative + " 不写 formatVersion",
                !json.Contains("formatVersion", StringComparison.Ordinal));
            ChecksContext.Check(
                "golden " + fixtureRelative + " 无 BOM",
                actual.Length < 3 || !(actual[0] == 0xEF && actual[1] == 0xBB && actual[2] == 0xBF));
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- V0 往返

    private static void CheckV0RoundTrip(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] V0 dlc_load.json 键序与内容往返");

        var tempRoot = NewTempRoot("v0");
        var gameDataDir = fs.Combine(tempRoot, "data");
        try
        {
            fs.EnsureDirectory(gameDataDir);
            var game = MakeGame(LoadFileFormat.DlcLoadV0, gameDataDir);
            var writer = new LoadFileWriter(fs, logger);

            // 空目录（文件不存在）→ 默认键序 disabled_dlcs, enabled_mods。
            ChecksContext.Equal("V0 文件名", "dlc_load.json", LoadFileWriter.GetFileName(LoadFileFormat.DlcLoadV0));
            ChecksContext.Equal(
                "V0 默认键序",
                "{\"disabled_dlcs\":[],\"enabled_mods\":[]}",
                writer.Serialize(game, LoadFileContent.Empty));

            var content = new LoadFileContent(
                new[] { "dlc/dlc001.dlc", "dlc/dlc002.dlc" },
                Array.Empty<string>(),
                new[] { "mod/ugc_1.mod", "mod/猎艳寻欢.mod" },
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>());
            var json = writer.Serialize(game, content);
            ChecksContext.Equal(
                "V0 内容序列化",
                "{\"disabled_dlcs\":[\"dlc/dlc001.dlc\",\"dlc/dlc002.dlc\"],"
                + "\"enabled_mods\":[\"mod/ugc_1.mod\",\"mod/猎艳寻欢.mod\"]}",
                json);

            // 写盘 → 读回 → 内容一致。
            writer.WriteAsync(game, content, CancellationToken.None).GetAwaiter().GetResult();
            var path = fs.Combine(gameDataDir, "dlc_load.json");
            ChecksContext.Check("V0 写盘成功", fs.FileExists(path), path);
            ChecksContext.EqualBytes("V0 写盘内容", Encoding.UTF8.GetBytes(json), fs.ReadAllBytes(path));

            var reloaded = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("V0 读回 disabled", 2, reloaded.DisabledDlcFilePaths.Count);
            ChecksContext.Equal("V0 读回 enabled_mods", 2, reloaded.EnabledModRegistryIds.Count);
            ChecksContext.Equal("V0 读回中文 mod", "mod/猎艳寻欢.mod", reloaded.EnabledModRegistryIds[1]);

            // 已有文件键序反了 → 必须沿用现有键序。
            fs.WriteAllTextAtomic(path, "{\"enabled_mods\":[],\"disabled_dlcs\":[]}");
            var reordered = writer.Serialize(game, LoadFileContent.Empty);
            ChecksContext.Equal(
                "V0 沿用现有键序",
                "{\"enabled_mods\":[],\"disabled_dlcs\":[]}",
                reordered);

            // 写入时应生成 .bak。
            writer.WriteAsync(game, content, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("V0 写入生成 .bak", fs.FileExists(path + ".bak"), path + ".bak");
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- V1 / V1_1

    private static void CheckV1_1ModGrouping(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] V1 / V1_1 content_load.json 分组与键序");

        var tempRoot = NewTempRoot("v1");
        var gameDataDir = fs.Combine(tempRoot, "data");
        try
        {
            fs.EnsureDirectory(gameDataDir);
            var writer = new LoadFileWriter(fs, logger);

            ChecksContext.Equal("V1 文件名", "content_load.json", LoadFileWriter.GetFileName(LoadFileFormat.ContentLoadV1));
            ChecksContext.Equal(
                "V1_1 文件名",
                "content_load.json",
                LoadFileWriter.GetFileName(LoadFileFormat.ContentLoadV1_1));

            var v1Game = MakeGame(LoadFileFormat.ContentLoadV1, gameDataDir);
            var v1Content = new LoadFileContent(
                Array.Empty<string>(),
                new[] { "ck3_dlc001_preorder" },
                Array.Empty<string>(),
                new[] { "C:\\mods\\local_dir" },
                new[] { "C:\\mods\\pack.zip" },
                new[] { "C:\\workshop\\ugc1" });
            ChecksContext.Equal(
                "V1 序列化（不写 dirPath 组）",
                "{\"enabledUGC\":[{\"path\":\"C:\\\\workshop\\\\ugc1\"}],"
                + "\"disabledDLC\":[{\"paradoxAppId\":\"ck3_dlc001_preorder\"}],"
                + "\"enabledMods\":[{\"path\":\"C:\\\\mods\\\\pack.zip\"}]}",
                writer.Serialize(v1Game, v1Content));

            // V1_1：先 dirPath 组，再 archivePath 组（两组拼接，不是全局排序）。
            var v11Game = MakeGame(LoadFileFormat.ContentLoadV1_1, gameDataDir);
            var v11Content = new LoadFileContent(
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<string>(),
                new[] { "D:\\mods\\zzz_dir", "D:\\mods\\aaa_dir" },
                new[] { "D:\\mods\\mmm.zip" },
                Array.Empty<string>());
            var v11Json = writer.Serialize(v11Game, v11Content);
            ChecksContext.Equal(
                "V1_1 dirPath 组在前、archivePath 组在后",
                "{\"enabledUGC\":[],\"disabledDLC\":[],"
                + "\"enabledMods\":[{\"path\":\"D:\\\\mods\\\\zzz_dir\"},{\"path\":\"D:\\\\mods\\\\aaa_dir\"},"
                + "{\"path\":\"D:\\\\mods\\\\mmm.zip\"}]}",
                v11Json);

            // 已有文件键序不同 → 沿用。
            fs.WriteAllTextAtomic(
                fs.Combine(gameDataDir, "content_load.json"),
                "{\"enabledMods\":[],\"enabledUGC\":[],\"disabledDLC\":[]}");
            ChecksContext.Equal(
                "V1_1 沿用现有键序",
                "{\"enabledMods\":[],\"enabledUGC\":[],\"disabledDLC\":[]}",
                writer.Serialize(v11Game, LoadFileContent.Empty));
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- 跳过规则

    /// <summary>
    /// <see cref="PlaySetEditor.BuildLoadFileContent"/> 现在返回「内容 + 被跳过的行」。
    /// <para>
    /// 跳过本身是各格式的固有局限（V0 只认 registryId、V1/V1.1 只认 archivePath 或 dirPath），
    /// 以前静默发生、玩家只看到「启用了但游戏里没有」。这里钉住两件事：
    /// <list type="number">
    /// <item>不满足条件的行确实被跳过，并且出现在 <c>Skipped</c> 里。</item>
    /// <item>满足条件的行照样进内容，跳过判定没有改变输出（V1.1 仍是先 dirPath 组、后 archivePath 组）。</item>
    /// </list>
    /// </para>
    /// </summary>
    private static void CheckLoadFilePlan(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] 加载文件跳过规则（LoadFilePlan）");

        var tempRoot = NewTempRoot("plan");
        try
        {
            var gameDataDir = fs.Combine(tempRoot, "data");
            fs.EnsureDirectory(gameDataDir);

            // V0（CK3）：没有 gameRegistryId 的行写不进去。
            var v0Game = MakeGame(LoadFileFormat.DlcLoadV0, gameDataDir);
            var v0Mods = new List<PlaySetMod>
            {
                MakeMod("mod/a.mod", @"C:\mods\a", null, enabled: true),
                MakeMod(string.Empty, @"C:\mods\b", null, enabled: true),
                MakeMod("mod/c.mod", @"C:\mods\c", null, enabled: false),
            };
            var v0Plan = PlaySetEditor.BuildLoadFileContent(v0Game, v0Mods, [], []);
            ChecksContext.Equal("V0 只写有 registryId 的行", 1, v0Plan.Content.EnabledModRegistryIds.Count);
            ChecksContext.Equal("V0 的 registryId", "mod/a.mod", v0Plan.Content.EnabledModRegistryIds[0]);
            ChecksContext.Equal("V0 跳过 1 行", 1, v0Plan.Skipped.Count);
            ChecksContext.Equal("V0 跳过的是缺 registryId 那行", @"C:\mods\b", v0Plan.Skipped[0].DirPath);
            ChecksContext.Check("停用的行不算被跳过", !v0Plan.Skipped.Any(m => m.Name == "mod_c"));

            // V1（只认 archivePath）：目录型本地 mod 会被丢掉。
            var v1Game = MakeGame(LoadFileFormat.ContentLoadV1, gameDataDir);
            var v1Mods = new List<PlaySetMod>
            {
                MakeMod("mod/a.mod", @"C:\mods\dir_a", null, enabled: true),
                MakeMod("mod/b.mod", null, @"C:\mods\pack_b.zip", enabled: true),
            };
            var v1Plan = PlaySetEditor.BuildLoadFileContent(v1Game, v1Mods, [], []);
            ChecksContext.Equal("V1 只写压缩包型", 1, v1Plan.Content.EnabledModArchivePaths.Count);
            ChecksContext.Equal("V1 的压缩包路径", @"C:\mods\pack_b.zip", v1Plan.Content.EnabledModArchivePaths[0]);
            ChecksContext.Equal("V1 跳过 1 行", 1, v1Plan.Skipped.Count);
            ChecksContext.Equal("V1 跳过的是目录型", @"C:\mods\dir_a", v1Plan.Skipped[0].DirPath);

            // V1.1（V3）：两种都能表达，但路径全空的行仍写不出来。
            var v11Game = MakeGame(LoadFileFormat.ContentLoadV1_1, gameDataDir, gameId: "victoria3");
            var v11Mods = new List<PlaySetMod>
            {
                MakeMod(string.Empty, @"C:\mods\dir_c", null, enabled: true),
                MakeMod(string.Empty, null, @"C:\mods\pack_d.zip", enabled: true),
                MakeMod(string.Empty, null, null, enabled: true),
            };
            var v11Plan = PlaySetEditor.BuildLoadFileContent(v11Game, v11Mods, [], []);
            ChecksContext.Equal("V1.1 目录型进 dirPaths", 1, v11Plan.Content.EnabledModDirPaths.Count);
            ChecksContext.Equal("V1.1 压缩包型进 archivePaths", 1, v11Plan.Content.EnabledModArchivePaths.Count);
            ChecksContext.Equal("V1.1 仍然 enabledUGC 为空", 0, v11Plan.Content.EnabledUgcPaths.Count);
            ChecksContext.Equal("V1.1 跳过 1 行（无路径）", 1, v11Plan.Skipped.Count);
            ChecksContext.Equal("V1.1 跳过的是无路径那行", "mod_missing", v11Plan.Skipped[0].Name);

            // 顺序：先 dirPath 组，再 archivePath 组（两组拼接，不是全局排序）。
            var json = new LoadFileWriter(fs, logger).Serialize(v11Game, v11Plan.Content);
            ChecksContext.Check(
                "V1.1 序列化里 dirPaths 在 archivePaths 之前",
                json.IndexOf(@"dir_c", StringComparison.Ordinal) < json.IndexOf(@"pack_d.zip", StringComparison.Ordinal),
                json);

            // CS2 没有加载文件：既不写内容也不报跳过。
            var cs2Game = MakeGame(LoadFileFormat.PlaysetConfigV1, gameDataDir, gameId: "cities_skylines_2");
            var cs2Plan = PlaySetEditor.BuildLoadFileContent(cs2Game, v11Mods, [], []);
            ChecksContext.Equal("CS2 不写任何内容", 0, cs2Plan.Content.EnabledModDirPaths.Count + cs2Plan.Content.EnabledModArchivePaths.Count);
            ChecksContext.Equal("CS2 也不报跳过", 0, cs2Plan.Skipped.Count);
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    /// <summary>造一行播放集 mod（自检专用，字段够用即可）。</summary>
    private static PlaySetMod MakeMod(string registryId, string? dirPath, string? archivePath, bool enabled) =>
        new(
            ModId: Guid.NewGuid(),
            Name: dirPath is null && archivePath is null ? "mod_missing" : "mod_" + (registryId.Length + (dirPath?.Length ?? 0)),
            DirPath: dirPath ?? string.Empty,
            ArchivePath: archivePath,
            GameRegistryId: registryId,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Enabled: enabled,
            Position: 0);

    // ---------------------------------------------------------------- 写入/读取容错

    private static void CheckWriteBackupAndAtomic(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] 写入备份与临时文件清理");

        var tempRoot = NewTempRoot("write");
        var gameDataDir = fs.Combine(tempRoot, "data");
        try
        {
            fs.EnsureDirectory(gameDataDir);
            var game = MakeGame(LoadFileFormat.DlcLoadV0, gameDataDir);
            var writer = new LoadFileWriter(fs, logger);
            var path = fs.Combine(gameDataDir, "dlc_load.json");

            fs.WriteAllTextAtomic(path, "{\"disabled_dlcs\":[\"old\"],\"enabled_mods\":[]}");
            writer.WriteAsync(game, LoadFileContent.Empty, CancellationToken.None).GetAwaiter().GetResult();

            ChecksContext.Check("写入后无残留 .tmp", !fs.FileExists(path + ".tmp"), path + ".tmp");
            ChecksContext.Check("写入后 .bak 存在", fs.FileExists(path + ".bak"));
            ChecksContext.Equal(
                "写入后 .bak 是上一版内容",
                "{\"disabled_dlcs\":[\"old\"],\"enabled_mods\":[]}",
                fs.ReadAllText(path + ".bak"));
            ChecksContext.Equal(
                "写入后正式文件是新内容",
                "{\"disabled_dlcs\":[],\"enabled_mods\":[]}",
                fs.ReadAllText(path));

            // 目标目录不存在时也要能写（EnsureDirectory）。
            var freshDir = fs.Combine(tempRoot, "fresh", "nested");
            var freshGame = MakeGame(LoadFileFormat.DlcLoadV0, freshDir);
            writer.WriteAsync(freshGame, LoadFileContent.Empty, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check(
                "目录不存在时自动创建",
                fs.FileExists(fs.Combine(freshDir, "dlc_load.json")));
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    private static void CheckReadTolerance(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] 加载文件读取容错");

        var tempRoot = NewTempRoot("read");
        var gameDataDir = fs.Combine(tempRoot, "data");
        try
        {
            fs.EnsureDirectory(gameDataDir);
            var game = MakeGame(LoadFileFormat.DlcLoadV0, gameDataDir);
            var writer = new LoadFileWriter(fs, logger);
            var path = fs.Combine(gameDataDir, "dlc_load.json");

            var missing = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("文件不存在返回空内容", 0, missing.DisabledDlcFilePaths.Count);

            fs.WriteAllTextAtomic(path, "{ this is not json");
            var broken = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("损坏 JSON 返回空内容", 0, broken.EnabledModRegistryIds.Count);

            fs.WriteAllTextAtomic(path, "[]");
            var notObject = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("根节点非对象返回空内容", 0, notObject.EnabledModRegistryIds.Count);

            // 缺键 / 元素类型不对：容错跳过，不抛。
            fs.WriteAllTextAtomic(
                path,
                "{\"disabled_dlcs\":[\"a\",3,null,\"b\"],\"enabled_mods\":\"oops\"}");
            var tolerant = writer.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("字符串数组中非字符串元素被跳过", 2, tolerant.DisabledDlcFilePaths.Count);
            ChecksContext.Equal("类型不对的键按空数组处理", 0, tolerant.EnabledModRegistryIds.Count);
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- GameLauncher

    private static void CheckGameLauncher(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] GameLauncher 路径与参数");

        var tempRoot = NewTempRoot("launch");
        try
        {
            var installRoot = fs.Combine(tempRoot, "Crusader Kings III");
            var binaries = fs.Combine(installRoot, "binaries");
            fs.EnsureDirectory(binaries);
            var exePath = fs.Combine(binaries, "ck3.exe");
            fs.WriteAllTextAtomic(exePath, "stub");

            var game = MakeGame(LoadFileFormat.DlcLoadV0, fs.Combine(tempRoot, "data")) with
            {
                InstallRoot = installRoot,
                ExePath = "binaries/ck3.exe",
                ExeArgs = new[] { "-gdpr-compliant" },
            };

            var recorder = new RecordingProcessLauncher();
            var launcher = new GameLauncher(recorder, fs, logger);

            ChecksContext.Equal(
                "GameLauncher 绝对化 exePath",
                fs.GetFullPath(exePath),
                launcher.ResolveExecutablePath(game, LaunchRequest.Default));

            ChecksContext.Equal(
                "GameLauncher 参数：默认只有 exeArgs",
                "-gdpr-compliant",
                string.Join(" ", launcher.BuildArguments(game, LaunchRequest.Default)));

            var continued = new LaunchRequest(null, null, ContinueLastSave: true);
            ChecksContext.Equal(
                "GameLauncher 参数：继续游戏追加 --continuelastsave（在 exeArgs 之前）",
                "--continuelastsave -gdpr-compliant",
                string.Join(" ", launcher.BuildArguments(game, continued)));

            var custom = new LaunchRequest("binaries/ck3.exe", new[] { "-debug_mode" }, ContinueLastSave: true);
            ChecksContext.Equal(
                "GameLauncher 参数顺序：自定义 → 继续 → exeArgs",
                "-debug_mode --continuelastsave -gdpr-compliant",
                string.Join(" ", launcher.BuildArguments(game, custom)));

            var result = launcher
                .LaunchAsync(game, LaunchRequest.Default, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("GameLauncher 启动成功", result.Started, result.ErrorMessage);
            ChecksContext.Equal("GameLauncher 传入 exePath", fs.GetFullPath(exePath), recorder.LastExePath ?? "(null)");
            ChecksContext.Equal("GameLauncher 传入工作目录", binaries, recorder.LastWorkingDirectory ?? "(null)");
            ChecksContext.Equal("GameLauncher 传入参数个数", 1, recorder.LastArgs.Count);
            ChecksContext.Equal("GameLauncher 传入参数值", "-gdpr-compliant", recorder.LastArgs[0]);

            // exe 不存在 → 失败结果，不能抛。
            var missingGame = game with { ExePath = "binaries/nope.exe" };
            var missingResult = launcher
                .LaunchAsync(missingGame, LaunchRequest.Default, CancellationToken.None)
                .GetAwaiter()
                .GetResult();
            ChecksContext.Check("exe 不存在返回失败", !missingResult.Started);
            ChecksContext.Equal(
                "exe 不存在错误信息",
                "找不到可执行文件: " + fs.GetFullPath(fs.Combine(installRoot, "binaries/nope.exe")),
                missingResult.ErrorMessage ?? "(null)");

            // 自定义 exePath 优先。
            ChecksContext.Equal(
                "LaunchRequest.CustomExePath 优先",
                fs.GetFullPath(fs.Combine(installRoot, "binaries/other.exe")),
                launcher.ResolveExecutablePath(game, new LaunchRequest("binaries/other.exe", null, false)));
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- ContinueGameReader

    private static void CheckContinueGameReader(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[LoadFileChecks] continue_game.json 读取（真机值）");

        var reader = new ContinueGameReader(fs, logger);

        var ck3 = reader
            .ReadAsync(MakeGame(LoadFileFormat.DlcLoadV0, ChecksContext.FixturePath("ck3")), CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Check("CK3 读到继续游戏信息", ck3 is not null);
        ChecksContext.Equal("CK3 continue title", Ck3ContinueTitle, ck3?.Title ?? "(null)");
        ChecksContext.Equal("CK3 continue rawGameVersion", Ck3ContinueRawVersion, ck3?.RawGameVersion ?? "(null)");
        ChecksContext.Check("CK3 continue 有 description", !string.IsNullOrEmpty(ck3?.Description));

        var v3 = reader
            .ReadAsync(
                MakeGame(LoadFileFormat.ContentLoadV1_1, ChecksContext.FixturePath("victoria3")),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("Victoria 3 continue title", Victoria3ContinueTitle, v3?.Title ?? "(null)");
        ChecksContext.Equal("Victoria 3 continue rawGameVersion", "1.12.2", v3?.RawGameVersion ?? "(null)");

        var stellaris = reader
            .ReadAsync(
                MakeGame(LoadFileFormat.DlcLoadV0, ChecksContext.FixturePath("stellaris")),
                CancellationToken.None)
            .GetAwaiter()
            .GetResult();
        ChecksContext.Equal("Stellaris continue title", StellarisContinueTitle, stellaris?.Title ?? "(null)");
        ChecksContext.Equal(
            "Stellaris continue description",
            StellarisContinueDescription,
            stellaris?.Description ?? "(null)");
        ChecksContext.Check(
            "Stellaris continue 没有 rawGameVersion 键（容错为 null）",
            stellaris?.RawGameVersion is null,
            stellaris?.RawGameVersion ?? "(null)");

        // 容错：不存在 / 损坏 / 无 title。
        var tempRoot = NewTempRoot("continue");
        try
        {
            var gameDataDir = fs.Combine(tempRoot, "data");
            fs.EnsureDirectory(gameDataDir);
            var game = MakeGame(LoadFileFormat.DlcLoadV0, gameDataDir);

            ChecksContext.Check(
                "continue_game.json 不存在返回 null",
                reader.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult() is null);

            var path = fs.Combine(gameDataDir, ContinueGameReader.FileName);
            fs.WriteAllTextAtomic(path, "{ broken");
            ChecksContext.Check(
                "continue_game.json 损坏返回 null",
                reader.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult() is null);

            fs.WriteAllTextAtomic(path, "{\"desc\":\"x\"}");
            ChecksContext.Check(
                "continue_game.json 缺 title 返回 null",
                reader.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult() is null);

            fs.WriteAllTextAtomic(path, "{\"title\":\"t\",\"rawGameVersion\":123}");
            var mixed = reader.ReadAsync(game, CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Equal("非字符串字段容错为 null", "t", mixed?.Title ?? "(null)");
            ChecksContext.Check("rawGameVersion 类型不对 → null", mixed?.RawGameVersion is null);
        }
        finally
        {
            CleanupTempRoot(fs, tempRoot);
        }
    }

    // ---------------------------------------------------------------- DLC 签名

    private static void CheckDlcSignature(IFileSystem fs)
    {
        Console.WriteLine("[LoadFileChecks] dlc_signature 算法");

        // 官方 bit() 归一化：n 去空白小写后，结果 = (n + "\0") + 交错串(n 每字符后插 '\0') 的后半截，长度恒为 2n。
        ChecksContext.Equal("MachineId 归一化（abc）", "abc\u0000c\u0000", DlcSignatureService.NormalizeMachineId("abc"));
        ChecksContext.Equal(
            "MachineId 归一化去空白并小写",
            "96e\u0000e\u0000",
            DlcSignatureService.NormalizeMachineId(" 96E \r\n"));
        ChecksContext.Equal("空 MachineId 归一化", string.Empty, DlcSignatureService.NormalizeMachineId(null));

        var normalizedGuid = DlcSignatureService.NormalizeMachineId(TestMachineGuid);
        ChecksContext.Equal("真机 MachineGuid 归一化长度（72 字符）", 72, normalizedGuid.Length);
        ChecksContext.Equal("归一化结果以原 GUID 首字符开头", '9', normalizedGuid[0]);
        ChecksContext.Equal("归一化结果第 37 个字符是 NUL", '\0', normalizedGuid[36]);

        ChecksContext.Equal("签名 salt 明文", "DontStealMyGamePlz__WINNERS_DONT_USE_DRUGS__DONT_COPY_THAT_FLOPPY", DlcSignatureService.SignatureSalt);
        ChecksContext.Equal("machineId 缓冲区大小", 255, DlcSignatureService.MachineIdBufferSize);

        var emptySignature = DlcSignatureService.ComputeSignature(TestMachineGuid, Array.Empty<DlcInfo>());
        ChecksContext.Equal(
            "空 DLC + 真机 MachineGuid 签名（与 V3 磁盘 dlc_signature 一致）",
            EmptyDlcSignatureWithRealMachineGuid,
            emptySignature);
        ChecksContext.Equal("签名长度 32", 32, emptySignature.Length);
        ChecksContext.Check(
            "签名全小写十六进制",
            emptySignature.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')),
            emptySignature);
        ChecksContext.Equal(
            "空 DLC + 空 MachineGuid 签名",
            EmptyDlcSignatureWithoutMachineGuid,
            DlcSignatureService.ComputeSignature(null, Array.Empty<DlcInfo>()));

        // 255 字节缓冲：归一化后超过 255 字节的部分被截断（400 字符 → 800 字节 ⇒ 截到 255 字节）。
        ChecksContext.Equal(
            "machineId 超过 255 字节时被截断",
            DlcSignatureService.ComputeSignature(new string('a', 300), Array.Empty<DlcInfo>()),
            DlcSignatureService.ComputeSignature(new string('a', 400), Array.Empty<DlcInfo>()));

        // 用两个 fixture 的原始字节做确定性断言（不依赖本机 DLC 安装）。
        var ck3Bytes = ReadFixtureBytes(fs, "ck3/dlc001.dlc");
        var stellarisBytes = ReadFixtureBytes(fs, "stellaris/dlc001.dlc");
        ChecksContext.Check("读取 ck3/dlc001.dlc", ck3Bytes.Length > 0);
        ChecksContext.Check("读取 stellaris/dlc001.dlc", stellarisBytes.Length > 0);
        ChecksContext.Equal("ck3/dlc001.dlc 字节数", 264, ck3Bytes.Length);
        ChecksContext.Equal("stellaris/dlc001.dlc 字节数", 392, stellarisBytes.Length);

        var dlcs = new[]
        {
            new DlcInfo("Symbols of Domination", "symbols_of_domination", "447680", "dlc002/x.dlc", "t.png", string.Empty, stellarisBytes),
            new DlcInfo("Garments of the Holy Roman Empire", "ck3_dlc001_preorder", "1296730", "dlc001/x.dlc", "t.png", string.Empty, ck3Bytes),
        };

        var signature = DlcSignatureService.ComputeSignature(TestMachineGuid, dlcs);
        ChecksContext.Equal("两个 fixture DLC 的确定性签名", FixtureDlcSignature, signature);

        // 排序按 DLC name（不是文件名/路径）：调换输入顺序结果必须一致。
        var reversed = new[] { dlcs[1], dlcs[0] };
        ChecksContext.Equal(
            "签名按 DLC name 排序（与输入顺序无关）",
            signature,
            DlcSignatureService.ComputeSignature(TestMachineGuid, reversed));

        // 不同 DLC 集合必须得到不同签名。
        ChecksContext.Check(
            "不同 DLC 集合签名不同",
            DlcSignatureService.ComputeSignature(TestMachineGuid, new[] { dlcs[0] }) != signature);

        // 两个合成 DLC：排序顺序 A 在 B 前（按 name 升序），输入顺序故意倒置以同时验证排序。
        var syntheticA = Encoding.UTF8.GetBytes("name = \"A\"\r\npath = \"x\"\r\n");
        var syntheticB = Encoding.UTF8.GetBytes("name = \"B\"\r\nsteam_id = \"1\"\r\n");
        var synthetic = new[]
        {
            new DlcInfo("B", string.Empty, "1", "b.dlc", string.Empty, string.Empty, syntheticB),
            new DlcInfo("A", string.Empty, "0", "a.dlc", string.Empty, string.Empty, syntheticA),
        };
        ChecksContext.Equal(
            "两个合成 DLC 的确定性签名（A 在 B 前）",
            SyntheticDlcSignature,
            DlcSignatureService.ComputeSignature(TestMachineGuid, synthetic));

        CheckRealStellarisSignature(fs);
    }

    /// <summary>
    /// 真机回归：本机 Stellaris 的 DLC 目录 + appmanifest + 磁盘 dlc_signature 都存在时，
    /// 必须精确复现磁盘上的 812abb4563023f364077c823548eb05b（磁盘文件 33 字节，尾部带 NUL）。
    /// </summary>
    private static void CheckRealStellarisSignature(IFileSystem fs)
    {
        if (!fs.FileExists(StellarisAppManifestPath) ||
            !fs.DirectoryExists(StellarisDlcRoot) ||
            !fs.FileExists(StellarisSignaturePath))
        {
            Console.WriteLine("[LoadFileChecks] 跳过真机 Stellaris 签名断言（缺 DLC 目录/appmanifest/签名文件）");
            return;
        }

        var machineGuid = DlcSignatureService.TryReadMachineGuid();
        if (string.IsNullOrEmpty(machineGuid))
        {
            Console.WriteLine("[LoadFileChecks] 跳过真机 Stellaris 签名断言（读取 MachineGuid 失败）");
            return;
        }

        // appmanifest_281990.acf 里的 "dlcappid" 就是本账号拥有的 DLC；官方只对已拥有的 DLC 参与签名。
        var ownedSteamIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in fs.ReadAllText(StellarisAppManifestPath).Split('\n'))
        {
            var keyIndex = line.IndexOf("\"dlcappid\"", StringComparison.OrdinalIgnoreCase);
            if (keyIndex < 0)
            {
                continue;
            }

            var digits = new StringBuilder(8);
            for (var i = keyIndex + "\"dlcappid\"".Length; i < line.Length; i++)
            {
                if (char.IsDigit(line[i]))
                {
                    digits.Append(line[i]);
                }
                else if (digits.Length > 0)
                {
                    break;
                }
            }

            if (digits.Length > 0)
            {
                ownedSteamIds.Add(digits.ToString());
            }
        }

        ChecksContext.Check("真机 Stellaris appmanifest 含 dlcappid", ownedSteamIds.Count > 0, ownedSteamIds.Count.ToString());

        var realDlcs = new List<DlcInfo>();
        foreach (var path in fs.EnumerateFiles(StellarisDlcRoot, "*.dlc", recursive: true))
        {
            var raw = fs.ReadAllBytes(path);
            if (raw.Length == 0)
            {
                continue;
            }

            var text = Encoding.UTF8.GetString(raw);
            var name = ReadDlcStringValue(text, "name");
            var steamId = ReadDlcStringValue(text, "steam_id");
            if (name is null || steamId is null || !ownedSteamIds.Contains(steamId))
            {
                continue;
            }

            realDlcs.Add(new DlcInfo(name, string.Empty, steamId, fs.GetFileName(path), string.Empty, string.Empty, raw));
        }

        ChecksContext.Check("真机 Stellaris 已拥有 DLC 数量 > 0", realDlcs.Count > 0, realDlcs.Count.ToString());

        var computed = DlcSignatureService.ComputeSignature(machineGuid, realDlcs);
        var onDisk = fs.ReadAllText(StellarisSignaturePath).TrimEnd('\0');
        ChecksContext.Equal("真机 Stellaris dlc_signature（磁盘值）", "812abb4563023f364077c823548eb05b", onDisk);
        ChecksContext.Equal("真机 Stellaris dlc_signature 复现", onDisk, computed);
    }

    /// <summary>从 Clausewitz k=v 文本里读取 <c>key = "value"</c> 形式的字符串值。</summary>
    private static string? ReadDlcStringValue(string text, string key)
    {
        foreach (var line in text.Split('\n'))
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith(key, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = trimmed.Substring(key.Length).TrimStart();
            if (rest.Length == 0 || rest[0] != '=')
            {
                continue;
            }

            var value = rest.Substring(1).Trim().Trim('"').Trim();
            return value.Length == 0 ? null : value;
        }

        return null;
    }

    // ---------------------------------------------------------------- 工具

    private static GameInstallation MakeGame(LoadFileFormat format, string gameDataDir, string gameId = "ck3") =>
        new(
            GameId: gameId,
            DisplayName: "Crusader Kings III",
            Version: "1.20.1.2",
            InstallRoot: @"C:\Games\Crusader Kings III",
            LauncherSettingsPath: @"C:\Games\Crusader Kings III\launcher\launcher-settings.json",
            Format: format,
            GameDataDir: gameDataDir,
            DlcPath: @"C:\Games\Crusader Kings III\game\dlc",
            ModDir: @"C:\Games\Crusader Kings III\game\mod",
            ExePath: @"binaries\ck3.exe",
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
                ButtonHoverTextColor: "#CCCCCC",
                FontSize: 14,
                FontWeight: 400,
                TextShadow: null,
                Source: ThemeAssetSource.BuiltInPlaceholder),
            BrowserDlcUrl: null,
            BrowserModUrl: null,
            SteamAppId: 1158310,
            Support: GameSupport.Full);

    private static byte[] ReadFixtureBytes(IFileSystem fs, string relative)
    {
        var path = ChecksContext.FixturePath(relative);
        return fs.FileExists(path) ? fs.ReadAllBytes(path) : Array.Empty<byte>();
    }

    private static string NewTempRoot(string tag) =>
        Path.Combine(Path.GetTempPath(), "pdxnext-loadlaunch-" + tag + "-" + Guid.NewGuid().ToString("N"));

    private static void CleanupTempRoot(IFileSystem fs, string tempRoot)
    {
        try
        {
            if (fs.DirectoryExists(tempRoot))
            {
                fs.Delete(tempRoot);
            }
        }
        catch (IOException)
        {
            // 清理失败不影响断言结果
        }
        catch (UnauthorizedAccessException)
        {
            // 清理失败不影响断言结果
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

    /// <summary>只记录调用参数的假进程启动器（不真的启动进程）。</summary>
    private sealed class RecordingProcessLauncher : IProcessLauncher
    {
        public string? LastExePath { get; private set; }

        public string? LastWorkingDirectory { get; private set; }

        public IReadOnlyList<string> LastArgs { get; private set; } = Array.Empty<string>();

        public Task<ProcessStartResult> StartDetachedAsync(
            string exePath,
            IReadOnlyList<string> args,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            LastExePath = exePath;
            LastArgs = args ?? Array.Empty<string>();
            LastWorkingDirectory = workingDirectory;
            return Task.FromResult(ProcessStartResult.Success(4242));
        }
    }
}
