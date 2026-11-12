namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// <see cref="SteamApiLocator"/> 的候选选择自检。
/// <para>
/// 真机事故有两起：① CK3 的 <c>binaries</c> 里有个遗留杂项 <c>123456steam_api64.dll</c>，
/// 按「通配 + 枚举顺序」会被排在真名前面；② 同目录的 <c>steam_api64.dll</c> 可能是 CreamAPI
/// 之类的 loader（834,560 B），宿主动词拿它读成就直接 0xC0000005，原版被改名成
/// <c>steam_api64_o.dll</c> 留在旁边。这里用临时目录把口径钉死：
/// 原版备份优先、精确名优先于杂项、变体按离标准名的距离排序、体积门槛。
/// </para>
/// <para>纯本地用例，不加载任何真实 Steam DLL（加载与崩溃回退由假宿主进程验证）。</para>
/// </summary>
public static class SteamApiLocatorChecks
{
    /// <summary>够过 <see cref="SteamApiLocator.MinimumSizeBytes"/> 的填充体积（内容不重要）。</summary>
    private const int FillerBytes = 128 * 1024;

    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckBackupWinsOverLoader(fs);
        CheckConfiguredDirectoryYieldsBackups(fs);
        CheckExactNameWinsWithoutBackup(fs);
        CheckVariantsAreOrderedByDistance(fs);
        CheckOnlyLoaderStillResolves(fs);
        CheckOnlyOriginalBackup(fs);
        CheckThirtyTwoBitFallback(fs);
        CheckUnityPluginsDirectory(fs);
        CheckMinimumSize(fs);
        CheckNothingUsable(fs);
        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 打过 DLC 解锁补丁的目录：标准名被 loader 占了，原版在 <c>steam_api64_o.dll</c>。
    /// 成就必须选原版，且候选顺序里它排在标准名之前。
    /// </summary>
    private static void CheckBackupWinsOverLoader(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "loader");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var loader = fs.Combine(binaries, "steam_api64.dll");
            var original = fs.Combine(binaries, "steam_api64_o.dll");
            fs.WriteAllBytesAtomic(loader, new byte[834560]);
            fs.WriteAllBytesAtomic(original, new byte[298384]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal(
                "定位: 标准名是 loader、旁边有原版备份时选原版",
                original,
                locator.Resolve(null, null, game));

            var candidates = locator.Candidates(null, null, game).ToList();
            var originalIndex = IndexOf(candidates, original);
            var loaderIndex = IndexOf(candidates, loader);
            ChecksContext.Check(
                "定位: 原版备份排在 loader 之前",
                originalIndex >= 0 && loaderIndex >= 0 && originalIndex < loaderIndex,
                "原版序号 " + originalIndex + "，loader 序号 " + loaderIndex);
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>
    /// 设置里手填的是目录（不是 dll）：目录里的原版备份也要进候选，且排在 loader 之前。
    /// 旧实现只把 <c>&lt;dir&gt;\steam_api64.dll</c> 放进去，于是 loader 排第一，打补丁的游戏一直 initFailed。
    /// </summary>
    private static void CheckConfiguredDirectoryYieldsBackups(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "configured-directory");
        try
        {
            var configured = fs.Combine(root, "binaries");
            fs.EnsureDirectory(configured);

            var loader = fs.Combine(configured, "steam_api64.dll");
            var original = fs.Combine(configured, "steam_api64_o.dll");
            fs.WriteAllBytesAtomic(loader, new byte[834560]);
            fs.WriteAllBytesAtomic(original, new byte[298384]);

            var locator = new SteamApiLocator(fs, new SilentLogger());

            var candidates = locator.Candidates(configured, null, null).ToList();
            var originalIndex = IndexOf(candidates, original);
            var loaderIndex = IndexOf(candidates, loader);
            ChecksContext.Check(
                "定位: 手填目录时原版备份进候选",
                originalIndex >= 0,
                "未进候选：" + string.Join("；", candidates));
            ChecksContext.Check("定位: 手填目录时 loader 也在候选里", loaderIndex >= 0, "未进候选");
            ChecksContext.Check(
                "定位: 手填目录时原版备份排在 loader 之前",
                originalIndex >= 0 && loaderIndex >= 0 && originalIndex < loaderIndex,
                "备份序号 " + originalIndex + "，loader 序号 " + loaderIndex);
            ChecksContext.Equal("定位: 手填目录优先选中原版备份", original, locator.Resolve(configured, null, null));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>没有备份时，同一目录里既有真名又有 <c>123456…</c> 杂项，必须选真名。</summary>
    private static void CheckExactNameWinsWithoutBackup(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "exact");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var real = fs.Combine(binaries, "steam_api64.dll");
            var junk = fs.Combine(binaries, "123456steam_api64.dll");
            fs.WriteAllBytesAtomic(real, new byte[FillerBytes]);
            fs.WriteAllBytesAtomic(junk, new byte[FillerBytes * 2]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal(
                "定位: 真名与 123456 杂项同目录时选真名",
                real,
                locator.Resolve(null, null, game));

            var candidates = locator.Candidates(null, null, game).ToList();
            var realIndex = IndexOf(candidates, real);
            var junkIndex = IndexOf(candidates, junk);
            ChecksContext.Check("定位: 游戏目录候选里包含真名", realIndex >= 0, "真名未进候选");
            ChecksContext.Check("定位: 游戏目录候选里包含杂项", junkIndex >= 0, "杂项未进候选");
            ChecksContext.Check(
                "定位: 杂项排在真名之后",
                realIndex >= 0 && junkIndex >= 0 && junkIndex > realIndex,
                "真名序号 " + realIndex + "，杂项序号 " + junkIndex);
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>精确名缺席时，变体按「离标准名的字符差」排序，而不是字典序（否则 123456… 会抢先）。</summary>
    private static void CheckVariantsAreOrderedByDistance(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "variants");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var junk = fs.Combine(binaries, "123456steam_api64.dll");
            var backup = fs.Combine(binaries, "steam_api64_o.dll");
            fs.WriteAllBytesAtomic(junk, new byte[FillerBytes]);
            fs.WriteAllBytesAtomic(backup, new byte[FillerBytes]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal(
                "定位: 只有变体时选离标准名更近的那份（不按字典序）",
                backup,
                locator.Resolve(null, null, game));

            var firstPass = locator.Candidates(null, null, game);
            var secondPass = locator.Candidates(null, null, game);
            ChecksContext.Check(
                "定位: 候选顺序稳定（两次枚举一致）",
                firstPass.SequenceEqual(secondPass, StringComparer.OrdinalIgnoreCase),
                string.Join("；", firstPass));

            var variantOrder = firstPass
                .Where(path => path.StartsWith(binaries, StringComparison.OrdinalIgnoreCase) && fs.FileExists(path))
                .ToList();
            ChecksContext.Equal("定位: 变体里 steam_api64_o.dll 排第一", 0, variantOrder.IndexOf(backup));
            ChecksContext.Equal("定位: 变体里 123456steam_api64.dll 排第二", 1, variantOrder.IndexOf(junk));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>只有 loader 一份时，定位器仍要把它交出去：能不能用由宿主的导出表探测决定，不靠体积猜。</summary>
    private static void CheckOnlyLoaderStillResolves(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "only-loader");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var loader = fs.Combine(binaries, "steam_api64.dll");
            fs.WriteAllBytesAtomic(loader, new byte[834560]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal("定位: 只有 loader 时仍返回它（由子进程探测否决）", loader, locator.Resolve(null, null, game));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>只有原版备份（标准名被删/被改名）时也要能选中。</summary>
    private static void CheckOnlyOriginalBackup(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "only-original");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var original = fs.Combine(binaries, "steam_api64_orig.dll");
            fs.WriteAllBytesAtomic(original, new byte[298384]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal("定位: 只有 steam_api64_orig.dll 时也能选中", original, locator.Resolve(null, null, game));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>只有 32 位 <c>steam_api.dll</c> 时也能用（降级候选）。</summary>
    private static void CheckThirtyTwoBitFallback(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "x86");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var x86 = fs.Combine(binaries, "steam_api.dll");
            fs.WriteAllBytesAtomic(x86, new byte[FillerBytes]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal("定位: 64 位缺席时降级到 steam_api.dll", x86, locator.Resolve(null, null, game));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>
    /// Unity 引擎的游戏（CS2）把 api 放在 <c>Cities2_Data\Plugins\x86_64</c>，
    /// 既不在安装根也不在 binaries。不认这个目录的话，CS2 成就一个候选都找不到。
    /// </summary>
    private static void CheckUnityPluginsDirectory(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "unity-plugins");
        try
        {
            var plugins = fs.Combine(root, "Cities2_Data", "Plugins", "x86_64");
            fs.EnsureDirectory(plugins);

            var api = fs.Combine(plugins, SteamApiLocator.FileName);
            fs.WriteAllBytesAtomic(api, new byte[FillerBytes]);

            var game = MakeGame(fs, root, "cities_skylines_2");
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Equal(
                "定位: CS2 的 Cities2_Data\\Plugins\\x86_64 也能被选中",
                api,
                locator.Resolve(null, null, game));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>体积明显不像 DLL（0 字节占位等）的候选要提前拒掉。</summary>
    private static void CheckMinimumSize(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "tiny");
        try
        {
            var binaries = fs.Combine(root, "binaries");
            fs.EnsureDirectory(binaries);

            var tiny = fs.Combine(binaries, "steam_api64.dll");
            fs.WriteAllBytesAtomic(tiny, new byte[256]);

            var game = MakeGame(fs, root);
            var locator = new SteamApiLocator(fs, new SilentLogger());

            ChecksContext.Check("定位: 过小的占位文件不被选中", locator.Resolve(null, null, game) is null);
            ChecksContext.Check(
                "定位: LooksUsable 对过小文件给出体积原因",
                !locator.LooksUsable(tiny, out var reason) && reason is { Length: > 0 } && reason!.Contains("体积", StringComparison.Ordinal),
                reason ?? "null");
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    /// <summary>没有任何候选时返回 null 并记 Warn（调用方据此报 Steam 不可用）。</summary>
    private static void CheckNothingUsable(IFileSystem fs)
    {
        var root = NewTempRoot(fs, "empty");
        try
        {
            fs.EnsureDirectory(fs.Combine(root, "binaries"));
            var game = MakeGame(fs, root);
            var logger = new RecordingLogger();
            var locator = new SteamApiLocator(fs, logger);

            ChecksContext.Check("定位: 空目录返回 null", locator.Resolve(null, null, game) is null);
            ChecksContext.Check(
                "定位: 找不到时记 Warn 并列出候选",
                logger.Warnings.Any(line => line.Contains("未找到可用的 steam_api64.dll", StringComparison.Ordinal)),
                string.Join(" | ", logger.Warnings));
        }
        finally
        {
            SafeDelete(fs, root);
        }
    }

    // ------------------------------------------------------------ 夹具

    private static int IndexOf(List<string> paths, string target) =>
        paths.FindIndex(path => string.Equals(path, target, StringComparison.OrdinalIgnoreCase));

    internal static GameInstallation MakeGame(IFileSystem fs, string root, string gameId = "ck3") => new(
        GameId: gameId,
        DisplayName: "Crusader Kings III",
        Version: "1.0.0",
        InstallRoot: root,
        LauncherSettingsPath: fs.Combine(root, "launcher-settings.json"),
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: fs.Combine(root, "user"),
        DlcPath: fs.Combine(root, "dlc"),
        ModDir: fs.Combine(root, "mod"),
        ExePath: fs.Combine(root, "binaries", "ck3.exe"),
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
        Support: GameSupport.Full);

    private static string NewTempRoot(IFileSystem fs, string name)
    {
        var root = fs.Combine(Path.GetTempPath(), "pdxnext-locator-" + name + "-" + Guid.NewGuid().ToString("N"));
        fs.EnsureDirectory(root);
        return root;
    }

    private static void SafeDelete(IFileSystem fs, string directory)
    {
        try
        {
            if (fs.DirectoryExists(directory))
            {
                fs.Delete(directory);
            }
        }
        catch
        {
            // 清理失败不影响断言结果。
        }
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

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);
    }
}
