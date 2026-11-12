namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Themes;

/// <summary>
/// EU5 主页「金色字标」的整条链路：内置素材（<c>Assets/themes/eu5/{background.jpg,logo.png}</c>）
/// 必须把背景与 Logo 分别补齐，二者互不牵连。
/// <para>
/// 旧实现 <c>ThemeResolver.ApplyBundled</c> 在「背景非空」时提前返回、且全程不读 bundled.Logo，
/// 于是 EU5 不管清缓存首次（Bundled）还是联网 hero 命中（背景来自缓存）字标都是 null，
/// 主页 <c>HomePage.xaml</c> 便退回纯文字标题。这里用真实 <see cref="ThemeResolver"/>
/// 走离线解析路径钉住四种组合，再接 <see cref="ThemePatch.Apply"/> 证明
/// <c>game.Theme.Logo</c> 最终非空。
/// </para>
/// <para>
/// 覆盖盲区：<c>OnlineChecks.FakeBundledThemes</c> 硬编码 <c>Logo=null</c>，所以既有自检
/// 永远测不出「字标没补」。本类改用真实的 <c>BundledThemes</c> + 临时目录，字标是真实存在的文件。
/// </para>
/// </summary>
public static class BundledThemeChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckBundledFallback(fs);
        CheckHomeWiring();

        return ChecksContext.Failures - before;
    }

    /// <summary>
    /// 四条组合，全部走「不联网」快路径（allowOnlineRefresh: false），
    /// metadata / 下载器都是「一碰就抛」的替身，能同时证明离线解析不触网。
    /// </summary>
    private static void CheckBundledFallback(IFileSystem fs)
    {
        var root = Path.Combine(Path.GetTempPath(), "pdx-eu5-logo-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themeDir = Path.Combine(root, "Assets", "themes", "eu5");
            Directory.CreateDirectory(themeDir);
            var bundledBackground = Path.Combine(themeDir, "background.jpg");
            var bundledLogo = Path.Combine(themeDir, "logo.png");
            File.WriteAllBytes(bundledBackground, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            File.WriteAllBytes(bundledLogo, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var cacheRoot = Path.Combine(root, "theme-cache");
            Directory.CreateDirectory(cacheRoot);

            var logger = new RecordingLogger();
            var metadata = new ForbiddenMetadataClient();
            var downloader = new ForbiddenAssetDownloader();
            var resolver = new ThemeResolver(
                fs,
                new FakeClock(new DateTimeOffset(2026, 11, 5, 12, 0, 0, TimeSpan.Zero)),
                logger,
                metadata,
                downloader,
                cacheRoot,
                steamCovers: null,
                bundled: new BundledThemes(root));
            var game = Game("eu5");

            // ① 清缓存、无 hero：背景与字标都来自内置素材，来源 Bundled。
            var offline = Resolve(resolver, game);
            ChecksContext.Equal("① 清缓存离线：背景来自内置素材", bundledBackground, offline.Background);
            ChecksContext.Equal("① 清缓存离线：字标来自内置素材", bundledLogo, offline.Logo);
            ChecksContext.Equal("③ 纯内置兜底：来源 = Bundled", ThemeAssetSource.Bundled, offline.Source);
            ChecksContext.Check(
                "① 离线解析全程不触网",
                metadata.Calls == 0 && downloader.DownloadCalls == 0 && downloader.ConditionalCalls == 0,
                "metadata=" + metadata.Calls + ", download=" + downloader.DownloadCalls
                    + ", conditional=" + downloader.ConditionalCalls);

            // ② 已经有 hero 背景缓存（联网成功后重启的形态）：背景保持 hero，字标仍要补。
            var heroPath = Path.Combine(cacheRoot, "eu5-background-hero.jpg");
            File.WriteAllBytes(heroPath, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            SaveIndex(fs, logger, resolver, game.GameId, heroPath, ThemeResolver.SteamHeroSource);

            var hero = Resolve(resolver, game);
            ChecksContext.Equal("② hero 背景已缓存：背景仍是 hero 缓存", heroPath, hero.Background);
            ChecksContext.Equal("② hero 背景已缓存：字标仍来自内置素材", bundledLogo, hero.Logo);
            ChecksContext.Equal("③ hero 命中：来源保持 SteamHero", ThemeAssetSource.SteamHero, hero.Source);
            ChecksContext.Check(
                "② hero 命中时没有重新下载",
                downloader.ConditionalCalls == 0 && downloader.DownloadCalls == 0,
                "download=" + downloader.DownloadCalls + ", conditional=" + downloader.ConditionalCalls);

            // ③ 普通在线缓存背景（来源不是 steam-hero）：来源保持 OnlineCache，字标照样补。
            var onlinePath = Path.Combine(cacheRoot, "eu5-background-online.png");
            File.WriteAllBytes(onlinePath, new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
            SaveIndex(fs, logger, resolver, game.GameId, onlinePath, "metadata");

            var online = Resolve(resolver, game);
            ChecksContext.Equal("③ 在线缓存背景：背景不被内置素材覆盖", onlinePath, online.Background);
            ChecksContext.Equal("③ 在线缓存背景：字标来自内置素材", bundledLogo, online.Logo);
            ChecksContext.Equal("③ 在线缓存背景：来源保持 OnlineCache", ThemeAssetSource.OnlineCache, online.Source);

            // ④ 设置页文案所依赖的来源语义：hero 命中不能被改写成 Bundled。
            ChecksContext.Check(
                "③ hero 命中时来源不是 Bundled（设置页文案不能变）",
                hero.Source != ThemeAssetSource.Bundled,
                hero.Source.ToString());

            // ⑤ 全链路探针：ThemeResolver → ThemePatch.Apply → game.Theme.Logo。
            //    这一步等价于 ThemeUpdateService.ApplyCachedLayerAsync 的写回路径。
            var patched = ThemePatch.Apply(new[] { game }, game.GameId, offline);
            ChecksContext.Check(
                "④ ThemePatch 整对象替换 ThemeSpec",
                patched.Count == 1 && ReferenceEquals(patched[0].Theme, offline));
            ChecksContext.Check(
                "④ apply 之后 game.Theme.Logo 非空",
                !string.IsNullOrWhiteSpace(patched[0].Theme.Logo),
                patched[0].Theme.Logo ?? "(null)");
            ChecksContext.Equal("④ apply 之后 game.Theme.Logo 指向内置字标", bundledLogo, patched[0].Theme.Logo);
            ChecksContext.Equal("④ apply 之后背景也非空", bundledBackground, patched[0].Theme.Background);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>
    /// 只钉两条接线事实：视图模型把 <c>game.Theme.Logo</c> 映射成 <c>LogoPath</c>，
    /// 页面用 <c>LogoPath</c> 控制字标/纯文字标题的可见性。
    /// 逻辑本身由上面的探针覆盖，不用源码字符串扫描去重复判逻辑。
    /// </summary>
    private static void CheckHomeWiring()
    {
        var viewModel = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "ViewModels", "HomeViewModel.cs"));
        if (ChecksContext.Check("找到 HomeViewModel.cs", viewModel is not null, viewModel ?? "未找到"))
        {
            var text = File.ReadAllText(viewModel!);
            ChecksContext.Check(
                "HomeViewModel 用 game.Theme.Logo 赋 LogoPath",
                text.Contains("LogoPath = game.Theme.Logo ?? string.Empty;", StringComparison.Ordinal));
        }

        var page = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml"));
        if (ChecksContext.Check("找到 HomePage.xaml", page is not null, page ?? "未找到"))
        {
            var text = File.ReadAllText(page!);
            ChecksContext.Check(
                "HomePage 用 LogoPath 显示字标（非空 Visible）",
                text.Contains("FromString(ViewModel.LogoPath)", StringComparison.Ordinal));
            ChecksContext.Check(
                "HomePage 用 LogoPath 隐藏纯文字标题（非空 Collapsed）",
                text.Contains("FromStringInverse(ViewModel.LogoPath)", StringComparison.Ordinal));
        }
    }

    private static ThemeSpec Resolve(ThemeResolver resolver, GameInstallation game) =>
        resolver.ResolveAsync(game, allowOnlineRefresh: false, CancellationToken.None)
            .GetAwaiter()
            .GetResult();

    private static void SaveIndex(
        IFileSystem fs,
        IAppLogger logger,
        ThemeResolver resolver,
        string gameId,
        string cachedPath,
        string source)
    {
        var index = AssetIndex.Load(fs, logger, resolver.IndexPath(gameId));
        index.Set(
            AssetKeys.Background,
            "https://example.com/" + Path.GetFileName(cachedPath),
            Path.GetFileName(cachedPath),
            etag: null,
            lastModified: null,
            source: source);
        index.Save(fs, logger, resolver.IndexPath(gameId));
    }

    /// <summary>EU5 现状：安装目录没有主题素材，层2 只有颜色兜底。</summary>
    private static GameInstallation Game(string gameId) => new(
        GameId: gameId,
        DisplayName: gameId,
        Version: "test",
        InstallRoot: Path.Combine(Path.GetTempPath(), "pdx-game-" + gameId),
        LauncherSettingsPath: string.Empty,
        Format: LoadFileFormat.ContentLoadV1,
        GameDataDir: string.Empty,
        DlcPath: string.Empty,
        ModDir: string.Empty,
        ExePath: string.Empty,
        ExeArgs: Array.Empty<string>(),
        Alternatives: Array.Empty<AlternativeExecutable>(),
        Theme: new ThemeSpec(
            Background: null,
            Logo: null,
            AppIcon: null,
            AppTaskbarIcon: null,
            ButtonBackground: null,
            ButtonFont: null,
            ButtonTextColor: ThemeResolver.ButtonTextColorFallback,
            ButtonHoverTextColor: ThemeResolver.ButtonHoverTextColorFallback,
            FontSize: ThemeResolver.FontSizeFallback,
            FontWeight: ThemeResolver.FontWeightFallback,
            TextShadow: null,
            Source: ThemeAssetSource.BuiltInPlaceholder),
        BrowserDlcUrl: null,
        BrowserModUrl: null,
        SteamAppId: 3450310,
        Support: GameSupport.LaunchOnly);

    /// <summary>同一套上溯规则：到含 .git 的目录就停，避免读到主工作树的文件。</summary>
    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            if (Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, ".git")))
            {
                break;
            }

            dir = dir.Parent;
        }

        var fromCwd = Path.Combine(Directory.GetCurrentDirectory(), relative);
        return File.Exists(fromCwd) ? fromCwd : null;
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
            // 临时目录删不掉不影响断言结论。
        }
        catch (UnauthorizedAccessException)
        {
            // 同上。
        }
    }

    // ------------------------------------------------------------- 测试替身

    private sealed class RecordingLogger : IAppLogger
    {
        public List<string> Warnings { get; } = new();

        public void Info(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message);

        public void Error(string message, Exception? exception = null) => Warnings.Add(message);
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset now) => UtcNow = now;

        public DateTimeOffset UtcNow { get; }
    }

    /// <summary>离线路径一碰就该抛：把「解析主题顺便联网」这种回归变成红断言。</summary>
    private sealed class ForbiddenMetadataClient : IMetadataClient
    {
        public int Calls { get; private set; }

        public Task<IReadOnlyList<GameMetadataDto>> GetMetadataAsync(
            IReadOnlyList<string> gameIds,
            CancellationToken cancellationToken)
        {
            Calls++;
            throw new InvalidOperationException("离线主题解析不得访问 metadata");
        }
    }

    /// <summary>离线路径一碰就该抛；同时计数，供断言确认没有下载尝试。</summary>
    private sealed class ForbiddenAssetDownloader : IAssetDownloader
    {
        public int DownloadCalls { get; private set; }

        public int ConditionalCalls { get; private set; }

        public Task<string?> DownloadAsync(
            string url,
            string targetPathWithoutExtension,
            CancellationToken cancellationToken)
        {
            DownloadCalls++;
            throw new InvalidOperationException("离线主题解析不得下载素材");
        }

        public Task<AssetDownloadResult> DownloadConditionalAsync(
            string url,
            string targetPathWithoutExtension,
            string? etag,
            string? lastModified,
            CancellationToken cancellationToken)
        {
            ConditionalCalls++;
            throw new InvalidOperationException("离线主题解析不得下载素材");
        }
    }
}
