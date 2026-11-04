namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Patch;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Themes;

/// <summary>
/// DLC 授权判定自检：appmanifest 的 <c>LastOwner</c> / <c>DisabledDLC</c> 解析、可信条件、
/// 授权合并、结论文案表、各种「探测不了」的降级路径、Steam 独占闸门与缓存。
/// 全部在临时目录里跑，用假探针，不加载真实 steam_api64.dll、不联网。
/// </summary>
public static class SteamOwnershipChecks
{
    private const int StellarisAppId = 281990;

    private const ulong RealSteamId = 76561198370719182;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        var root = Path.Combine(Path.GetTempPath(), "pdx-ownership-checks-" + Guid.NewGuid().ToString("N"));
        try
        {
            fs.EnsureDirectory(root);
            CheckManifestFields(fs, logger, root);
            CheckTrustedDisabled();
            CheckManifestLookup(fs, logger, root);
            CheckOwnershipMerge(fs, logger, root, lastOwner: RealSteamId, expectTrusted: true);
            CheckOwnershipMerge(fs, logger, root, lastOwner: 76561190000000000, expectTrusted: false);
            CheckVerdictTable();
            CheckAccountProbe(fs, logger, root);
            CheckUnavailablePaths(fs, logger, root);
            CheckSessionGate();
            CheckCache(fs, logger, root);
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

    // ------------------------------------------------------------------ appmanifest

    private static void CheckManifestFields(IFileSystem fs, IAppLogger logger, string root)
    {
        var library = Path.Combine(root, "manifest-lib");
        var dir = Path.Combine(library, "steamapps");
        fs.EnsureDirectory(dir);

        var path = Path.Combine(dir, "appmanifest_281990.acf");
        fs.WriteAllTextAtomic(path, ManifestText(RealSteamId.ToString(), "518910, 553280"));

        var reader = new AppManifestReader(fs, logger);
        var manifest = reader.ReadForApp(library, StellarisAppId);
        ChecksContext.Check("能读到 appmanifest", manifest is not null);
        if (manifest is null)
        {
            return;
        }

        ChecksContext.Equal("LastOwner 解析成 SteamID64", RealSteamId.ToString(), manifest.LastOwner);
        ChecksContext.Equal("DisabledDLC 条目数", 2, manifest.DisabledDlc?.Count ?? -1);
        ChecksContext.Check("DisabledDLC 集合含 518910", manifest.DisabledDlcSet.Contains("518910"));
        ChecksContext.Check("DisabledDLC 集合含 553280", manifest.DisabledDlcSet.Contains("553280"));
        ChecksContext.Equal("appid 照旧解析", StellarisAppId, manifest.AppId);
        ChecksContext.Equal("Name 照旧解析", "Stellaris", manifest.Name);

        // 没有 LastOwner / DisabledDLC 时必须是 null，而不是空串，避免界面显示一串空白。
        var bare = Path.Combine(dir, "appmanifest_394360.acf");
        fs.WriteAllTextAtomic(bare, ManifestText(null, null));
        var bareManifest = reader.ReadForApp(library, 394360);
        ChecksContext.Check("缺 LastOwner 时读成 null", bareManifest?.LastOwner is null);
        ChecksContext.Check("缺 DisabledDLC 时读成 null", bareManifest?.DisabledDlc is null);
        ChecksContext.Equal("缺 DisabledDLC 时集合为空", 0, bareManifest?.DisabledDlcSet.Count ?? -1);
    }

    private static void CheckTrustedDisabled()
    {
        var manifest = new AppManifest(
            StellarisAppId,
            "Stellaris",
            "Stellaris",
            @"F:\SteamLibrary\steamapps\common\Stellaris",
            4,
            100,
            RealSteamId.ToString(),
            new[] { "518910", "553280" });

        ChecksContext.Equal(
            "LastOwner 匹配时可信条目数",
            2,
            AppManifestLookup.TrustedDisabledDlc(manifest, RealSteamId).Count);
        ChecksContext.Equal(
            "LastOwner 不匹配时一条都不信",
            0,
            AppManifestLookup.TrustedDisabledDlc(manifest, 76561190000000000).Count);
        ChecksContext.Equal(
            "拿不到 SteamID 时一条都不信",
            0,
            AppManifestLookup.TrustedDisabledDlc(manifest, 0).Count);
        ChecksContext.Equal(
            "没有清单时一条都不信",
            0,
            AppManifestLookup.TrustedDisabledDlc(null, RealSteamId).Count);
    }

    private static void CheckManifestLookup(IFileSystem fs, IAppLogger logger, string root)
    {
        var library = Path.Combine(root, "lookup-lib");
        var installRoot = Path.Combine(library, "steamapps", "common", "Stellaris");
        fs.EnsureDirectory(installRoot);
        fs.EnsureDirectory(Path.Combine(library, "steamapps"));
        fs.WriteAllTextAtomic(
            Path.Combine(library, "steamapps", "appmanifest_281990.acf"),
            ManifestText(RealSteamId.ToString(), null));

        var lookup = new AppManifestLookup(fs, logger);
        ChecksContext.Equal("从安装目录推回库根", library, lookup.FindLibraryRoot(installRoot));

        var game = NewInstall(root, installRoot: installRoot, appId: StellarisAppId);
        var manifest = lookup.Find(game, StellarisAppId, null);
        ChecksContext.Check("安装目录推导就能找到清单", manifest is not null);
        ChecksContext.Equal("找到的清单 InstallDir", "Stellaris", manifest?.InstallDir);

        // 已知库补找只认「清单里的安装路径 == 这份安装」，否则会把同一款游戏在别的库里的
        // 清单误当成本机的：跨库（甚至跨机器搬运的目录）拼错授权就是从这里来的。
        var otherRoot = Path.Combine(root, "not-a-library", "Games", "Stellaris");
        fs.EnsureDirectory(otherRoot);
        var strayGame = NewInstall(root, installRoot: otherRoot, appId: StellarisAppId);
        ChecksContext.Check(
            "安装路径对不上的清单不采信",
            lookup.Find(strayGame, StellarisAppId, new[] { library }) is null);

        var insideLibrary = Path.Combine(library, "steamapps", "common", "Stellaris");
        var insideGame = NewInstall(root, installRoot: insideLibrary, appId: StellarisAppId);
        ChecksContext.Check(
            "路径对得上时才能在已知库里找到",
            lookup.Find(insideGame, StellarisAppId, new[] { library }) is not null);
    }

    // ------------------------------------------------------------------ 合并 / 判定

    private static void CheckOwnershipMerge(
        IFileSystem fs,
        IAppLogger logger,
        string root,
        ulong lastOwner,
        bool expectTrusted)
    {
        var name = "merge-" + lastOwner.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var library = Path.Combine(root, name);
        var installRoot = Path.Combine(library, "steamapps", "common", "Stellaris");
        fs.EnsureDirectory(installRoot);
        fs.EnsureDirectory(Path.Combine(library, "steamapps"));
        fs.WriteAllTextAtomic(
            Path.Combine(library, "steamapps", "appmanifest_281990.acf"),
            ManifestText(lastOwner.ToString(System.Globalization.CultureInfo.InvariantCulture), "518910"));

        var apiPath = Path.Combine(installRoot, "steam_api64.dll");
        fs.WriteAllBytesAtomic(apiPath, new byte[128]);

        var game = NewInstall(root, installRoot: installRoot, appId: StellarisAppId);
        var patch = new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api64.dll");
        var probe = new FakeProbe(
            _ => new SteamDllProbeResult(
                Initialized: true,
                SteamRunning: true,
                SteamId64: RealSteamId,
                Dlcs: new[]
                {
                    new SteamDlcOwnership("447682", "Digital Artbook", IsOwned: true, IsInstalled: true),
                    new SteamDlcOwnership("518910", "Leviathans", IsOwned: false, IsInstalled: false),
                    new SteamDlcOwnership("553280", "Utopia", IsOwned: false, IsInstalled: false),
                },
                Error: null));

        var service = new SteamOwnershipService(fs, logger, patch, () => new[] { library }, probe);
        var report = service.Inspect(game, CancellationToken.None);

        ChecksContext.Equal("探测成功状态", SteamOwnershipState.Succeeded, report.State);
        ChecksContext.Check("报告带补丁状态", report.PatchStatus == GamePatchStatus.Patched);
        ChecksContext.Equal("SteamID64 落到报告里", RealSteamId, report.SteamId64);
        ChecksContext.Check("真实拥有的 id 在集合里", report.OwnedAppIds.Contains("447682"));

        if (expectTrusted)
        {
            ChecksContext.Check("LastOwner 匹配时 DisabledDLC 升级为已拥有", report.OwnedAppIds.Contains("518910"));
            ChecksContext.Equal("可信 DisabledDLC 条目数", 1, report.TrustedDisabledDlc.Count);
            ChecksContext.Check("清单所有者比对为真", report.ManifestOwnerMatches);
            var upgraded = report.Dlcs.FirstOrDefault(d => d.AppId == "518910");
            ChecksContext.Check("对应 DLC 条目被改成已拥有", upgraded is { IsOwned: true });
        }
        else
        {
            ChecksContext.Check("LastOwner 不匹配时不采信 DisabledDLC", !report.OwnedAppIds.Contains("518910"));
            ChecksContext.Equal("不可信状态可信条目数为 0", 0, report.TrustedDisabledDlc.Count);
            ChecksContext.Check("清单所有者比对为假", !report.ManifestOwnerMatches);
        }

        ChecksContext.Check("未声明拥有的 id 不凭空出现", !report.OwnedAppIds.Contains("553280"));
        ChecksContext.Equal("DLC 条目数", 3, report.Dlcs.Count);

        var candidates = new[]
        {
            new DlcOwnershipCandidate("447682", "Digital Artbook"),
            new DlcOwnershipCandidate("518910", "Leviathans"),
            new DlcOwnershipCandidate("553280", "Utopia"),
        };
        var verdict = DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, candidates);
        if (expectTrusted)
        {
            ChecksContext.Check("有一项未授权 → 补丁不完整", verdict.Kind == DlcOwnershipVerdictKind.PatchedIncomplete);
            ChecksContext.Equal("未授权磁盘条目数", 1, verdict.UnownedDiskDlcCount);
            ChecksContext.Equal("未授权名单只剩 Utopia", "Utopia", verdict.UnownedNames.FirstOrDefault());
        }
        else
        {
            ChecksContext.Equal("不可信时未授权磁盘条目数", 2, verdict.UnownedDiskDlcCount);
        }
    }

