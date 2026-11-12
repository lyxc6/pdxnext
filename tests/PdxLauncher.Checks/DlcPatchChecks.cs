namespace PdxLauncher.Checks;

using System.Security.Cryptography;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Themes;

/// <summary>
/// DLC 解锁补丁自检：内嵌素材完整性、DLC 源清单、已知原版登记表、状态判定
/// （原版 / 已打 / 他人补丁 / Steam 覆盖 / 未验证版本）、一键打补丁与移除补丁的字节级还原、
/// 备份来源存疑时的归档保护、清单落盘、只读与占用失败路径。
/// 全部在临时目录里跑，不碰真实安装目录、不联网。
/// </summary>
public static class DlcPatchChecks
{
    private const string ExpectedLoader64Sha256 =
        "f428bfe07eaf1e6f6fa1f28b6a787f93794bc5f97e8609645571b8bdc10d5385";

    private const string ExpectedLoader86Sha256 =
        "a34e7ef464373898fa659294eef80ab554417c8ae6fef860f03fb7f5ae6bb698";

    /// <summary>瘦身后的 ini 模板：只剩 [steam] / [steam_misc] / [dlc] 三个段头，DLC 由清单生成。</summary>
    private const string ExpectedIniTemplateSha256 =
        "ba1d9d4b95ed3eedb1276dbe38f170046e131ddefc875985b69d87006a67e03d";

    /// <summary>
    /// Victoria 3（appId 529340）打补丁生成出来的完整 ini。摊平后的 <c>[dlc]</c> 表包含**全部**游戏的条目，
    /// 所以任一节增删条目都会改这两个值（2026-11-08 补 7 条：26,677 → 27,059 字节）。
    /// 它钉住的是当前输出；「与重构前的大表逐字节一致」在 490 条那一代成立过，新增条目后只能用本值复核回归。
    /// </summary>
    private const string ExpectedVictoria3IniSha256 =
        "f97af4e3ef3a7a905253a2c87f16be69b151b27a829fed0e86ddd50ac0e92e9d";

    private const int ExpectedVictoria3IniLength = 27_059;

    private const int ExpectedDlcTotal = 497;

    /// <summary>Cities: Skylines II 一节条数（1.15 整合包新增 6 条创作者包 / 资料片）。</summary>
    private const int ExpectedCitiesSkylines2DlcCount = 35;

    /// <summary>Europa Universalis V 一节条数（1.15 整合包新增 Across the Pillars）。</summary>
    private const int ExpectedEuropaUniversalis5DlcCount = 6;

    private const int ExpectedVictoria3DlcCount = 23;

    private const int ExpectedDlcSources = 11;

    /// <summary>Victoria 3: State and Revolution - Immersion Pack（2026-11-05 随 1.15 上线）。</summary>
    /// <remarks>来源：https://store.steampowered.com/app/4314660/ （也可从 /dlc/529340 进入）。</remarks>
    private const int StateAndRevolutionAppId = 4314660;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        var root = Path.Combine(Path.GetTempPath(), "pdx-patch-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(root);
            CheckEmbeddedAssets(fs, logger);
            CheckDlcCatalog(fs, logger, root);
            CheckVersionRegistry();
            CheckNotDetected(fs, logger, root);
            CheckApplyAndRevert(fs, logger, root);
            CheckApplyIsIdempotent(fs, logger, root);
            CheckBinariesLayout(fs, logger, root);
            CheckCitiesSkylines2Layout(fs, logger, root);
            CheckCitiesSkylines1Layout(fs, logger, root);
            CheckKnownVersionInstall(fs, logger, root);
            CheckUnverifiedVersion(fs, logger, root);
            CheckForeignPatch(fs, logger, root);
            CheckSteamOverwrote(fs, logger, root);
            CheckBackupSuspect(fs, logger, root);
            CheckUnsupportedWithoutAppId(fs, logger, root);
            CheckLockedFile(fs, logger, root);
            CheckRevertKeepsForeignIni(fs, logger, root);
            CheckExternalAssetsOverride(fs, logger, root);
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
                // 清理失败不影响断言结果
            }
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ------------------------------------------------------------------ 内嵌素材

