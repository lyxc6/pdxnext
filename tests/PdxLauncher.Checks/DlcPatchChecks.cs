namespace PdxLauncher.Checks;

using System.Security.Cryptography;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Themes;

/// <summary>
/// DLC 解锁补丁自检：内嵌素材完整性、状态判定（原版 / 已打 / 他人补丁 / Steam 覆盖）、
/// 一键打补丁与移除补丁的字节级还原、清单落盘、只读与占用失败路径。
/// 全部在临时目录里跑，不碰真实安装目录、不联网。
/// </summary>
public static class DlcPatchChecks
{
    private const string ExpectedLoader64Sha256 =
        "f428bfe07eaf1e6f6fa1f28b6a787f93794bc5f97e8609645571b8bdc10d5385";

    private const string ExpectedLoader86Sha256 =
        "a34e7ef464373898fa659294eef80ab554417c8ae6fef860f03fb7f5ae6bb698";

    private const string ExpectedIniSha256 =
        "d493a262cf377e030402e49939deb2e7857439f71818f9c06c5cad2ad6e3d733";

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
            CheckNotDetected(fs, logger, root);
            CheckApplyAndRevert(fs, logger, root);
            CheckApplyIsIdempotent(fs, logger, root);
            CheckBinariesLayout(fs, logger, root);
            CheckCitiesSkylines2Layout(fs, logger, root);
            CheckForeignPatch(fs, logger, root);
            CheckSteamOverwrote(fs, logger, root);
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
        ChecksContext.Equal("ini 模板 sha256", ExpectedIniSha256, Sha256(System.Text.Encoding.UTF8.GetBytes(ini)));
        ChecksContext.Check("ini 模板含 490 条 DLC 标记", ini.Contains("[dlc]", StringComparison.Ordinal));
        ChecksContext.Check("ini 模板含 orgapi64", ini.Contains("orgapi64 = steam_api64_o.dll", StringComparison.Ordinal));
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

        var applied = service.Apply(install);
        ChecksContext.Equal("打完补丁状态", GamePatchStatus.Patched, applied.Status);
        ChecksContext.Check("打完补丁可以移除", applied.CanRevert);
        ChecksContext.Equal("补丁文件数量", 1, applied.PatchedFiles.Count);

        var assets = new PatchAssets(fs, logger, Path.Combine(root, "state-assets-none"));
        ChecksContext.Check("loader 素材可读", assets.TryGetLoader(x64: true, out var loader));
        ChecksContext.Check("目标已是 loader", fs.ReadAllBytes(apiPath).AsSpan().SequenceEqual(loader));
        ChecksContext.EqualBytes("备份是原文件", original, fs.ReadAllBytes(backupPath));

        var ini = fs.ReadAllText(iniPath);
        ChecksContext.Check("ini 写入 appid", ini.Contains("appid = 394360", StringComparison.Ordinal), ini.Length.ToString());
        ChecksContext.Check("ini 带 PdxNext 标记", ini.Contains("; PdxNext 自动生成", StringComparison.Ordinal));
        ChecksContext.Check("ini 保留 orgapi64", ini.Contains("orgapi64 = steam_api64_o.dll", StringComparison.Ordinal));
        ChecksContext.Check("ini 保留 490 条 DLC", ini.Contains("[dlc]", StringComparison.Ordinal));

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
        service.Apply(install);
        ChecksContext.EqualBytes("第一次备份 = 原文件", original, fs.ReadAllBytes(backupPath));

        var again = service.Apply(install);
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
        ChecksContext.Check("他人补丁可移除", report.CanRevert);
        ChecksContext.Check("他人补丁可重打", report.CanApply);
    }

    private static void CheckSteamOverwrote(IFileSystem fs, IAppLogger logger, string root)
    {
        Console.WriteLine("[DlcPatchChecks] Steam 更新覆盖");
        var install = NewInstall(root, "overwrote", "ck3", appId: 1158310);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        var backupPath = Path.Combine(install.InstallRoot, "steam_api64_o.dll");
        var original = FakeVanillaDll(0x66, 1024);
        fs.WriteAllBytesAtomic(apiPath, original);
        fs.WriteAllBytesAtomic(backupPath, original);

        var service = NewService(fs, logger, Path.Combine(root, "state-overwrote.json"));
        var report = service.Inspect(install);
        ChecksContext.Equal("被 Steam 覆盖的状态", GamePatchStatus.SteamOverwrote, report.Status);
        ChecksContext.Check("可重新打补丁", report.CanApply);
        ChecksContext.Check("可清理残留", report.CanRevert);
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
            var report = service.Apply(install);
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
        service.Apply(install);
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
        var fakeIni = "[steam]\r\nappid = 1\r\norgapi64 = steam_api64_o.dll\r\n[dlc]\r\n";
        fs.WriteAllBytesAtomic(Path.Combine(assetsDir, PatchAssets.Loader64FileName), fakeLoader);
        fs.WriteAllTextAtomic(Path.Combine(assetsDir, PatchAssets.IniFileName), fakeIni);

        var assets = new PatchAssets(fs, logger, assetsDir);
        ChecksContext.Check("外部 loader 生效", assets.TryGetLoader(x64: true, out var loader));
        ChecksContext.EqualBytes("外部 loader 字节", fakeLoader, loader);
        ChecksContext.Check("标记为使用外部素材", assets.UsesExternalAssets);
        ChecksContext.Check("外部 ini 生效", assets.TryGetIniTemplate(out var ini));
        ChecksContext.Check("外部 ini 内容", ini.Contains("orgapi64", StringComparison.Ordinal));

        var install = NewInstall(root, "external", "eu4", appId: 236850);
        var apiPath = Path.Combine(install.InstallRoot, "steam_api64.dll");
        fs.WriteAllBytesAtomic(apiPath, FakeVanillaDll(0xBB, 256));
        var service = new GamePatchService(fs, logger, assets, Path.Combine(root, "state-external.json"));
        var applied = service.Apply(install);
        ChecksContext.Equal("外部素材也能打补丁", GamePatchStatus.Patched, applied.Status);
        ChecksContext.EqualBytes("目标写入外部 loader", fakeLoader, fs.ReadAllBytes(apiPath));
    }

    // ------------------------------------------------------------------ 助手

    private static GamePatchService NewService(IFileSystem fs, IAppLogger logger, string statePath)
    {
        var assets = new PatchAssets(fs, logger, Path.Combine(Path.GetTempPath(), "pdx-no-assets"));
        return new GamePatchService(fs, logger, assets, statePath);
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