    private static void CheckVerdictTable()
    {
        var full = Report(
            SteamOwnershipState.Succeeded,
            new[]
            {
                new SteamDlcOwnership("1", "A", true, true),
                new SteamDlcOwnership("2", "B", true, true),
            });
        var partial = Report(
            SteamOwnershipState.Succeeded,
            new[]
            {
                new SteamDlcOwnership("1", "A", true, true),
                new SteamDlcOwnership("2", "B", false, false),
            });
        var disk = new[]
        {
            new DlcOwnershipCandidate("1", "A"),
            new DlcOwnershipCandidate("2", "B"),
        };

        ChecksContext.Check(
            "没探测结果 → 未知",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, null, disk).Kind == DlcOwnershipVerdictKind.Unknown);
        ChecksContext.Check(
            "探测失败 → 未知",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, Report(SteamOwnershipState.InitFailed, Array.Empty<SteamDlcOwnership>()), disk).Kind
                == DlcOwnershipVerdictKind.Unknown);

        var effective = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, full, disk);
        ChecksContext.Check("已打补丁 + 全拥有 → 生效", effective.Kind == DlcOwnershipVerdictKind.PatchedEffective);
        ChecksContext.Check("生效判定带 IsEffective", effective.IsEffective);
        ChecksContext.Equal("生效判定磁盘 DLC 数", 2, effective.DiskDlcCount);
        ChecksContext.Equal("生效判定已授权磁盘数", 2, effective.OwnedDiskDlcCount);

        var incomplete = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, partial, disk);
        ChecksContext.Check("已打补丁 + 部分拥有 → 不完整", incomplete.Kind == DlcOwnershipVerdictKind.PatchedIncomplete);
        ChecksContext.Equal("不完整判定已授权磁盘数", 1, incomplete.OwnedDiskDlcCount);

        // 别人打的补丁（没有 PdxNext 标记）同样是 loader 在岗，不能落进「未打补丁」。
        ChecksContext.Check(
            "他人补丁 + 全拥有 → 生效",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.PatchedOther, full, disk).Kind == DlcOwnershipVerdictKind.PatchedEffective);
        ChecksContext.Check(
            "更新覆盖了补丁文件 → 按未打补丁算",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.SteamOverwrote, full, disk).Kind == DlcOwnershipVerdictKind.VanillaAllOwned);

        ChecksContext.Check(
            "没打补丁 + 部分拥有 → 未生效",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.Vanilla, partial, disk).Kind == DlcOwnershipVerdictKind.VanillaPartial);
        ChecksContext.Check(
            "没打补丁 + 全拥有 → 看不出差别",
            DlcOwnershipVerdict.Evaluate(GamePatchStatus.Vanilla, full, disk).Kind == DlcOwnershipVerdictKind.VanillaAllOwned);

        // CS2 这类没有 .dlc 描述文件的游戏：只能拿 Steam 自己的清单对账。
        var noDisk = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, partial, Array.Empty<DlcOwnershipCandidate>());
        ChecksContext.Equal("没有磁盘条目时按 Steam 清单计数", 2, noDisk.DiskDlcCount);
        ChecksContext.Equal("没有磁盘条目时已授权数", 1, noDisk.OwnedDiskDlcCount);
        ChecksContext.Check("没有磁盘条目时仍能判出未生效", noDisk.Kind == DlcOwnershipVerdictKind.PatchedIncomplete);

        var many = new List<DlcOwnershipCandidate>();
        var dlcs = new List<SteamDlcOwnership>();
        for (var i = 0; i < 9; i++)
        {
            var id = (100 + i).ToString(System.Globalization.CultureInfo.InvariantCulture);
            many.Add(new DlcOwnershipCandidate(id, "D" + i));
            dlcs.Add(new SteamDlcOwnership(id, "D" + i, IsOwned: false, IsInstalled: false));
        }

        var trimmed = DlcOwnershipVerdict.Evaluate(GamePatchStatus.Patched, Report(SteamOwnershipState.Succeeded, dlcs), many);
        ChecksContext.Equal("未授权名单最多留 6 条", 6, trimmed.UnownedNames.Count);
        ChecksContext.Equal("未授权磁盘条目数仍是 9", 9, trimmed.UnownedDiskDlcCount);
    }

    /// <summary>
    /// 补丁 loader 在岗时的双探测：主探测拿到加载器整表（490 条，含其他游戏），
    /// 原版备份拿到账号真实拥有（23 条）。界面上的「补丁生效」和「账号真实拥有」是两条线。
    /// </summary>
    private static void CheckAccountProbe(IFileSystem fs, IAppLogger logger, string root)
    {
        var installRoot = Path.Combine(root, "account-probe", "steamapps", "common", "Victoria 3");
        fs.EnsureDirectory(installRoot);
        fs.WriteAllBytesAtomic(Path.Combine(installRoot, "steam_api64.dll"), new byte[64]);
        fs.WriteAllBytesAtomic(Path.Combine(installRoot, "steam_api64_o.dll"), new byte[128]);

        var disk = new List<DlcOwnershipCandidate>();
        for (var i = 0; i < 17; i++)
        {
            disk.Add(new DlcOwnershipCandidate(
                "5" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                "Vic3 D" + i));
        }

        var loaderDlcs = new List<SteamDlcOwnership>();
        foreach (var dlc in disk)
        {
            loaderDlcs.Add(new SteamDlcOwnership(dlc.SteamId, dlc.DisplayName, IsOwned: true, IsInstalled: true));
        }

        for (var i = 0; i < 473; i++)
        {
            loaderDlcs.Add(new SteamDlcOwnership(
                "9" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                "Foreign " + i,
                IsOwned: true,
                IsInstalled: true));
        }

        var accountDlcs = new List<SteamDlcOwnership>(disk.Count + 6);
        foreach (var dlc in disk)
        {
            accountDlcs.Add(new SteamDlcOwnership(dlc.SteamId, dlc.DisplayName, IsOwned: true, IsInstalled: true));
        }

        for (var i = 0; i < 6; i++)
        {
            accountDlcs.Add(new SteamDlcOwnership(
                "6" + i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture),
                "Account " + i,
                IsOwned: true,
                IsInstalled: true));
        }

        SteamDllProbeResult Ok(IReadOnlyList<SteamDlcOwnership> dlcs)
            => new(true, true, RealSteamId, dlcs, null);

        var probes = new List<string>();
        var probe = new FakeProbe(path =>
        {
            probes.Add(path);
            return Ok(path.EndsWith("steam_api64_o.dll", StringComparison.OrdinalIgnoreCase) ? accountDlcs : loaderDlcs);
        });

        var game = NewInstall(root, installRoot: installRoot, appId: StellarisAppId);
        var service = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.PatchedOther, StellarisAppId, installRoot, "steam_api64.dll"),
            null,
            probe);
        var report = service.Inspect(game, CancellationToken.None);

        ChecksContext.Check("他人补丁也算 loader 在岗", report.IsLoaderActive);
        ChecksContext.Check("主报告成功", report.HasOwnership);
        ChecksContext.Equal("主探测拿到加载器整表", 490, report.Dlcs.Count);
        ChecksContext.Check("账号视图可用", report.Account is { HasOwnership: true });
        ChecksContext.Equal("账号视图只含 Steam 目录", 23, report.Account?.Dlcs.Count ?? 0);
        ChecksContext.Equal(
            "账号探测用的是原版备份",
            Path.Combine(installRoot, "steam_api64_o.dll"),
            report.Account?.ApiFilePath ?? string.Empty);
        ChecksContext.Equal("两次探测（loader + 备份）", 2, probes.Count);

        var verdict = DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, disk);
        ChecksContext.Check("他人补丁 + 磁盘全授权 → 补丁生效", verdict.Kind == DlcOwnershipVerdictKind.PatchedEffective);
        ChecksContext.Check("结论标记 loader 在岗", verdict.IsLoaderActive);
        ChecksContext.Equal("探测到的 DLC 数来自加载器", 490, verdict.ProbeDlcCount);
        ChecksContext.Equal("账号 DLC 数", 23, verdict.AccountDlcCount ?? 0);
        ChecksContext.Equal("账号已拥有数", 23, verdict.AccountOwnedCount ?? 0);
        ChecksContext.Equal("磁盘中账号确实拥有的数量", 17, verdict.AccountOwnedDiskDlcCount ?? 0);
        ChecksContext.Equal("加载器表里的外区条目", 467, verdict.ForeignDlcCount);

        // CS2 这类没有 .dlc 文件的游戏：loader 在岗时必须用账号清单，而不是 490 条的加载器表。
        var cs2 = DlcOwnershipVerdict.Evaluate(report.PatchStatus, report, Array.Empty<DlcOwnershipCandidate>());
        ChecksContext.Equal("CS2 回退用账号清单", 23, cs2.DiskDlcCount);
        ChecksContext.Equal("CS2 回退计数也来自账号视图", 23, cs2.OwnedDiskDlcCount);

        // 缓存命中时第二次 Inspect 不应再拉起任何探测。
        service.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("缓存命中不重复双探", 2, probes.Count);
        service.Invalidate();
        service.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("Invalidate 之后重新双探", 4, probes.Count);

        // 备份缺失：只探一次，账号视图为 null，界面据此提示「缺少原版备份」。
        var missingRoot = Path.Combine(root, "account-missing", "steamapps", "common", "Victoria 3");
        fs.EnsureDirectory(missingRoot);
        fs.WriteAllBytesAtomic(Path.Combine(missingRoot, "steam_api64.dll"), new byte[64]);
        var missingCalls = 0;
        var missingService = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.PatchedOther, StellarisAppId, missingRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ =>
            {
                missingCalls++;
                return Ok(loaderDlcs);
            }));
        var missing = missingService.Inspect(
            NewInstall(root, installRoot: missingRoot, appId: StellarisAppId),
            CancellationToken.None);
        ChecksContext.Check("没有备份就没有账号视图", missing.Account is null);
        ChecksContext.Equal("没有备份只探一次", 1, missingCalls);
        ChecksContext.Check(
            "没有备份时结论仍标 loader 在岗",
            DlcOwnershipVerdict.Evaluate(missing.PatchStatus, missing, disk).IsLoaderActive);

        // 备份与当前文件字节相同：说明备份本身也是 loader，不能白探，直接标 backupNotOriginal。
        var sameRoot = Path.Combine(root, "account-same", "steamapps", "common", "Victoria 3");
        fs.EnsureDirectory(sameRoot);
        var loaderBytes = new byte[64];
        fs.WriteAllBytesAtomic(Path.Combine(sameRoot, "steam_api64.dll"), loaderBytes);
        fs.WriteAllBytesAtomic(Path.Combine(sameRoot, "steam_api64_o.dll"), loaderBytes);
        var sameCalls = 0;
        var sameService = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.PatchedOther, StellarisAppId, sameRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ =>
            {
                sameCalls++;
                return Ok(loaderDlcs);
            }));
        var same = sameService.Inspect(NewInstall(root, installRoot: sameRoot, appId: StellarisAppId), CancellationToken.None);
        ChecksContext.Equal("同字节备份不探测", 1, sameCalls);
        ChecksContext.Check("同字节备份不算账号视图", same.Account is { HasOwnership: false });
        ChecksContext.Equal(
            "同字节备份的错误键",
            SteamProbeError.BackupNotOriginal,
            same.Account?.ErrorKey ?? string.Empty);

        // 备份内容不同但返回同一张表：同样按「备份也是 loader」处理。
        var sameTableRoot = Path.Combine(root, "account-same-table", "steamapps", "common", "Victoria 3");
        fs.EnsureDirectory(sameTableRoot);
        fs.WriteAllBytesAtomic(Path.Combine(sameTableRoot, "steam_api64.dll"), new byte[64]);
        fs.WriteAllBytesAtomic(Path.Combine(sameTableRoot, "steam_api64_o.dll"), new byte[96]);
        var sameTableCalls = 0;
        var sameTableService = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.PatchedOther, StellarisAppId, sameTableRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ =>
            {
                sameTableCalls++;
                return Ok(loaderDlcs);
            }));
        var sameTable = sameTableService.Inspect(
            NewInstall(root, installRoot: sameTableRoot, appId: StellarisAppId),
            CancellationToken.None);
        ChecksContext.Equal("同表备份会探一次", 2, sameTableCalls);
        ChecksContext.Equal(
            "同表备份的错误键",
            SteamProbeError.BackupNotOriginal,
            sameTable.Account?.ErrorKey ?? string.Empty);

        // 备份探测本身失败：账号视图置为不可用，但不影响主报告。
        var brokenRoot = Path.Combine(root, "account-broken", "steamapps", "common", "Victoria 3");
        fs.EnsureDirectory(brokenRoot);
        fs.WriteAllBytesAtomic(Path.Combine(brokenRoot, "steam_api64.dll"), new byte[64]);
        fs.WriteAllBytesAtomic(Path.Combine(brokenRoot, "steam_api64_o.dll"), new byte[96]);
        var broken = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.PatchedOther, StellarisAppId, brokenRoot, "steam_api64.dll"),
            null,
            new FakeProbe(path => path.EndsWith("steam_api64_o.dll", StringComparison.OrdinalIgnoreCase)
                ? SteamDllProbeResult.Fail(SteamProbeError.InitFailed, "backup broken")
                : Ok(loaderDlcs)))
            .Inspect(NewInstall(root, installRoot: brokenRoot, appId: StellarisAppId), CancellationToken.None);
        ChecksContext.Check("主报告不受账号探测失败影响", broken.HasOwnership);
        ChecksContext.Check("账号探测失败 → 账号视图不可用", broken.Account is { HasOwnership: false });
        ChecksContext.Equal(
            "账号探测失败保留错误键",
            SteamProbeError.InitFailed,
            broken.Account?.ErrorKey ?? string.Empty);
    }

    private static void CheckUnavailablePaths(IFileSystem fs, IAppLogger logger, string root)
    {
        var installRoot = Path.Combine(root, "unavailable", "steamapps", "common", "Stellaris");
        fs.EnsureDirectory(installRoot);

        // 没有 appid：连 Steam 都不用问。这里用一款映射表里没有的游戏，
        // 否则 GameSteamAppIds 会按 gameId 兜出一个 appid，就走不到这条分支了。
        var noAppIdGame = NewInstall(root, installRoot: installRoot, appId: null, gameId: "not-a-known-game");
        var noAppIdPatch = new FakePatchService(GamePatchStatus.Unsupported, null, installRoot, "steam_api64.dll");
        var noAppId = new SteamOwnershipService(fs, logger, noAppIdPatch, null, new FakeProbe(_ => ThrowProbe()))
            .Inspect(noAppIdGame, CancellationToken.None);
        ChecksContext.Equal("缺 appid → 不可用", SteamOwnershipState.Unavailable, noAppId.State);
        ChecksContext.Equal("缺 appid 的错误键", SteamProbeError.NoAppId, noAppId.ErrorKey);

        // 补丁服务没定位到 DLL：NoDll。
        var game = NewInstall(root, installRoot: installRoot, appId: StellarisAppId);
        var noTarget = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.NotDetected, StellarisAppId, null, null),
            null,
            new FakeProbe(_ => ThrowProbe()));
        var noTargetReport = noTarget.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("没定位到 DLL 的错误键", SteamProbeError.NoDll, noTargetReport.ErrorKey);

        // 定位到了但文件不存在：也是 NoDll。
        var missing = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ => ThrowProbe()));
        ChecksContext.Equal("文件不存在 → NoDll", SteamProbeError.NoDll, missing.Inspect(game, CancellationToken.None).ErrorKey);

        // 32 位游戏的 steam_api.dll 在 x64 进程里加载不了，必须提前判出来而不是让探针报 BadImageFormat。
        fs.WriteAllBytesAtomic(Path.Combine(installRoot, "steam_api.dll"), new byte[128]);
        var x86 = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api.dll"),
            null,
            new FakeProbe(_ => ThrowProbe()));
        var x86Report = x86.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("32 位 DLL → X86", SteamProbeError.X86, x86Report.ErrorKey);
        ChecksContext.Equal("32 位 DLL 归到不可用", SteamOwnershipState.Unavailable, x86Report.State);

        // Steam 没开：要能和「初始化失败」区分开，界面文案不一样。
        fs.WriteAllBytesAtomic(Path.Combine(installRoot, "steam_api64.dll"), new byte[128]);
        var off = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ => SteamDllProbeResult.Fail(SteamProbeError.SteamNotRunning)));
        var offReport = off.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("Steam 未运行 → SteamNotRunning", SteamOwnershipState.SteamNotRunning, offReport.State);
        ChecksContext.Equal("Steam 未运行的错误键", SteamProbeError.SteamNotRunning, offReport.ErrorKey);
        ChecksContext.Check("失败报告不带授权结论", !offReport.HasOwnership);

        var initFailed = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api64.dll"),
            null,
            new FakeProbe(_ => SteamDllProbeResult.Fail(SteamProbeError.InitFailed, "detail")));
        var initReport = initFailed.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("初始化失败 → InitFailed", SteamOwnershipState.InitFailed, initReport.State);
        ChecksContext.Equal("初始化失败带上细节", "detail", initReport.Detail);
    }

    private static void CheckSessionGate()
    {
        ChecksContext.Check("闸门初始未占用", !SteamNativeSession.IsActive);
        ChecksContext.Check("第一次进入成功", SteamNativeSession.TryEnter("自检甲", out var first));
        ChecksContext.Check("进入后闸门占用中", SteamNativeSession.IsActive);
        ChecksContext.Equal("闸门记录占用者", "自检甲", SteamNativeSession.Owner);
        ChecksContext.Check("第二次进入被拒", !SteamNativeSession.TryEnter("自检乙", out var second));
        second.Dispose();
        ChecksContext.Check("被拒的作用域释放不影响持有者", SteamNativeSession.IsActive);
        ChecksContext.Equal("占用者仍是甲", "自检甲", SteamNativeSession.Owner);

        first.Dispose();
        ChecksContext.Check("释放后闸门空闲", !SteamNativeSession.IsActive);
        ChecksContext.Check("释放后可以再次进入", SteamNativeSession.TryEnter("自检乙", out var third));
        ChecksContext.Equal("新占用者生效", "自检乙", SteamNativeSession.Owner);
        third.Dispose();
        ChecksContext.Check("重复释放不会留下占用", !SteamNativeSession.IsActive);
    }

    private static void CheckCache(IFileSystem fs, IAppLogger logger, string root)
    {
        var installRoot = Path.Combine(root, "cache", "steamapps", "common", "Stellaris");
        fs.EnsureDirectory(installRoot);
        fs.WriteAllBytesAtomic(Path.Combine(installRoot, "steam_api64.dll"), new byte[256]);

        var calls = 0;
        var probe = new FakeProbe(_ =>
        {
            calls++;
            return new SteamDllProbeResult(
                Initialized: true,
                SteamRunning: true,
                SteamId64: RealSteamId,
                Dlcs: new[] { new SteamDlcOwnership("1", "A", IsOwned: true, IsInstalled: true) },
                Error: null);
        });

        var game = NewInstall(root, installRoot: installRoot, appId: StellarisAppId);
        var service = new SteamOwnershipService(
            fs,
            logger,
            new FakePatchService(GamePatchStatus.Patched, StellarisAppId, installRoot, "steam_api64.dll"),
            null,
            probe);

        var first = service.Inspect(game, CancellationToken.None);
        var second = service.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("30 秒内重复检测只探一次", 1, calls);
        ChecksContext.Check("两次检测结果一致", first.State == second.State && first.Dlcs.Count == second.Dlcs.Count);

        service.Invalidate();
        service.Inspect(game, CancellationToken.None);
        ChecksContext.Equal("Invalidate 之后重新探测", 2, calls);
    }

    // ------------------------------------------------------------------ 助手

    private static SteamOwnershipReport Report(
        SteamOwnershipState state,
        IReadOnlyList<SteamDlcOwnership> dlcs,
        GamePatchStatus patchStatus = GamePatchStatus.Patched)
    {
        var owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in dlcs)
        {
            if (dlc.IsOwned)
            {
                owned.Add(dlc.AppId);
            }
        }

        return new SteamOwnershipReport(
            state,
            patchStatus,
            1,
            @"C:\game",
            "steam_api64.dll",
            @"C:\game\steam_api64.dll",
            0,
            null,
            false,
            dlcs,
            owned,
            new HashSet<string>(StringComparer.Ordinal),
            state == SteamOwnershipState.Succeeded ? null : SteamProbeError.InitFailed,
            null);
    }

    private static SteamDllProbeResult ThrowProbe()
        => throw new InvalidOperationException("这条路径不该真的去探测 DLL");

    /// <summary>伪造一份 appmanifest：只写自检关心的键。</summary>
    private static string ManifestText(string? lastOwner, string? disabledDlc)
    {
        var lines = new List<string>
        {
            "\"AppState\"",
            "{",
            "\t\"appid\"\t\t\"281990\"",
            "\t\"name\"\t\t\"Stellaris\"",
            "\t\"StateFlags\"\t\t\"4\"",
            "\t\"installdir\"\t\t\"Stellaris\"",
            "\t\"SizeOnDisk\"\t\t\"100\"",
        };

        if (lastOwner is not null)
        {
            lines.Add("\t\"LastOwner\"\t\t\"" + lastOwner + "\"");
        }

        if (disabledDlc is not null)
        {
            lines.Add("\t\"DisabledDLC\"\t\t\"" + disabledDlc + "\"");
        }

        lines.Add("}");
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static GameInstallation NewInstall(string root, string installRoot, int? appId, string gameId = "stellaris")
        => new(
            GameId: gameId,
            DisplayName: gameId,
            Version: "test",
            InstallRoot: installRoot,
            LauncherSettingsPath: Path.Combine(installRoot, "launcher-settings.json"),
            Format: LoadFileFormat.ContentLoadV1,
            GameDataDir: Path.Combine(installRoot, "game"),
            DlcPath: Path.Combine(installRoot, "dlc"),
            ModDir: Path.Combine(installRoot, "mod"),
            ExePath: Path.Combine(installRoot, "stellaris.exe"),
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

    /// <summary>只报告状态的最小补丁服务；不碰任何文件。</summary>
    private sealed class FakePatchService : IGamePatchService
    {
        private readonly GamePatchStatus _status;
        private readonly int? _appId;
        private readonly string? _apiDirectory;
        private readonly string? _apiFileName;

        public FakePatchService(GamePatchStatus status, int? appId, string? apiDirectory, string? apiFileName)
        {
            _status = status;
            _appId = appId;
            _apiDirectory = apiDirectory;
            _apiFileName = apiFileName;
        }

        public string AssetsDirectory => Path.Combine(Path.GetTempPath(), "pdx-no-assets");

        public bool UsesExternalAssets => false;

        public GamePatchReport Inspect(GameInstallation game)
            => new(_status, _appId, _apiDirectory, _apiFileName, null, false, false, Array.Empty<string>());

        public GamePatchReport Apply(GameInstallation game) => Inspect(game);

        public GamePatchReport Revert(GameInstallation game) => Inspect(game);
    }

    /// <summary>可编程的假探针：既能返结果，也能断言「这条路不该探测」。</summary>
    private sealed class FakeProbe : ISteamDllProbe
    {
        private readonly Func<string, SteamDllProbeResult> _handler;

        public FakeProbe(Func<string, SteamDllProbeResult> handler) => _handler = handler;

        public SteamDllProbeResult Probe(string apiFilePath, int appId) => _handler(apiFilePath);
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