    private static void CheckEmbeddedAssets(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[DlcPatchChecks] 内嵌素材");
        var assets = new PatchAssets(fs, logger, Path.Combine(Path.GetTempPath(), "pdx-no-assets"));

        ChecksContext.Check("x64 loader 可读", assets.TryGetLoader(x64: true, out var x64));
        ChecksContext.Equal("x64 loader 字节数", 834_560, x64.Length);
        ChecksContext.Equal("x64 loader sha256", ExpectedLoader64Sha256, Sha256(x64));

        ChecksContext.Check("x86 loader 可读", assets.TryGetLoader(x64: false, out var x86));
        ChecksContext.Equal("x86 loader 字节数", 737_280, x86.Length);
        ChecksContext.Equal("x86 loader sha256", ExpectedLoader86Sha256, Sha256(x86));

        ChecksContext.Check("ini 模板可读", assets.TryGetIniTemplate(out var ini));
        ChecksContext.Equal("ini 模板字节数", 803, System.Text.Encoding.UTF8.GetByteCount(ini));
        ChecksContext.Equal("ini 模板 sha256", ExpectedIniTemplateSha256, Sha256(System.Text.Encoding.UTF8.GetBytes(ini)));
        ChecksContext.Check("ini 模板含 [dlc] 段头", ini.Contains("[dlc]", StringComparison.Ordinal));
        ChecksContext.Check("ini 模板含 orgapi64", ini.Contains("orgapi64 = steam_api64_o.dll", StringComparison.Ordinal));
        ChecksContext.Check(
            "ini 模板不再自带大表",
            !ini.Contains("Victoria 3: State and Revolution", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ DLC 清单

    private static void CheckDlcCatalog(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] DLC 分节清单");
        var assets = new PatchAssets(fs, logger, Path.Combine(Path.GetTempPath(), "pdx-no-assets"));
        ChecksContext.Check("DLC 清单可读", assets.TryGetDlcCatalog(out var catalog));
        ChecksContext.Equal("清单 schema", 1, catalog.Schema);
        ChecksContext.Equal("清单校验问题数", 0, catalog.Validate().Count);
        ChecksContext.Equal("清单总条数", ExpectedDlcTotal, catalog.Count);
        ChecksContext.Equal("清单游戏节数", ExpectedDlcSources, catalog.Sources.Count);

        // 每节声明的 appId 必须与 GameSteamAppIds 的映射一致，避免把 DLC 挂到错误的游戏名下。
        foreach (var source in catalog.Sources)
        {
            ChecksContext.Check("清单节 appId 与映射表一致：" + source.GameId,
                GameSteamAppIds.TryGet(source.GameId, out var mapped) && mapped == source.SteamAppId,
                source.SteamAppId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var victoria3 = catalog.ForGame("victoria3");
        ChecksContext.Equal("Victoria 3 条数", ExpectedVictoria3DlcCount, victoria3.Count);
        ChecksContext.Equal("Cities Skylines 条数", 94, catalog.ForGame("cities_skylines").Count);
        ChecksContext.Equal(
            "Cities Skylines II 条数",
            ExpectedCitiesSkylines2DlcCount,
            catalog.ForGame("cities_skylines_2").Count);
        ChecksContext.Equal(
            "Europa Universalis V 条数",
            ExpectedEuropaUniversalis5DlcCount,
            catalog.ForGame("eu5").Count);
        ChecksContext.Check(
            "State and Revolution 在 Victoria 3 名下",
            victoria3.Any(entry => entry.SteamAppId == StateAndRevolutionAppId
                && entry.Name.Contains("State and Revolution", StringComparison.Ordinal)),
            StateAndRevolutionAppId.ToString(System.Globalization.CultureInfo.InvariantCulture));

        var lines = catalog.DlcLines().ToList();
        ChecksContext.Equal("生成行数 = 条目 + 每节注释", ExpectedDlcTotal + ExpectedDlcSources, lines.Count);
        ChecksContext.Check("生成行带游戏分节注释", lines.Contains("; Victoria III (update)"));

        // 生成出来的 ini：只保留一段 [dlc]，且与重构前的大表输出逐字节一致。
        var install = NewInstall(root, "catalog", "victoria3", appId: 529340);
        fs.EnsureDirectory(Path.Combine(install.InstallRoot, "binaries"));
        fs.WriteAllBytesAtomic(Path.Combine(install.InstallRoot, "binaries", "steam_api64.dll"), FakeVanillaDll(0x10, 1024));
        var service = NewService(fs, logger, Path.Combine(root, "state-catalog.json"));
        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("Victoria 3 打完补丁", GamePatchStatus.Patched, applied.Status);

        var iniPath = Path.Combine(install.InstallRoot, "binaries", "cream_api.ini");
        var ini = fs.ReadAllText(iniPath);
        ChecksContext.Equal("生成的 ini 只有一段 [dlc]", 1, CountOccurrences(ini, "[dlc]"));
        ChecksContext.Equal("生成的 ini 字节数", ExpectedVictoria3IniLength, System.Text.Encoding.UTF8.GetByteCount(ini));
        ChecksContext.Equal(
            "生成的 ini 与重构前逐字节一致",
            ExpectedVictoria3IniSha256,
            Sha256(System.Text.Encoding.UTF8.GetBytes(ini)));
        ChecksContext.Check(
            "生成的 ini 带 State and Revolution",
            ini.Contains("4314660 = Victoria 3: State and Revolution - Immersion Pack", StringComparison.Ordinal));
        ChecksContext.Check(
            "生成的 ini 保留 appid",
            ini.Contains("appid = 529340", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ 版本登记表

    private static void CheckVersionRegistry()
    {
        Console.WriteLine("[DlcPatchChecks] 已知原版登记表");
        var registry = PatchVersionRegistry.Default;
        ChecksContext.Check("内嵌登记表加载成功", registry.LoadError is null, registry.LoadError ?? "(null)");
        ChecksContext.Check("登记表至少有 7 条真机条目", registry.Entries.Count >= 7, registry.Entries.Count.ToString());

        var victoria3 = registry.Entries.FirstOrDefault(entry => entry.GameId == "victoria3");
        ChecksContext.Check("登记表含 Victoria 3", victoria3 is not null);
        ChecksContext.Equal("Victoria 3 版本号", "1.15.0 (Ivan-Chai)", victoria3?.Version ?? "(missing)");
        ChecksContext.Equal("Victoria 3 buildid", "26453692", victoria3?.BuildId ?? "(missing)");
        ChecksContext.Equal("Victoria 3 原版字节数", 298_384, victoria3?.Size ?? 0);
        ChecksContext.Equal(
            "Victoria 3 原版 sha256",
            "1db3fd414039d3e5815a5721925dd2e0a3a9f2549603c6cab7c49b84966a1af3",
            victoria3?.Sha256 ?? "(missing)");

        var hoi4 = registry.Entries.FirstOrDefault(entry => entry.GameId == "hoi4");
        ChecksContext.Equal("HoI4 buildid", "26409260", hoi4?.BuildId ?? "(missing)");
        ChecksContext.Equal("HoI4 原版字节数", 289_568, hoi4?.Size ?? 0);

        // 用一张测试表验证匹配逻辑：游戏 / 文件名 / 大小 / sha256 缺一不可。
        var fake = FakeVanillaDll(0x40, 4096);
        var test = PatchVersionRegistry.FromJson(RegistryJson("hoi4", "test-1.2.3", "123456", "steam_api64.dll", fake));
        var matched = test.Match("hoi4", "steam_api64.dll", fake);
        ChecksContext.Check("测试登记表命中", matched is not null);
        ChecksContext.Equal("命中版本号", "test-1.2.3", matched?.Version ?? "(missing)");
        ChecksContext.Equal("命中 buildid", "123456", matched?.BuildId ?? "(missing)");
        ChecksContext.Check("同名不同游戏不命中", test.Match("ck3", "steam_api64.dll", fake) is null);
        ChecksContext.Check("不同文件名不命中", test.Match("hoi4", "steam_api.dll", fake) is null);
        ChecksContext.Check("不同字节不命中", test.Match("hoi4", "steam_api64.dll", FakeVanillaDll(0x41, 4096)) is null);
        ChecksContext.Check("空登记表什么都不命中", PatchVersionRegistry.Empty.Match("hoi4", "steam_api64.dll", fake) is null);
        ChecksContext.Check("IsKnownOriginal 认这份字节", test.IsKnownOriginal("steam_api64.dll", fake));
        ChecksContext.Check("IsKnownOriginal 不认别的文件名", !test.IsKnownOriginal("steam_api.dll", fake));

        var rejected = false;
        try
        {
            PatchVersionRegistry.FromJson(
                "{\"schema\":1,\"entries\":[{\"gameId\":\"hoi4\",\"fileName\":\"steam_api64.dll\",\"sha256\":\"zz\",\"size\":1}]}");
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        ChecksContext.Check("非法登记表被拒绝", rejected);
    }

    // ------------------------------------------------------------------ 状态

    private static void CheckNotDetected(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 没有 steam_api 文件");
        var install = NewInstall(root, "empty", "hoi4", appId: 394360);
        var service = NewService(fs, logger, Path.Combine(root, "state-notdetected.json"));

        var report = service.Inspect(install);
        ChecksContext.Equal("空目录状态", GamePatchStatus.NotDetected, report.Status);
        ChecksContext.Check("空目录不能打补丁", !report.CanApply);
        ChecksContext.Check("空目录不能移除", !report.CanRevert);
        ChecksContext.Equal<int?>("空目录也报 appId", 394360, report.SteamAppId);
    }

    private static void CheckApplyAndRevert(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 打补丁 → 还原");
        var install = NewInstall(root, "apply", "hoi4", appId: 394360);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        var original = FakeVanillaDll(0x11, 4096);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-apply.json"));
        var before = service.Inspect(install);
        ChecksContext.Equal("原版状态", GamePatchStatus.Vanilla, before.Status);
        ChecksContext.Check("原版可以打补丁", before.CanApply);
        ChecksContext.Check("原版不能移除", !before.CanRevert);
        ChecksContext.Equal("原版没有补丁文件", 0, before.PatchedFiles.Count);
        ChecksContext.Equal("测试字节不在登记表里", PatchVersionState.Unknown, before.VersionState);

        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("打完补丁状态", GamePatchStatus.Patched, applied.Status);
        ChecksContext.Check("本工具打的补丁 → loader 在岗", applied.IsLoaderActive);
        ChecksContext.Check("打完补丁可以移除", applied.CanRevert);
        ChecksContext.Equal("补丁文件数量", 1, applied.PatchedFiles.Count);
        ChecksContext.Check("没有备份来源存疑", !applied.BackupSuspect);
        ChecksContext.Check("没有产生归档", applied.BackupArchivePath is null);

        var assets = new PatchAssets(fs, logger, Path.Combine(root, "state-assets-none"));
        ChecksContext.Check("loader 素材可读", assets.TryGetLoader(x64: true, out var loader));
        ChecksContext.Check("目标已是 loader", fs.ReadAllBytes(apiPath).AsSpan().SequenceEqual(loader));
        ChecksContext.EqualBytes("备份是原文件", original, fs.ReadAllBytes(backupPath));

        var ini = fs.ReadAllText(iniPath);
        ChecksContext.Check("ini 写入 appid", ini.Contains("appid = 394360", StringComparison.Ordinal), ini.Length.ToString());
        ChecksContext.Check("ini 带 PdxNext 标记", ini.Contains("; PdxNext 自动生成", StringComparison.Ordinal));
        ChecksContext.Check("ini 保留 orgapi64", ini.Contains("orgapi64 = steam_api64_o.dll", StringComparison.Ordinal));
        ChecksContext.Check("ini 保留 [dlc] 段", ini.Contains("[dlc]", StringComparison.Ordinal));

        var state = fs.ReadAllText(Path.Combine(root, "state-apply.json"));
        ChecksContext.Check("清单记录 api 文件", state.Contains("steam_api64.dll", StringComparison.Ordinal));
        ChecksContext.Check("清单记录 appId", state.Contains("394360", StringComparison.Ordinal));

        var reverted = service.Revert(install);
        ChecksContext.Equal("还原后状态", GamePatchStatus.Vanilla, reverted.Status);
        ChecksContext.EqualBytes("还原后字节", original, fs.ReadAllBytes(apiPath));
        ChecksContext.Check("备份已删除", !fs.FileExists(backupPath));
        ChecksContext.Check("ini 已删除", !fs.FileExists(iniPath));
        ChecksContext.Check("清单已清空", !fs.FileExists(Path.Combine(root, "state-apply.json"))
            || !fs.ReadAllText(Path.Combine(root, "state-apply.json")).Contains("hoi4", StringComparison.Ordinal));
    }

    private static void CheckApplyIsIdempotent(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 重复打补丁不污染备份");
        var install = NewInstall(root, "idempotent", "stellaris", appId: 281990);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var original = FakeVanillaDll(0x22, 2048);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-idempotent.json"));
        service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.EqualBytes("第一次备份 = 原文件", original, fs.ReadAllBytes(backupPath));

        var again = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("再打一次状态仍是已打补丁", GamePatchStatus.Patched, again.Status);
        ChecksContext.EqualBytes("再打一次备份仍是原文件", original, fs.ReadAllBytes(backupPath));

        service.Revert(install);
        ChecksContext.EqualBytes("再打一次后仍能还原", original, fs.ReadAllBytes(apiPath));
    }

    private static void CheckBinariesLayout(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] binaries 布局");
        var install = NewInstall(root, "binaries", "victoria3", appId: null);
        var apiPath = Path.Combine(install.InstallRoot, "binaries", "steam_api64.dll");
        fs.EnsureDirectory(Path.Combine(install.InstallRoot, "binaries"));
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0x33, 1024));

        var service = NewService(fs, logger, Path.Combine(root, "state-binaries.json"));
        var report = service.Inspect(install);
        ChecksContext.Equal("binaries 下能找到 api", GamePatchStatus.Vanilla, report.Status);
        ChecksContext.Equal("选择的目录", Path.Combine(install.InstallRoot, "binaries"), report.ApiDirectory);
        ChecksContext.Equal<int?>("外部 appId 兜底（victoria3）", 529340, report.SteamAppId);
    }

    private static void CheckCitiesSkylines2Layout(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] CS2 Plugins 布局");
        var install = NewInstall(root, "cs2", "cities_skylines_2", appId: 949230);
        var plugins = Path.Combine(install.InstallRoot, "Cities2_Data", "Plugins", "x86_64");
        fs.EnsureDirectory(plugins);
        fs.WriteAllBytesAtomic(Path.Combine(plugins, "steam_api64.dll"), FakeVanillaDll(0x44, 1024));

        var service = NewService(fs, logger, Path.Combine(root, "state-cs2.json"));
        var report = service.Inspect(install);
        ChecksContext.Equal("CS2 能找到 Plugins 下的 api", GamePatchStatus.Vanilla, report.Status);
        ChecksContext.Equal("CS2 选择 Plugins 目录", plugins, report.ApiDirectory);
    }

    private static void CheckCitiesSkylines1Layout(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] CS1 安装根布局（独立安装，无 appId）");
        // CS1 真机把 steam_api64.dll 直接放在安装根，且没有 Cities2_Data 之类的子目录。
        // appId 传 null 模拟「独立安装没有 appmanifest」：必须靠 GameSteamAppIds 的映射表拿到 255710。
        var install = NewInstall(root, "cs1", "cities_skylines", appId: null);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        var original = FakeVanillaDll(0x3C, 2048);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-cs1.json"));
        var before = service.Inspect(install);
        ChecksContext.Equal("CS1 无 appmanifest 也能识别", GamePatchStatus.Vanilla, before.Status);
        ChecksContext.Equal<int?>("CS1 appId 来自映射表", 255710, before.SteamAppId);
        ChecksContext.Equal("CS1 目标目录是安装根", install.InstallRoot, before.ApiDirectory);
        ChecksContext.Check("CS1 原版可以打补丁", before.CanApply);

        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("CS1 打完补丁状态", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("CS1 备份是原文件", original, fs.ReadAllBytes(backupPath));

        var ini = fs.ReadAllText(iniPath);
        ChecksContext.Check("CS1 ini 写入 appid 255710", ini.Contains("appid = 255710", StringComparison.Ordinal));
        ChecksContext.Check("CS1 ini 保留 orgapi64", ini.Contains("orgapi64 = steam_api64_o.dll", StringComparison.Ordinal));
        ChecksContext.Check("CS1 ini 带 Cities Skylines 区块", ini.Contains("Cities Skylines", StringComparison.Ordinal));

        var reverted = service.Revert(install);
        ChecksContext.Equal("CS1 还原后状态", GamePatchStatus.Vanilla, reverted.Status);
        ChecksContext.EqualBytes("CS1 还原后字节", original, fs.ReadAllBytes(apiPath));
        ChecksContext.Check("CS1 还原后备份已删除", !fs.FileExists(backupPath));
        ChecksContext.Check("CS1 还原后 ini 已删除", !fs.FileExists(iniPath));
    }

    private static void CheckKnownVersionInstall(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 已知原版直接打补丁");
        var install = NewInstall(root, "known", "hoi4", appId: 394360);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var original = FakeVanillaDll(0x12, 4096);
        fs.WriteAllBytesAtomic(apiPath, original);

        // 测试专用登记表：把这份伪造字节登记成 hoi4 的「官方原版」。
        var versions = PatchVersionRegistry.FromJson(
            RegistryJson("hoi4", "test-1.2.3", "123456", "steam_api64.dll", original));
        var service = NewService(fs, logger, Path.Combine(root, "state-known.json"), versions);

        var before = service.Inspect(install);
        ChecksContext.Equal("命中登记表 → Known", PatchVersionState.Known, before.VersionState);
        ChecksContext.Equal("命中版本号", "test-1.2.3", before.MatchedVersion ?? "(missing)");
        ChecksContext.Equal("命中 buildid", "123456", before.MatchedBuildId ?? "(missing)");
        ChecksContext.Equal("原版状态", GamePatchStatus.Vanilla, before.Status);

        var applied = service.Apply(install);
        ChecksContext.Equal("已知原版无需确认直接打上", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("备份是原文件", original, fs.ReadAllBytes(backupPath));

        // loader 在岗时版本信息来自备份，仍然认得出来。
        ChecksContext.Equal("打完补丁仍认得出原版版本", PatchVersionState.Known, applied.VersionState);
        ChecksContext.Equal("打完补丁仍带 buildid", "123456", applied.MatchedBuildId ?? "(missing)");
    }

    private static void CheckUnverifiedVersion(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 未验证版本需要确认");
        var install = NewInstall(root, "unverified", "hoi4", appId: 394360);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        var statePath = Path.Combine(root, "state-unverified.json");
        var original = FakeVanillaDll(0x13, 4096);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, statePath, PatchVersionRegistry.Empty);
        var before = service.Inspect(install);
        ChecksContext.Equal("未知哈希 → Unknown", PatchVersionState.Unknown, before.VersionState);
        ChecksContext.Equal("未知哈希仍是原版", GamePatchStatus.Vanilla, before.Status);

        var blocked = service.Apply(install);
        ChecksContext.Equal("未验证版本被拦下", GamePatchStatus.UnverifiedVersion, blocked.Status);
        ChecksContext.Check("拦下时给出原因", !string.IsNullOrWhiteSpace(blocked.Detail), blocked.Detail ?? "(空)");
        ChecksContext.Check("拦下时仍允许用户确认后继续", blocked.CanApply);
        ChecksContext.Check("拦下时不写盘：api 未变", fs.ReadAllBytes(apiPath).AsSpan().SequenceEqual(original));
        ChecksContext.Check("拦下时不写盘：没有 _o", !fs.FileExists(backupPath));
        ChecksContext.Check("拦下时不写盘：没有 ini", !fs.FileExists(iniPath));
        ChecksContext.Check("拦下时不写盘：没有清单", !fs.FileExists(statePath));

        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("确认后可以打上", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("确认后备份是原文件", original, fs.ReadAllBytes(backupPath));
    }

    private static void CheckForeignPatch(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 他人补丁");
        var install = NewInstall(root, "foreign", "eu4", appId: 236850);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0x55, 1024));
        fs.WriteAllBytesAtomic(backupPath, FakeVanillaDll(0x99, 512));

        var service = NewService(fs, logger, Path.Combine(root, "state-foreign.json"));
        var report = service.Inspect(install);
        ChecksContext.Equal("他人补丁状态", GamePatchStatus.PatchedOther, report.Status);
        ChecksContext.Check("他人补丁 → loader 在岗", report.IsLoaderActive);
        ChecksContext.Check("他人补丁可移除", report.CanRevert);
        ChecksContext.Check("他人补丁可重打", report.CanApply);
    }

    private static void CheckSteamOverwrote(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] Steam 更新覆盖（真更新：当前文件是另一份字节）");
        var install = NewInstall(root, "overwrote", "ck3", appId: 1158310);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var original = FakeVanillaDll(0x66, 1024);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-overwrote.json"), PatchVersionRegistry.Empty);
        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("先打上补丁", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("备份是原文件", original, fs.ReadAllBytes(backupPath));

        // 旧用例喂的是「当前 == 备份」，那其实什么都证明不了；这里模拟真更新：
        // loader 在位、_o 还在，Steam 把 steam_api64.dll 换成了另一份原版（大小不变、字节不同）。
        var updated = FakeVanillaDll(0x67, 1024);
        fs.WriteAllBytesAtomic(apiPath, updated);

        var report = service.Inspect(install);
        ChecksContext.Equal("被 Steam 覆盖的状态", GamePatchStatus.SteamOverwrote, report.Status);
        ChecksContext.Check("被覆盖后当前 API 不是自家 loader", !report.IsLoaderActive);
        ChecksContext.Check("可重新打补丁", report.CanApply);
        ChecksContext.Check("可清理残留", report.CanRevert);
        ChecksContext.Equal("被覆盖后版本仍未验证", PatchVersionState.Unknown, report.VersionState);

        // 极端情况：Steam 把原版写回得和备份一模一样，也仍算被覆盖。
        fs.WriteAllBytesAtomic(apiPath, original);
        ChecksContext.Equal("与备份同字节也算覆盖", GamePatchStatus.SteamOverwrote, service.Inspect(install).Status);

        var reverted = service.Revert(install);
        ChecksContext.Equal("清理残留后回到原版", GamePatchStatus.Vanilla, reverted.Status);
        ChecksContext.EqualBytes("清理后是备份的字节", original, fs.ReadAllBytes(apiPath));
        ChecksContext.Check("清理后备份已删除", !fs.FileExists(backupPath));
    }

    private static void CheckBackupSuspect(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 别人的 loader 不覆盖 _o（备份来源存疑）");
        var install = NewInstall(root, "suspect", "eu4", appId: 236850);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        var current = FakeVanillaDll(0x91, 4096);
        var foreignBackup = FakeVanillaDll(0x92, 300);
        fs.WriteAllBytesAtomic(apiPath, current);
        fs.WriteAllBytesAtomic(backupPath, foreignBackup);

        var service = NewService(fs, logger, Path.Combine(root, "state-suspect.json"), PatchVersionRegistry.Empty);
        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("打上补丁", GamePatchStatus.Patched, applied.Status);
        ChecksContext.Check("标注备份来源存疑", applied.BackupSuspect);
        ChecksContext.Check("给出归档路径", !string.IsNullOrWhiteSpace(applied.BackupArchivePath));

        var archive = applied.BackupArchivePath;
        ChecksContext.Check("归档文件存在", archive is not null && fs.FileExists(archive));
        if (archive is not null)
        {
            ChecksContext.EqualBytes("归档内容是被替换掉的当前文件", current, fs.ReadAllBytes(archive));
        }

        ChecksContext.EqualBytes("原有 _o 没有被覆盖", foreignBackup, fs.ReadAllBytes(backupPath));
        ChecksContext.Check("归档名带 _o.bak- 前缀", archive is not null
            && Path.GetFileName(archive).StartsWith("steam_api64_o.bak-", StringComparison.Ordinal), archive ?? "(null)");

        // 移除补丁只认 _o：它还原的是「别人的备份」，所以界面必须提示来源存疑。
        var reverted = service.Revert(install);
        ChecksContext.Equal("移除后回到 _o 的字节", GamePatchStatus.Vanilla, reverted.Status);
        ChecksContext.EqualBytes("移除后 api = 原有 _o", foreignBackup, fs.ReadAllBytes(apiPath));
        ChecksContext.Check("移除后 ini 已删除", !fs.FileExists(iniPath));
        ChecksContext.Check("归档留给用户处理", archive is not null && fs.FileExists(archive));
    }

    private static void CheckUnsupportedWithoutAppId(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 无 appId");
        var install = NewInstall(root, "noappid", "unknown_game", appId: null);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0x77, 512));

        var service = NewService(fs, logger, Path.Combine(root, "state-noappid.json"));
        var report = service.Inspect(install);
        ChecksContext.Equal("无 appId 状态", GamePatchStatus.Unsupported, report.Status);
        ChecksContext.Check("无 appId 不能打补丁", !report.CanApply);
    }

    private static void CheckLockedFile(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 文件被占用");
        var install = NewInstall(root, "locked", "stellaris", appId: 281990);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var original = FakeVanillaDll(0x88, 1024);
        fs.WriteAllBytesAtomic(apiPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-locked.json"));

        // 独占打开：服务的读/写都必须失败，但不能破坏原文件。
        using (var hold = new FileStream(apiPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var report = service.Apply(install, allowUnverifiedVersion: true);
            ChecksContext.Check("占用时返回失败细节", !string.IsNullOrWhiteSpace(report.Detail), report.Detail ?? "(空)");
        }

        ChecksContext.EqualBytes("占用失败后原文件不变", original, fs.ReadAllBytes(apiPath));
    }

    private static void CheckRevertKeepsForeignIni(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 不动用户自备 ini");
        var install = NewInstall(root, "foreign-ini", "eu5", appId: 3450310);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0xAA, 1024));
        fs.WriteAllTextAtomic(iniPath, "[steam]\nappid = 3450310\n");

        var service = NewService(fs, logger, Path.Combine(root, "state-foreign-ini.json"));
        service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Check("打补丁覆盖 ini", fs.ReadAllText(iniPath).Contains("; PdxNext 自动生成", StringComparison.Ordinal));

        // 用户重新放了一份自己的 ini，移除补丁时必须留着。
        fs.WriteAllTextAtomic(iniPath, "[steam]\nappid = 3450310\n");
        service.Revert(install);
        ChecksContext.Check("用户自备 ini 保留", fs.FileExists(iniPath));
    }

    private static void CheckExternalAssetsOverride(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] 外部素材覆盖");
        var assetsDir = Path.Combine(root, "external-assets");
        fs.EnsureDirectory(assetsDir);
        var fakeLoader = new byte[] { 1, 2, 3, 4 };

        // 外部 ini 自带一份旧 [dlc] 大表：生成时必须整段换成源清单，不能出现两段 [dlc]。
        var fakeIni = "[steam]\r\nappid = 1\r\norgapi64 = steam_api64_o.dll\r\n[dlc]\r\n999 = Legacy Entry\r\n";
        fs.WriteAllBytesAtomic(Path.Combine(assetsDir, PatchAssets.Loader64FileName), fakeLoader);
        fs.WriteAllTextAtomic(Path.Combine(assetsDir, PatchAssets.IniFileName), fakeIni);

        var assets = new PatchAssets(fs, logger, assetsDir);
        ChecksContext.Check("外部 loader 生效", assets.TryGetLoader(x64: true, out var loader));
        ChecksContext.EqualBytes("外部 loader 字节", fakeLoader, loader);
        ChecksContext.Check("标记为使用外部素材", assets.UsesExternalAssets);
        ChecksContext.Check("外部 ini 生效", assets.TryGetIniTemplate(out var ini));
        ChecksContext.Check("外部 ini 内容", ini.Contains("orgapi64", StringComparison.Ordinal));
        ChecksContext.Check("外部素材仍用内置 DLC 清单", assets.TryGetDlcCatalog(out _));

        var install = NewInstall(root, "external", "eu4", appId: 236850);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var iniPath = Path.Combine(install.InstallRoot, "cream_api.ini");
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0xBB, 256));
        var service = new GamePatchService(fs, logger, assets, Path.Combine(root, "state-external.json"));
        var applied = service.Apply(install, allowUnverifiedVersion: true);
        ChecksContext.Equal("外部素材也能打补丁", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("目标写入外部 loader", fakeLoader, fs.ReadAllBytes(apiPath));

        var generated = fs.ReadAllText(iniPath);
        ChecksContext.Equal("外部 ini 的大表被整段替换", 1, CountOccurrences(generated, "[dlc]"));
        ChecksContext.Check("旧大表条目被丢弃", !generated.Contains("999 = Legacy Entry", StringComparison.Ordinal));
        ChecksContext.Check("改为写入源清单", generated.Contains("4314660 = Victoria 3: State and Revolution - Immersion Pack", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------------ 助手

    private static GamePatchService NewService(
        IFileSystem fs,
        IAppLogger logger,
        string statePath,
        PatchVersionRegistry? versions = null)
    {
        var assets = new PatchAssets(fs, logger, Path.Combine(Path.GetTempPath(), "pdx-no-assets"));
        return new GamePatchService(fs, logger, assets, statePath, isProcessRunning: null, versions: versions);
    }

    /// <summary>拼一份只含一条登记的最小登记表 JSON，用于验证匹配逻辑。</summary>
    private static string RegistryJson(string gameId, string version, string buildId, string fileName, byte[] data)
        => "{\"schema\":1,\"updated\":\"test\",\"entries\":[{\"gameId\":\"" + gameId
            + "\",\"version\":\"" + version
            + "\",\"buildId\":\"" + buildId
            + "\",\"fileName\":\"" + fileName
            + "\",\"sha256\":\"" + Sha256(data)
            + "\",\"size\":" + data.Length.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}]}";

    private static int CountOccurrences(string haystack, string needle)
    {
        var count = 0;
        var index = 0;
        while (true)
        {
            var found = haystack.IndexOf(needle, index, StringComparison.Ordinal);
            if (found < 0)
            {
                return count;
            }

            count++;
            index = found + needle.Length;
        }
    }

    private static GameInstallation NewInstall(string root, string name, string gameId, int? appId)
    {
        var installRoot = Path.Combine(root, name);
        Directory.CreateDirectory(installRoot);
        return new GameInstallation(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: Path.Combine(installRoot, "game"),
            DlcPath: Path.Combine(installRoot, "dlc"),
            ModDir: Path.Combine(installRoot, "mod"),
            ExePath: Path.Combine(installRoot, gameId + ".exe"),
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
            SteamAppId: appId,
            Support: GameSupport.Full);
    }

    /// <summary>伪造一份「Steam 原版 dll」：内容可辨且足够长。</summary>
    private static byte[] FakeVanillaDll(byte seed, int length)
    {
        var data = new byte[length];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((seed + i) & 0xFF);
        }

        return data;
    }

    private static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

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
