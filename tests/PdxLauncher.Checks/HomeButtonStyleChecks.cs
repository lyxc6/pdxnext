namespace PdxLauncher.Checks;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 主页「继续 / 开始游戏」按钮的样式口径：没有官方按钮美术的来源（内置占位、随包自带素材）
/// 用 WinUI 原生按钮，有美术的来源（安装目录、在线缓存、Steam 胶囊）保留自定义皮肤。
/// <para>
/// 判据本身是纯函数，这里对五个 <see cref="ThemeAssetSource"/> 逐一断言；另加一条源码扫描，
/// 确认页面真的调用了这个判据——否则「判据对了但页面没用」编译器看不见。
/// 随包素材的解析用临时目录钉住：EU5 有 logo.png 时要有字标，没有时必须回落到 null。
/// </para>
/// </summary>
public static class HomeButtonStyleChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        CheckPolicyBySource();
        CheckHomePageUsesPolicy();
        CheckBundledThemesLogo();
        CheckBundledThemesRejectsUnsafeGameId();
        CheckEu5LogoAsset();

        return ChecksContext.Failures - before;
    }

    /// <summary>判据只认「没有按钮美术」的两种来源，其余三种必须保留皮肤。</summary>
    private static void CheckPolicyBySource()
    {
        ChecksContext.Check(
            "内置占位走原生样式",
            HomeButtonStylePolicy.UsesNativeStyle(ThemeAssetSource.BuiltInPlaceholder));
        ChecksContext.Check(
            "随包自带素材（EU5）走原生样式",
            HomeButtonStylePolicy.UsesNativeStyle(ThemeAssetSource.Bundled));
        ChecksContext.Check(
            "安装目录主题保留皮肤",
            !HomeButtonStylePolicy.UsesNativeStyle(ThemeAssetSource.InstallDir));
        ChecksContext.Check(
            "在线缓存主题保留皮肤",
            !HomeButtonStylePolicy.UsesNativeStyle(ThemeAssetSource.OnlineCache));
        ChecksContext.Check(
            "Steam 胶囊主题保留皮肤",
            !HomeButtonStylePolicy.UsesNativeStyle(ThemeAssetSource.SteamCapsule));
    }

    /// <summary>页面必须按 spec.Source 走判据，并且真的两枚按钮都调到原生分支。</summary>
    private static void CheckHomePageUsesPolicy()
    {
        var path = FindRepoFile(Path.Combine("src", "PdxLauncher.App", "Views", "HomePage.xaml.cs"));
        if (!ChecksContext.Check("找到 HomePage.xaml.cs", path is not null, path ?? "未找到"))
        {
            return;
        }

        var text = File.ReadAllText(path!);
        var guard = text.IndexOf("HomeButtonStylePolicy.UsesNativeStyle(spec.Source)", StringComparison.Ordinal);
        ChecksContext.Check("HomePage 用 spec.Source 调判据", guard >= 0, "index " + guard);
        ChecksContext.Check("原生分支给「继续」用 DefaultButtonStyle", text.Contains("\"DefaultButtonStyle\"", StringComparison.Ordinal));
        ChecksContext.Check("原生分支给「开始」用 AccentButtonStyle", text.Contains("\"AccentButtonStyle\"", StringComparison.Ordinal));
        ChecksContext.Equal("ApplyNativeButton 出现次数（声明 + 两次调用）", 3, CountOf(text, "ApplyNativeButton("));
        ChecksContext.Check("判据在原生分支之前生效", guard >= 0 && guard < text.IndexOf("ApplyNativeButton(", StringComparison.Ordinal));
    }

    /// <summary>同目录有 logo.png 才给字标；背景缺失时整个主题不成立。</summary>
    private static void CheckBundledThemesLogo()
    {
        var root = Path.Combine(Path.GetTempPath(), "pdx-bundled-themes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themeDir = Path.Combine(root, "Assets", "themes", "eu5");
            Directory.CreateDirectory(themeDir);
            var background = Path.Combine(themeDir, "background.jpg");
            var logo = Path.Combine(themeDir, "logo.png");
            File.WriteAllBytes(background, new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 });
            File.WriteAllBytes(logo, new byte[] { 0x89, 0x50, 0x4E, 0x47 });

            var themes = new BundledThemes(root);
            var spec = themes.TryResolve(Game("eu5"));

            if (ChecksContext.Check("有背景 + 字标时解析出主题", spec is not null))
            {
                ChecksContext.Equal("来源是 Bundled", ThemeAssetSource.Bundled, spec!.Source);
                ChecksContext.Equal("背景指向 background.jpg", background, spec.Background);
                ChecksContext.Equal("字标指向同目录 logo.png", logo, spec.Logo);
            }

            File.Delete(logo);
            var noLogo = themes.TryResolve(Game("eu5"));
            ChecksContext.Check("有背景、无 logo.png 时仍解析出主题", noLogo is not null);
            ChecksContext.Check("无 logo.png 时字标为 null", noLogo?.Logo is null, noLogo?.Logo);

            File.WriteAllBytes(logo, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
            File.Delete(background);
            ChecksContext.Check("只有 logo.png、没有背景时主题不成立", themes.TryResolve(Game("eu5")) is null);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>gameId 是外部输入：带分隔符 / 上跳 / 空白时不得拼进路径。</summary>
    private static void CheckBundledThemesRejectsUnsafeGameId()
    {
        var root = Path.Combine(Path.GetTempPath(), "pdx-bundled-themes-" + Guid.NewGuid().ToString("N"));
        try
        {
            var themes = new BundledThemes(root);
            ChecksContext.Check("gameId 为 .. 时拒绝", themes.TryResolve(Game("..")) is null);
            ChecksContext.Check("gameId 含分隔符时拒绝", themes.TryResolve(Game("eu5/../vic3")) is null);
            ChecksContext.Check("gameId 为空白时拒绝", themes.TryResolve(Game("  ")) is null);
        }
        finally
        {
            TryDeleteDirectory(root);
        }
    }

    /// <summary>真素材护栏：EU5 字标在仓库里、尺寸 1299×497、体积 ≤120 KiB，且不再带无人读取的 cover.jpg。</summary>
    private static void CheckEu5LogoAsset()
    {
        var themeDir = Path.Combine("src", "PdxLauncher.App", "Assets", "themes", "eu5");
        var logo = FindRepoFile(Path.Combine(themeDir, "logo.png"));
        if (!ChecksContext.Check("仓库里有 EU5 logo.png", logo is not null, logo ?? "未找到"))
        {
            return;
        }

        var bytes = File.ReadAllBytes(logo!);
        ChecksContext.Check("logo.png 体积 ≤120 KiB", bytes.Length <= 120 * 1024, bytes.Length + " B");
        ChecksContext.Check(
            "logo.png 是 PNG 签名",
            bytes.Length >= 24
                && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47);
        if (bytes.Length >= 24)
        {
            var width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
            var height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];
            ChecksContext.Equal("logo.png 宽", 1299, width);
            ChecksContext.Equal("logo.png 高", 497, height);
        }

        var cover = FindRepoFile(Path.Combine(themeDir, "cover.jpg"));
        ChecksContext.Check("EU5 目录不再带无人读取的 cover.jpg", cover is null, cover ?? "已删除");
    }

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
            ButtonTextColor: "#FFFFFF",
            ButtonHoverTextColor: "#FFFFFF",
            FontSize: 14,
            FontWeight: 400,
            TextShadow: null,
            Source: ThemeAssetSource.BuiltInPlaceholder),
        BrowserDlcUrl: null,
        BrowserModUrl: null,
        SteamAppId: null,
        Support: GameSupport.LaunchOnly);

    private static int CountOf(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }

    /// <summary>
    /// 从检查程序目录逐级上溯找仓库文件（与 ChecksContext.FixturePath 同一套做法）。
    /// 到含 .git 的目录就停：worktree 的上一级是主工作树，越过边界会读到别的分支的文件。
    /// </summary>
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

            var isRepoRoot = Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, ".git"));
            if (isRepoRoot)
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
}
