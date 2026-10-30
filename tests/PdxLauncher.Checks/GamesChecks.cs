namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.Online;
using PdxLauncher.Core.Parsing;
using PdxLauncher.Core.Steam;
using PdxLauncher.Core.Themes;

/// <summary>
/// T1 core-games 自检：Clausewitz / VDF 解析、Steam 库探测、appmanifest、
/// launcher-settings、ThemeReader、HslaColor、GameCatalog（含真机断言）。
/// </summary>
public static class GamesChecks
{
    /// <summary>运行全部断言；返回 0 表示全部通过。</summary>
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var logger = new NullLogger();

        CheckClausewitz(fs, logger);
        CheckValveKeyValues();
        CheckLauncherSettings(fs, logger);
        CheckSyntheticLayout(fs, logger);
        CheckThemeReader(fs, logger);
        CheckColors();
        CheckSyntheticLibrary(fs, logger);
        CheckSignatureDetection(fs, logger);
        CheckStandaloneLibrary(fs, logger);
        CheckLauncherLibraryPaths(fs, logger);
        CheckSupportTiers();
        CheckRealMachine(fs, logger);

        return ChecksContext.Failures == before ? 0 : 1;
    }

    // ---------------------------------------------------------------- 解析器

    private static void CheckClausewitz(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] Clausewitz 解析");

        var ck3Path = ChecksContext.FixturePath("ck3/dlc001.dlc");
        var ck3 = ClausewitzParser.ParseFile(fs, ck3Path, logger);
        ChecksContext.Equal("ck3 .dlc 键数量", 8, ck3.Count);
        ChecksContext.Equal("ck3 .dlc name", "Garments of the Holy Roman Empire", Get(ck3, "name"));
        ChecksContext.Equal("ck3 .dlc steam_id", "1296730", Get(ck3, "steam_id"));
        ChecksContext.Equal("ck3 .dlc pops_id", "ck3_dlc001_preorder", Get(ck3, "pops_id"));
        ChecksContext.Equal("ck3 .dlc affects_checksum", "yes", Get(ck3, "affects_checksum"));
        ChecksContext.Equal("ck3 .dlc path", "dlc/dlc001_preorder", Get(ck3, "path"));

        var stellarisPath = ChecksContext.FixturePath("stellaris/dlc001.dlc");
        var stellaris = ClausewitzParser.ParseFile(fs, stellarisPath, logger);
        ChecksContext.Equal("stellaris .dlc 键数量", 13, stellaris.Count);
        ChecksContext.Equal("stellaris .dlc steam_id（无引号数字）", "447680", Get(stellaris, "steam_id"));
        ChecksContext.Equal("stellaris .dlc category（等号无空格）", "content_pack", Get(stellaris, "category"));
        ChecksContext.Equal("stellaris .dlc affects_checksum", "no", Get(stellaris, "affects_checksum"));
        ChecksContext.Equal("stellaris .dlc 空值 msgr_id", string.Empty, Get(stellaris, "msgr_id"));
        ChecksContext.Equal("stellaris .dlc archive", "dlc/dlc001_symbols_of_domination/dlc001.zip", Get(stellaris, "archive"));

        var crlf = new string(new[] { (char)13, (char)10 });
        var messy = (char)0xFEFF + "# 注释行" + crlf
            + "name = \"first\"" + crlf
            + "tags={ \"A\" \"B\" }" + crlf
            + "nested_block = {" + crlf
            + "    inner = \"x\"" + crlf
            + "}" + crlf
            + "steam_id = 123" + crlf
            + "empty = \"\"" + crlf
            + "name = \"second\"" + crlf
            + crlf;
        var parsed = ClausewitzParser.Parse(messy);
        ChecksContext.Equal("容错: 重复键后者覆盖", "second", Get(parsed, "name"));
        ChecksContext.Equal("容错: 无引号数字值", "123", Get(parsed, "steam_id"));
        ChecksContext.Equal("容错: 空引号值", string.Empty, Get(parsed, "empty"));
        ChecksContext.Check("容错: tags 块不进字典", !parsed.ContainsKey("tags"));
        ChecksContext.Check("容错: 嵌套块不进字典", !parsed.ContainsKey("nested_block") && !parsed.ContainsKey("inner"));
        ChecksContext.Equal("容错: BOM/注释/CRLF 后键数量", 3, parsed.Count);
    }

    private static void CheckValveKeyValues()
    {
        Console.WriteLine("[GamesChecks] Valve KeyValues 解析");

        var nl = Environment.NewLine;
        var vdf = string.Join(nl, new[]
        {
            "\"libraryfolders\"",
            "{",
            "\t\"0\"",
            "\t{",
            "\t\t\"path\"\t\t\"D:/app/Steam\"",
            "\t\t\"label\"\t\t\"\"",
            "\t}",
            "\t\"1\"",
            "\t{",
            "\t\t\"path\"\t\t\"E:/SteamLibrary\"",
            "\t\t\"apps\"",
            "\t\t{",
            "\t\t\t\"529340\"\t\t\"1234\"",
            "\t\t}",
            "\t}",
            "\t\"contentstatsid\"\t\t\"-1234567\"",
            "}",
        });

        var root = ValveKeyValuesParser.Parse(vdf);
        var container = root.FindChild("libraryfolders") ?? root;
        var paths = new List<string>();
        foreach (var child in container.Children)
        {
            if (!child.IsBlock)
            {
                continue;
            }

            var path = child.GetValue("path");
            if (!string.IsNullOrWhiteSpace(path))
            {
                paths.Add(path.Replace('/', (char)92));
            }
        }

        ChecksContext.Equal("libraryfolders.vdf 库数量", 2, paths.Count);
        ChecksContext.Equal("libraryfolders.vdf 库 0", "D:" + (char)92 + "app" + (char)92 + "Steam", paths.Count > 0 ? paths[0] : "<none>");
        ChecksContext.Equal("libraryfolders.vdf 库 1", "E:" + (char)92 + "SteamLibrary", paths.Count > 1 ? paths[1] : "<none>");

        var oldSchema = "\"LibraryFolders\"" + nl + "{" + nl + "\t\"TimeNextStatsReport\"\t\t\"0\"" + nl
            + "\t\"1\"\t\t\"F:/SteamLibrary\"" + nl + "}";
        var oldRoot = ValveKeyValuesParser.Parse(oldSchema);
        var oldContainer = oldRoot.FindChild("LibraryFolders") ?? oldRoot;
        var oldPath = oldContainer.GetValue("1");
        ChecksContext.Equal("旧 schema libraryfolders 值", "F:/SteamLibrary", oldPath);

        var acf = string.Join(nl, new[]
        {
            "\"AppState\"",
            "{",
            "\t\"appid\"\t\t\"1158310\"",
            "\t\"name\"\t\t\"Crusader Kings III\"",
            "\t\"StateFlags\"\t\t\"4\"",
            "\t\"installdir\"\t\t\"Crusader Kings III\"",
            "\t\"SizeOnDisk\"\t\t\"123456789\"",
            "}",
        });
        var acfRoot = ValveKeyValuesParser.Parse(acf);
        var appState = acfRoot.FindChild("AppState") ?? acfRoot;
        ChecksContext.Equal("appmanifest appid", "1158310", appState.GetValue("appid"));
        ChecksContext.Equal("appmanifest installdir", "Crusader Kings III", appState.GetValue("installdir"));
        ChecksContext.Equal("appmanifest StateFlags", "4", appState.GetValue("StateFlags"));

        // 坏文件必须容错：空字符串、半截块
        var broken = ValveKeyValuesParser.Parse("\"a\" { \"b\" \"c\" ");
        ChecksContext.Check("VDF 半截块不抛异常且仍能取到键", broken.FindChild("a") is not null);

        var empty = ValveKeyValuesParser.Parse(string.Empty);
        ChecksContext.Equal("VDF 空文本子节点数", 0, empty.Children.Count);
    }

    // ------------------------------------------------------- launcher-settings

    private static void CheckLauncherSettings(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] launcher-settings 解析");
        var reader = new LauncherSettingsReader(fs, logger);
        var fixtures = Path.GetDirectoryName(ChecksContext.FixturePath("ck3/launcher-settings.json")) ?? string.Empty;
        var root = Path.GetDirectoryName(fixtures) ?? string.Empty;

        var ck3 = reader.ReadFile(ChecksContext.FixturePath("ck3/launcher-settings.json"));
        ChecksContext.Check("ck3 launcher-settings 解析成功", ck3 is not null);
        if (ck3 is not null)
        {
            ChecksContext.Equal("ck3 gameId", "ck3", ck3.GameId);
            ChecksContext.Equal("ck3 displayName", "Crusader Kings III", ck3.DisplayName);
            ChecksContext.Equal("ck3 version", "1.20.1.2 (Crozier)", ck3.Version);
            ChecksContext.Equal("ck3 rawVersion", "1.20.1.2", ck3.RawVersion);
            ChecksContext.Equal("ck3 formatVersion", 0.0, ck3.FormatVersion);
            ChecksContext.Equal("ck3 Format", LoadFileFormat.DlcLoadV0, ck3.Format);
            // dlcPath 的基准是 launcher-settings.json 所在目录（fixtures/ck3），
            // 所以 "../game" 落在 fixtures/game，而不是 fixtures/ck3/game。
            ChecksContext.Check(
                "ck3 dlcPath = <settings dir>/../game",
                Norm(ck3.DlcPath).EndsWith("/game", StringComparison.OrdinalIgnoreCase),
                ck3.DlcPath);
            ChecksContext.Check(
                "ck3 exePath 以 ck3.exe 结尾",
                Norm(ck3.ExePath).EndsWith("binaries/ck3.exe", StringComparison.OrdinalIgnoreCase),
                ck3.ExePath);
            ChecksContext.Check(
                "ck3 gameDataDir 展开到我的文档",
                Norm(ck3.GameDataDir).EndsWith("Paradox Interactive/Crusader Kings III", StringComparison.OrdinalIgnoreCase),
                ck3.GameDataDir);
            ChecksContext.Equal("ck3 exeArgs 数量", 1, ck3.ExeArgs.Count);
            ChecksContext.Equal("ck3 exeArgs[0]", "-gdpr-compliant", ck3.ExeArgs.Count > 0 ? ck3.ExeArgs[0] : "<none>");
            ChecksContext.Equal("ck3 备选可执行文件数量", 1, ck3.Alternatives.Count);
            ChecksContext.Equal(
                "ck3 备选 label 取 zh-hans",
                "以调试模式打开游戏",
                ck3.Alternatives.Count > 0 ? ck3.Alternatives[0].DisplayLabel : "<none>");
            ChecksContext.Check("ck3 browserDlcUrl 非空", ck3.BrowserDlcUrl is not null, ck3.BrowserDlcUrl ?? "<null>");
            // themeFile 同样相对 settings 目录："./assets/theme-settings.json" → fixtures/ck3/assets/...
            ChecksContext.Check(
                "ck3 themeFile 绝对化",
                ck3.ThemeFile is not null && Norm(ck3.ThemeFile).EndsWith("assets/theme-settings.json", StringComparison.OrdinalIgnoreCase),
                ck3.ThemeFile ?? "<null>");
        }

        var v3 = reader.ReadFile(ChecksContext.FixturePath("victoria3/launcher-settings.json"));
        ChecksContext.Check("victoria3 launcher-settings 解析成功", v3 is not null);
        if (v3 is not null)
        {
            ChecksContext.Equal("victoria3 gameId", "victoria3", v3.GameId);
            ChecksContext.Equal("victoria3 formatVersion", 1.1, v3.FormatVersion);
            ChecksContext.Equal("victoria3 Format", LoadFileFormat.ContentLoadV1_1, v3.Format);
            ChecksContext.Check("victoria3 空 browserDlcUrl → null", v3.BrowserDlcUrl is null, v3.BrowserDlcUrl ?? "<null>");
            ChecksContext.Check("victoria3 isLooseFilesModSupported", v3.IsLooseFilesModSupported);
            ChecksContext.Check(
                "victoria3 modDir 以 Victoria 3/mod 结尾",
                Norm(v3.ModDir).EndsWith("Victoria 3/mod", StringComparison.OrdinalIgnoreCase),
                v3.ModDir);
            ChecksContext.Check(
                "victoria3 dlcPath = <settings dir>/../game",
                Norm(v3.DlcPath).EndsWith("/game", StringComparison.OrdinalIgnoreCase),
                v3.DlcPath);
            ChecksContext.Equal(
                "victoria3 备选 label 取 zh-hans",
                "以调试模式打开游戏",
                v3.Alternatives.Count > 0 ? v3.Alternatives[0].DisplayLabel : "<none>");
        }

        var stellaris = reader.ReadFile(ChecksContext.FixturePath("stellaris/launcher-settings.json"));
        ChecksContext.Check("stellaris launcher-settings 解析成功", stellaris is not null);
        if (stellaris is not null)
        {
            ChecksContext.Equal("stellaris gameId", "stellaris", stellaris.GameId);
            ChecksContext.Equal("stellaris 缺 formatVersion → 0", 0.0, stellaris.FormatVersion);
            ChecksContext.Equal("stellaris Format", LoadFileFormat.DlcLoadV0, stellaris.Format);
            var stellarisSettingsDir =
                Path.GetDirectoryName(ChecksContext.FixturePath("stellaris/launcher-settings.json")) ?? string.Empty;
            ChecksContext.Check(
                "stellaris 缺 dlcPath → 默认 ./ 相对 settings 目录",
                string.Equals(
                    Norm(stellaris.DlcPath).TrimEnd('/'),
                    Norm(stellarisSettingsDir).TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase),
                stellaris.DlcPath);
            ChecksContext.Check(
                "stellaris exePath 以 stellaris.exe 结尾",
                Norm(stellaris.ExePath).EndsWith("stellaris/stellaris.exe", StringComparison.OrdinalIgnoreCase),
                stellaris.ExePath);
            ChecksContext.Check(
                "stellaris modDir 以 Stellaris/mod 结尾",
                Norm(stellaris.ModDir).EndsWith("Stellaris/mod", StringComparison.OrdinalIgnoreCase),
                stellaris.ModDir);
            ChecksContext.Equal(
                "stellaris 备选 label 取 zh-hans",
                "跨平台多人联机",
                stellaris.Alternatives.Count > 0 ? stellaris.Alternatives[0].DisplayLabel : "<none>");
        }

        var pa = reader.ReadFile(ChecksContext.FixturePath("prison_architect/launcher-settings.json"));
        ChecksContext.Check("prison_architect launcher-settings 解析成功", pa is not null);
        if (pa is not null)
        {
            ChecksContext.Equal("prison_architect gameId", "prison_architect_pc_console", pa.GameId);
            ChecksContext.Equal("prison_architect formatVersion", 1.0, pa.FormatVersion);
            ChecksContext.Equal("prison_architect Format", LoadFileFormat.ContentLoadV1, pa.Format);
            ChecksContext.Check(
                "prison_architect gameDataDir 展开 %USERPROFILE%",
                Norm(pa.GameDataDir).EndsWith("AppData/Local/Introversion/Prison Architect", StringComparison.OrdinalIgnoreCase),
                pa.GameDataDir);
            ChecksContext.Check(
                "prison_architect exePath",
                Norm(pa.ExePath).EndsWith("Prison Architect64.exe", StringComparison.OrdinalIgnoreCase),
                pa.ExePath);
            ChecksContext.Check("prison_architect 无 displayName 时回退目录名", !string.IsNullOrWhiteSpace(pa.DisplayName), pa.DisplayName);
        }

        // Cities: Skylines 1：读取层必须保持官方原始 id（cities_v2），归一只发生在发现阶段。
        var cs1 = reader.ReadFile(ChecksContext.FixturePath("cities_skylines/launcher-settings.json"));
        ChecksContext.Check("cities_skylines launcher-settings 解析成功", cs1 is not null);
        if (cs1 is not null)
        {
            ChecksContext.Equal("cities_skylines fixture 的原始 gameId 保持 cities_v2", "cities_v2", cs1.GameId);
            ChecksContext.Equal("cities_skylines formatVersion", 1.0, cs1.FormatVersion);
            ChecksContext.Equal("cities_skylines Format", LoadFileFormat.ContentLoadV1, cs1.Format);
            ChecksContext.Equal("cities_skylines displayName", "Cities: Skylines", cs1.DisplayName);
            ChecksContext.Check(
                "cities_skylines exePath 以 Cities.exe 结尾",
                Norm(cs1.ExePath).EndsWith("Cities.exe", StringComparison.OrdinalIgnoreCase),
                cs1.ExePath);
            ChecksContext.Check(
                "cities_skylines gameDataDir 展开 %LOCALAPPDATA%",
                Norm(cs1.GameDataDir).EndsWith("Colossal Order/Cities_Skylines", StringComparison.OrdinalIgnoreCase),
                cs1.GameDataDir);
            // 真机 launcher-settings.json 没有 dlcPath，读取层按 "./" 兜底到 settings 目录。
            var cs1SettingsDir =
                Path.GetDirectoryName(ChecksContext.FixturePath("cities_skylines/launcher-settings.json")) ?? string.Empty;
            ChecksContext.Check(
                "cities_skylines 缺 dlcPath → 默认 ./ 相对 settings 目录",
                string.Equals(
                    Norm(cs1.DlcPath).TrimEnd('/'),
                    Norm(cs1SettingsDir).TrimEnd('/'),
                    StringComparison.OrdinalIgnoreCase),
                cs1.DlcPath);
        }

        // 注意：fixtures 把 launcher-settings.json 直接放在 fixtures/<gameId>/ 下，
        // 是真实 <root>\launcher\ 布局的扁平化表示，所以这里不对 InstallRoot 断言；
        // 三种布局约定（根 / launcher / Launcher）的 InstallRoot 推导由 CheckSyntheticLayout 覆盖。
        ChecksContext.Check("不存在的目录返回 null", reader.Read(Path.Combine(root, "no-such-game-dir")) is null);
        ChecksContext.Check("formatVersion 映射 0", LoadFileFormat.DlcLoadV0 == LauncherSettingsReader.MapFormat(0));
        ChecksContext.Check("formatVersion 映射 1", LoadFileFormat.ContentLoadV1 == LauncherSettingsReader.MapFormat(1));
        ChecksContext.Check("formatVersion 映射 1.1", LoadFileFormat.ContentLoadV1_1 == LauncherSettingsReader.MapFormat(1.1));
    }

    // ------------------------------------------- 合成安装布局（相对路径绝对化）

    /// <summary>
    /// 用临时目录合成一份完整的 CK3 风格安装布局，验证相对路径的基准目录
    /// （launcher-settings.json 所在目录而不是安装根）以及路径不带尾分隔符。
    /// </summary>
    private static void CheckSyntheticLayout(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 合成安装布局（相对路径绝对化）");
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-layout-" + Guid.NewGuid().ToString("N"));
        try
        {
            var installRoot = Path.Combine(tempRoot, "install");
            var launcherDir = Path.Combine(installRoot, "launcher");
            var assetsDir = Path.Combine(launcherDir, "assets");
            var gameDir = Path.Combine(installRoot, "game");
            fs.EnsureDirectory(assetsDir);
            fs.EnsureDirectory(gameDir);

            var settingsPath = Path.Combine(launcherDir, "launcher-settings.json");
            fs.WriteAllTextAtomic(
                settingsPath,
                "{"
                + "\"gameId\":\"ck3\","
                + "\"formatVersion\":1.1,"
                + "\"dlcPath\":\"../game\","
                + "\"themeFile\":\"./assets/theme-settings.json\","
                + "\"exePath\":\"../binaries/ck3.exe\","
                + "\"gameDataPath\":\"%USER_DOCUMENTS%/Paradox Interactive/Crusader Kings III\""
                + "}");

            var themePath = Path.Combine(assetsDir, "theme-settings.json");
            fs.WriteAllTextAtomic(
                themePath,
                fs.ReadAllText(ChecksContext.FixturePath("ck3/theme-settings.json")));

            var reader = new LauncherSettingsReader(fs, logger);
            var found = reader.FindSettingsFile(installRoot);
            ChecksContext.Check(
                "布局: FindSettingsFile 找到 launcher/launcher-settings.json",
                found is not null && string.Equals(Norm(found), Norm(settingsPath), StringComparison.OrdinalIgnoreCase),
                found ?? "<null>");

            var settings = reader.Read(installRoot);
            ChecksContext.Check("布局: Read 解析成功", settings is not null);
            if (settings is null)
            {
                return;
            }

            ChecksContext.Equal("布局: Format", LoadFileFormat.ContentLoadV1_1, settings.Format);
            ChecksContext.Check(
                "布局: InstallRoot = <tmp>/install",
                string.Equals(Norm(settings.InstallRoot), Norm(installRoot), StringComparison.OrdinalIgnoreCase),
                settings.InstallRoot);
            ChecksContext.Check(
                "布局: dlcPath = ../game 相对 settings 目录",
                string.Equals(Norm(settings.DlcPath), Norm(gameDir), StringComparison.OrdinalIgnoreCase),
                settings.DlcPath);
            ChecksContext.Check(
                "布局: dlcPath 不带尾分隔符",
                !settings.DlcPath.EndsWith("\\", StringComparison.Ordinal) && !settings.DlcPath.EndsWith("/", StringComparison.Ordinal),
                settings.DlcPath);
            ChecksContext.Check(
                "布局: themeFile 绝对化到 launcher/assets",
                settings.ThemeFile is not null && string.Equals(Norm(settings.ThemeFile), Norm(themePath), StringComparison.OrdinalIgnoreCase),
                settings.ThemeFile ?? "<null>");
            ChecksContext.Check(
                "布局: exePath 绝对化到 install/binaries/ck3.exe",
                string.Equals(
                    Norm(settings.ExePath),
                    Norm(Path.Combine(installRoot, "binaries", "ck3.exe")),
                    StringComparison.OrdinalIgnoreCase),
                settings.ExePath);
            ChecksContext.Check(
                "布局: exePath 不带尾分隔符",
                !settings.ExePath.EndsWith("\\", StringComparison.Ordinal),
                settings.ExePath);
            ChecksContext.Check(
                "布局: modDir 无尾分隔符且以 mod 结尾",
                !settings.ModDir.EndsWith("\\", StringComparison.Ordinal)
                    && Norm(settings.ModDir).EndsWith("Crusader Kings III/mod", StringComparison.OrdinalIgnoreCase),
                settings.ModDir);

            var themeReader = new ThemeReader(fs, logger);
            var theme = themeReader.Read(settings.InstallRoot, settings.ThemeFile);
            ChecksContext.Equal("布局: 主题 Source = InstallDir", ThemeAssetSource.InstallDir, theme.Source);
            ChecksContext.Equal("布局: 主题按钮色", "hsla(53, 31%, 85%, 0.9)", theme.ButtonTextColor);

            // 根目录布局（Stellaris 风格）：设置文件就在 <root> 下，InstallRoot 不应再往上跳一层。
            var rootInstall = Path.Combine(tempRoot, "install-root-layout");
            fs.EnsureDirectory(Path.Combine(rootInstall, "assets"));
            fs.WriteAllTextAtomic(
                Path.Combine(rootInstall, "launcher-settings.json"),
                "{"
                + "\"gameId\":\"stellaris\","
                + "\"exePath\":\"./stellaris.exe\","
                + "\"themeFile\":\"./assets/theme-settings.json\""
                + "}");
            fs.WriteAllTextAtomic(
                Path.Combine(rootInstall, "assets", "theme-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("stellaris/theme-settings.json")));

            var rootSettings = reader.Read(rootInstall);
            ChecksContext.Check("布局: 根目录布局解析成功", rootSettings is not null);
            if (rootSettings is not null)
            {
                ChecksContext.Check(
                    "布局: 根目录布局 InstallRoot = 设置文件所在目录",
                    string.Equals(Norm(rootSettings.InstallRoot), Norm(rootInstall), StringComparison.OrdinalIgnoreCase),
                    rootSettings.InstallRoot);
                ChecksContext.Check(
                    "布局: 根目录布局 exePath = <root>/stellaris.exe",
                    string.Equals(
                        Norm(rootSettings.ExePath),
                        Norm(Path.Combine(rootInstall, "stellaris.exe")),
                        StringComparison.OrdinalIgnoreCase),
                    rootSettings.ExePath);
                ChecksContext.Check(
                    "布局: 根目录布局 缺 dlcPath → 默认 ./ 落在 <root>",
                    string.Equals(Norm(rootSettings.DlcPath), Norm(rootInstall), StringComparison.OrdinalIgnoreCase),
                    rootSettings.DlcPath);
                var rootTheme = themeReader.Read(rootSettings.InstallRoot, rootSettings.ThemeFile);
                ChecksContext.Equal("布局: 根目录布局 主题 Source = InstallDir", ThemeAssetSource.InstallDir, rootTheme.Source);
                ChecksContext.Equal("布局: 根目录布局 主题色 #ffffff", "#ffffff", rootTheme.ButtonTextColor);
            }
        }
        finally
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
        }
    }

    // ---------------------------------------------------------------- 主题

    private static void CheckThemeReader(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] ThemeReader");
        var reader = new ThemeReader(fs, logger);

        var ck3 = reader.ReadFile(ChecksContext.FixturePath("ck3/theme-settings.json"));
        ChecksContext.Equal("ck3 主题 Source", ThemeAssetSource.InstallDir, ck3.Source);
        ChecksContext.Equal("ck3 button.color 原样", "hsla(53, 31%, 85%, 0.9)", ck3.ButtonTextColor);
        ChecksContext.Equal("ck3 buttonHover.color 原样", "hsla(53, 36%, 95%, 1)", ck3.ButtonHoverTextColor);
        ChecksContext.Equal("ck3 fontSize", 24.0, ck3.FontSize);
        ChecksContext.Equal("ck3 fontWeight", 200, ck3.FontWeight);
        ChecksContext.Check(
            "ck3 background 绝对化",
            ck3.Background is not null && Norm(ck3.Background).EndsWith("ck3/app-background.png", StringComparison.OrdinalIgnoreCase),
            ck3.Background ?? "<null>");
        ChecksContext.Check(
            "ck3 logo 绝对化",
            ck3.Logo is not null && Norm(ck3.Logo).EndsWith("ck3/logo.svg", StringComparison.OrdinalIgnoreCase),
            ck3.Logo ?? "<null>");
        ChecksContext.Check(
            "ck3 buttonFont 绝对化",
            ck3.ButtonFont is not null && Norm(ck3.ButtonFont).EndsWith("fonts/CormorantGaramond-Regular.ttf", StringComparison.OrdinalIgnoreCase),
            ck3.ButtonFont ?? "<null>");
        ChecksContext.Check("ck3 无 appIcon", ck3.AppIcon is null, ck3.AppIcon ?? "<null>");
        ChecksContext.Check("ck3 无 textShadow", ck3.TextShadow is null, ck3.TextShadow ?? "<null>");

        var stellaris = reader.ReadFile(ChecksContext.FixturePath("stellaris/theme-settings.json"));
        ChecksContext.Equal("stellaris button.color", "#ffffff", stellaris.ButtonTextColor);
        ChecksContext.Equal("stellaris buttonHover.color", "#ffffff", stellaris.ButtonHoverTextColor);
        ChecksContext.Equal("stellaris 缺 fontWeight → 400", 400, stellaris.FontWeight);
        ChecksContext.Equal("stellaris textShadow 原样", "0 2px rgba(0, 0, 0, 0.0)", stellaris.TextShadow);
        ChecksContext.Equal("stellaris Source", ThemeAssetSource.InstallDir, stellaris.Source);

        var v3 = reader.ReadFile(ChecksContext.FixturePath("victoria3/theme-settings.json"));
        ChecksContext.Check(
            "victoria3 appIcon 绝对化",
            v3.AppIcon is not null && Norm(v3.AppIcon).EndsWith("app-icon.png", StringComparison.OrdinalIgnoreCase),
            v3.AppIcon ?? "<null>");
        ChecksContext.Check(
            "victoria3 appTaskbarIcon 绝对化",
            v3.AppTaskbarIcon is not null && Norm(v3.AppTaskbarIcon).EndsWith("app-icon.ico", StringComparison.OrdinalIgnoreCase),
            v3.AppTaskbarIcon ?? "<null>");

        var placeholder = ThemeReader.Placeholder();
        ChecksContext.Equal("占位主题 Source", ThemeAssetSource.BuiltInPlaceholder, placeholder.Source);
        ChecksContext.Equal("占位主题按钮色", ThemeReader.DefaultButtonTextColor, placeholder.ButtonTextColor);

        var missing = reader.Read(Path.Combine(Path.GetTempPath(), "pdxnext-no-theme-" + Guid.NewGuid().ToString("N")));
        ChecksContext.Equal("缺失主题目录 → 占位", ThemeAssetSource.BuiltInPlaceholder, missing.Source);
    }

    // ---------------------------------------------------------------- 颜色

    private static void CheckColors()
    {
        Console.WriteLine("[GamesChecks] HslaColor");

        ChecksContext.Check("hsla 解析成功", HslaColor.TryParse("hsla(53, 31%, 85%, 0.9)", out var hsla), "hsla(53, 31%, 85%, 0.9)");
        ChecksContext.Check(
            "hsla RGB ≈ (229,226,205)",
            Math.Abs(hsla.R - 229) <= 1 && Math.Abs(hsla.G - 226) <= 1 && Math.Abs(hsla.B - 205) <= 1,
            hsla.R + "," + hsla.G + "," + hsla.B);
        ChecksContext.Check("hsla alpha ≈ 0.9", Math.Abs(hsla.A - 0.9) < 1e-9, hsla.A.ToString(System.Globalization.CultureInfo.InvariantCulture));

        ChecksContext.Check("#ffffff 解析成功", HslaColor.TryParse("#ffffff", out var white), "#ffffff");
        ChecksContext.Equal("#ffffff R", (byte)255, white.R);
        ChecksContext.Equal("#ffffff G", (byte)255, white.G);
        ChecksContext.Equal("#ffffff B", (byte)255, white.B);
        ChecksContext.Equal("#ffffff A", 1.0, white.A);

        ChecksContext.Check("#474747 解析成功", HslaColor.TryParse("#474747", out var dark), "#474747");
        ChecksContext.Equal("#474747 R", (byte)71, dark.R);
        ChecksContext.Equal("#474747 A", 1.0, dark.A);

        ChecksContext.Check("hsla alpha=1 解析成功", HslaColor.TryParse("hsla(53, 36%, 95%, 1)", out var opaque) && Math.Abs(opaque.A - 1.0) < 1e-9, "hsla(53, 36%, 95%, 1)");
        ChecksContext.Check("#RGB 简写解析成功", HslaColor.TryParse("#fff", out var shortWhite) && shortWhite.R == 255 && shortWhite.G == 255 && shortWhite.B == 255);
        ChecksContext.Check("textShadow 串不是颜色", !HslaColor.TryParse("0 2px rgba(0, 0, 0, 0.0)", out _), "0 2px rgba(0, 0, 0, 0.0)");
        ChecksContext.Check("null 不抛异常", !HslaColor.TryParse(null, out _));
        ChecksContext.Check("空串不抛异常", !HslaColor.TryParse("   ", out _));
    }

    // ------------------------------------------------- 合成 Steam 库（确定性）

    private static void CheckSyntheticLibrary(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 合成 Steam 库（GameCatalog / AppManifest / SteamLibraryLocator）");
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-games-" + Guid.NewGuid().ToString("N"));
        try
        {
            var steamApps = Path.Combine(tempRoot, "steamapps");
            var common = Path.Combine(steamApps, "common");
            fs.EnsureDirectory(common);

            var ck3Root = Path.Combine(common, "Crusader Kings III");
            var ck3Launcher = Path.Combine(ck3Root, "launcher");
            fs.EnsureDirectory(Path.Combine(ck3Launcher, "assets"));
            fs.WriteAllTextAtomic(
                Path.Combine(ck3Launcher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("ck3/launcher-settings.json")));
            fs.WriteAllTextAtomic(
                Path.Combine(ck3Launcher, "assets", "theme-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("ck3/theme-settings.json")));

            var notPdxRoot = Path.Combine(common, "Totally Not Paradox");
            fs.EnsureDirectory(notPdxRoot);
            fs.WriteAllTextAtomic(
                Path.Combine(notPdxRoot, "launcher-settings.json"),
                "{\"gameId\":\"some_other_game\",\"version\":\"1.0\"}");

            var paRoot = Path.Combine(common, "Prison Architect");
            var paLauncher = Path.Combine(paRoot, "Launcher");
            fs.EnsureDirectory(paLauncher);
            fs.WriteAllTextAtomic(
                Path.Combine(paLauncher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("prison_architect/launcher-settings.json")));

            var noSettingsRoot = Path.Combine(common, "Empty Game Dir");
            fs.EnsureDirectory(noSettingsRoot);

            // Cities: Skylines 1 真机布局：launcher-settings.json 直接放在安装根，gameId 写 cities_v2。
            var cs1Root = Path.Combine(common, "Cities Skylines");
            var cs1Assets = Path.Combine(cs1Root, "LauncherAssets");
            fs.EnsureDirectory(cs1Assets);
            fs.WriteAllTextAtomic(
                Path.Combine(cs1Root, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("cities_skylines/launcher-settings.json")));

            var nl = Environment.NewLine;
            fs.WriteAllTextAtomic(
                Path.Combine(steamApps, "appmanifest_1158310.acf"),
                string.Join(nl, new[]
                {
                    "\"AppState\"",
                    "{",
                    "\t\"appid\"\t\t\"1158310\"",
                    "\t\"name\"\t\t\"Crusader Kings III\"",
                    "\t\"StateFlags\"\t\t\"4\"",
                    "\t\"installdir\"\t\t\"Crusader Kings III\"",
                    "\t\"SizeOnDisk\"\t\t\"123456789\"",
                    "}",
                }));

            var vdfPath = Path.Combine(steamApps, "libraryfolders.vdf");
            fs.WriteAllTextAtomic(
                vdfPath,
                string.Join(nl, new[]
                {
                    "\"libraryfolders\"",
                    "{",
                    "\t\"0\"",
                    "\t{",
                    "\t\t\"path\"\t\t\"" + tempRoot.Replace((char)92, '/') + "\"",
                    "\t}",
                    "}",
                }));

            var locator = new SteamLibraryLocator(fs, logger);
            var libraries = locator.ReadLibraryFolders(vdfPath);
            ChecksContext.Equal("合成库: libraryfolders 解析出 1 个库", 1, libraries.Count);
            ChecksContext.Check(
                "合成库: 路径等于临时根",
                libraries.Count == 1 && string.Equals(Norm(libraries[0]), Norm(tempRoot), StringComparison.OrdinalIgnoreCase),
                libraries.Count == 1 ? libraries[0] : "<none>");

            var manifestReader = new AppManifestReader(fs, logger);
            var manifests = manifestReader.ReadAll(tempRoot);
            ChecksContext.Equal("合成库: appmanifest 数量", 1, manifests.Count);
            if (manifests.Count == 1)
            {
                ChecksContext.Equal("合成库: appid", 1158310, manifests[0].AppId);
                ChecksContext.Equal("合成库: installdir", "Crusader Kings III", manifests[0].InstallDir);
                ChecksContext.Check("合成库: StateFlags 表示已安装", manifests[0].IsInstalled, manifests[0].StateFlags.ToString(System.Globalization.CultureInfo.InvariantCulture));
                ChecksContext.Equal("合成库: SizeOnDisk", 123456789L, manifests[0].SizeOnDisk);
            }

            var settingsReader = new LauncherSettingsReader(fs, logger);
            var themeReader = new ThemeReader(fs, logger);
            var catalog = new GameCatalog(
                fs,
                logger,
                manifestReader,
                settingsReader,
                themeReader,
                new[] { tempRoot });
            var games = catalog.Discover(CancellationToken.None);
            ChecksContext.Equal("合成库: 收录游戏数量（4 个）", 4, games.Count);
            ChecksContext.Equal(
                "合成库: gameId 集合",
                "cities_skylines,ck3,prison_architect_pc_console,some_other_game",
                string.Join(",", games.Select(static g => g.GameId).OrderBy(static id => id, StringComparer.Ordinal)));
            var game = games.FirstOrDefault(static g => g.GameId == "ck3");
            ChecksContext.Check("合成库: 找到 ck3", game is not null);
            if (game is not null)
            {
                ChecksContext.Equal("合成库: gameId", "ck3", game.GameId);
                ChecksContext.Equal("合成库: ck3 Support", GameSupport.Full, game.Support);
                ChecksContext.Equal("合成库: SteamAppId", (int?)1158310, game.SteamAppId);
                ChecksContext.Equal("合成库: Theme.Source", ThemeAssetSource.InstallDir, game.Theme.Source);
                ChecksContext.Equal("合成库: Theme.ButtonTextColor", "hsla(53, 31%, 85%, 0.9)", game.Theme.ButtonTextColor);
                ChecksContext.Equal("合成库: Format", LoadFileFormat.DlcLoadV0, game.Format);
                ChecksContext.Check(
                    "合成库: InstallRoot",
                    string.Equals(Norm(game.InstallRoot), Norm(ck3Root), StringComparison.OrdinalIgnoreCase),
                    game.InstallRoot);
            }

            var paGame = games.FirstOrDefault(static g => g.GameId == "prison_architect_pc_console");
            ChecksContext.Check("合成库: 找到 prison_architect_pc_console", paGame is not null);
            if (paGame is not null)
            {
                ChecksContext.Equal("合成库: PA Support", GameSupport.LaunchOnly, paGame.Support);
                ChecksContext.Equal("合成库: PA IsSupported", true, paGame.IsSupported);
                ChecksContext.Equal("合成库: PA SupportsContentManagement", false, paGame.SupportsContentManagement);
                ChecksContext.Equal("合成库: PA ExeArgs", "--launched-from-launcher", string.Join(" ", paGame.ExeArgs));
            }

            // Cities: Skylines 1：原始 id cities_v2 归一成 cities_skylines，支持等级「仅启动」。
            var cs1Game = games.FirstOrDefault(static g => g.GameId == "cities_skylines");
            ChecksContext.Check("合成库: 找到 cities_skylines（原始 id 已归一）", cs1Game is not null);
            if (cs1Game is not null)
            {
                ChecksContext.Equal("合成库: CS1 Support", GameSupport.LaunchOnly, cs1Game.Support);
                ChecksContext.Equal("合成库: CS1 IsSupported", true, cs1Game.IsSupported);
                ChecksContext.Equal("合成库: CS1 SupportsContentManagement", false, cs1Game.SupportsContentManagement);
                ChecksContext.Equal("合成库: CS1 DisplayName", "Cities: Skylines", cs1Game.DisplayName);
                ChecksContext.Equal("合成库: CS1 Format", LoadFileFormat.ContentLoadV1, cs1Game.Format);
                ChecksContext.Equal("合成库: CS1 无 appmanifest → SteamAppId 为 null", (int?)null, cs1Game.SteamAppId);
                ChecksContext.Check(
                    "合成库: CS1 InstallRoot 就是安装根（没有 launcher 子目录）",
                    string.Equals(Norm(cs1Game.InstallRoot), Norm(cs1Root), StringComparison.OrdinalIgnoreCase),
                    cs1Game.InstallRoot);
            }

            // 未登记的 gameId 也会收录（能显示、能启动），但支持等级是 Unsupported。
            var unknownGame = games.FirstOrDefault(static g => g.GameId == "some_other_game");
            ChecksContext.Check("合成库: 找到 some_other_game", unknownGame is not null);
            if (unknownGame is not null)
            {
                ChecksContext.Equal("合成库: 未知游戏 Support", GameSupport.Unsupported, unknownGame.Support);
                ChecksContext.Equal("合成库: 未知游戏 IsSupported", false, unknownGame.IsSupported);
                ChecksContext.Equal("合成库: 未知游戏 SupportsContentManagement", false, unknownGame.SupportsContentManagement);
            }

            var one = catalog.DiscoverOneAsync("CK3", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("合成库: DiscoverOneAsync 忽略大小写", one is not null && one.GameId == "ck3");
            var cs1ByRawId = catalog.DiscoverOneAsync("cities_v2", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check(
                "合成库: DiscoverOneAsync 用官方原始 id 也能命中",
                cs1ByRawId is not null && cs1ByRawId.GameId == "cities_skylines",
                cs1ByRawId?.GameId ?? "<null>");
            var none = catalog.DiscoverOneAsync("eu5", CancellationToken.None).GetAwaiter().GetResult();
            ChecksContext.Check("合成库: DiscoverOneAsync 未安装返回 null", none is null);
        }
        finally
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
        }
    }

    // ------------------------------------------- 无 launcher-settings.json 的安装（EU5）

    private static void CheckSignatureDetection(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 特征识别（EU5 无 launcher-settings.json）");
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-signature-" + Guid.NewGuid().ToString("N"));
        try
        {
            var steamApps = Path.Combine(tempRoot, "steamapps");
            var common = Path.Combine(steamApps, "common");
            fs.EnsureDirectory(common);

            // EU5 真实布局：只有 binaries/game/clausewitz/jomini，没有任何元数据文件。
            var eu5Root = Path.Combine(common, "Europa Universalis V");
            fs.EnsureDirectory(Path.Combine(eu5Root, "binaries"));
            fs.EnsureDirectory(Path.Combine(eu5Root, "game"));
            fs.EnsureDirectory(Path.Combine(eu5Root, "clausewitz"));
            fs.EnsureDirectory(Path.Combine(eu5Root, "jomini"));
            fs.WriteAllBytesAtomic(
                Path.Combine(eu5Root, "binaries", "eu5.exe"),
                new byte[] { 0x4D, 0x5A });

            // 干扰项：别的厂商的游戏（有自己的 exe，但不是已知 Paradox 布局）必须仍然被跳过。
            var otherRoot = Path.Combine(common, "Some Other Game");
            fs.EnsureDirectory(Path.Combine(otherRoot, "bin"));
            fs.WriteAllBytesAtomic(
                Path.Combine(otherRoot, "bin", "game.exe"),
                new byte[] { 0x4D, 0x5A });

            var nl = Environment.NewLine;
            fs.WriteAllTextAtomic(
                Path.Combine(steamApps, "appmanifest_3450310.acf"),
                string.Join(nl, new[]
                {
                    "\"AppState\"",
                    "{",
                    "\t\"appid\"\t\t\"3450310\"",
                    "\t\"name\"\t\t\"Europa Universalis V\"",
                    "\t\"StateFlags\"\t\t\"4\"",
                    "\t\"installdir\"\t\t\"Europa Universalis V\"",
                    "}",
                }));

            var catalog = new GameCatalog(
                fs,
                logger,
                new AppManifestReader(fs, logger),
                new LauncherSettingsReader(fs, logger),
                new ThemeReader(fs, logger),
                new[] { tempRoot });

            var games = catalog.Discover(CancellationToken.None);
            ChecksContext.Equal("特征识别: 只收录 EU5（干扰项被跳过）", 1, games.Count);
            ChecksContext.Equal(
                "特征识别: gameId",
                "eu5",
                string.Join(",", games.Select(static g => g.GameId)));

            var eu5 = games.FirstOrDefault(static g => g.GameId == "eu5");
            ChecksContext.Check("特征识别: 找到 eu5", eu5 is not null);
            if (eu5 is not null)
            {
                ChecksContext.Equal("特征识别: DisplayName", "Europa Universalis V", eu5.DisplayName);
                ChecksContext.Equal("特征识别: Support", GameSupport.Full, eu5.Support);
                ChecksContext.Equal("特征识别: IsSupported", true, eu5.IsSupported);
                ChecksContext.Equal("特征识别: 支持内容管理", true, eu5.SupportsContentManagement);
                ChecksContext.Equal("特征识别: ExePath", "binaries/eu5.exe", eu5.ExePath);
                ChecksContext.Equal("特征识别: SteamAppId", (int?)3450310, eu5.SteamAppId);
                ChecksContext.Equal("特征识别: 版本未知（空串）", string.Empty, eu5.Version);
                ChecksContext.Equal(
                    "特征识别: DLC 路径推导为 game/dlc",
                    Norm(Path.Combine(eu5Root, "game", "dlc")),
                    Norm(eu5.DlcPath));
                ChecksContext.Check(
                    "特征识别: mod 目录推导到文档目录",
                    Norm(eu5.ModDir).EndsWith(
                        "Paradox Interactive/Europa Universalis V/mod",
                        StringComparison.OrdinalIgnoreCase),
                    eu5.ModDir);
                ChecksContext.Equal("特征识别: 无 launcher-settings 路径", string.Empty, eu5.LauncherSettingsPath);
                ChecksContext.Equal("特征识别: 存档格式为 playsets.json", LoadFileFormat.PlaysetsJsonV1, eu5.Format);
                ChecksContext.Equal("特征识别: 不写加载文件", false, eu5.WritesLoadFile);
                ChecksContext.Check(
                    "特征识别: InstallRoot",
                    string.Equals(Norm(eu5.InstallRoot), Norm(eu5Root), StringComparison.OrdinalIgnoreCase),
                    eu5.InstallRoot);

                // 启动链路：相对 exe 必须能拼成绝对路径，否则点「启动」会报找不到可执行文件。
                var launcher = new PdxLauncher.Core.Launching.GameLauncher(
                    new NullProcessLauncher(),
                    fs,
                    logger);
                ChecksContext.Check(
                    "特征识别: 可执行文件路径可解析",
                    string.Equals(
                        Norm(launcher.ResolveExecutablePath(eu5, request: null)),
                        Norm(Path.Combine(eu5Root, "binaries", "eu5.exe")),
                        StringComparison.OrdinalIgnoreCase),
                    launcher.ResolveExecutablePath(eu5, request: null));
                ChecksContext.Equal("特征识别: 默认无启动参数", 0, launcher.BuildArguments(eu5, request: null).Count);
            }
        }
        finally
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
        }
    }

    /// <summary>只用于拼装启动命令的替身：真正启动由 LoadFileChecks 的 RecordingProcessLauncher 覆盖。</summary>
    private sealed class NullProcessLauncher : IProcessLauncher
    {
        public Task<ProcessStartResult> StartDetachedAsync(
            string exePath,
            IReadOnlyList<string> args,
            string workingDirectory,
            CancellationToken cancellationToken)
            => Task.FromResult(ProcessStartResult.Failure("自检不真正启动进程"));
    }

    // ------------------------------------------- 官方启动器独立库（无 Steam）

    private static void CheckStandaloneLibrary(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 官方启动器独立库（GameCatalog 双来源）");
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-standalone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var steamLibrary = Path.Combine(tempRoot, "steam-lib");
            var standaloneRoot = Path.Combine(tempRoot, "pdx-games");
            var selfGameRoot = Path.Combine(tempRoot, "self-game");

            // Steam 库里的 CK3（有 appmanifest，SteamAppId 必须读得出来）
            var steamCk3 = Path.Combine(steamLibrary, "steamapps", "common", "Crusader Kings III");
            var steamCk3Launcher = Path.Combine(steamCk3, "launcher");
            fs.EnsureDirectory(steamCk3Launcher);
            fs.WriteAllTextAtomic(
                Path.Combine(steamCk3Launcher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("ck3/launcher-settings.json")));

            var nl = Environment.NewLine;
            fs.WriteAllTextAtomic(
                Path.Combine(steamLibrary, "steamapps", "appmanifest_1158310.acf"),
                string.Join(nl, new[]
                {
                    "\"AppState\"",
                    "{",
                    "\t\"appid\"\t\t\"1158310\"",
                    "\t\"installdir\"\t\t\"Crusader Kings III\"",
                    "\t\"StateFlags\"\t\t\"4\"",
                    "}",
                }));

            // 独立库布局：<库根>\<游戏目录>\launcher\launcher-settings.json（无 appmanifest）
            var standaloneStellaris = Path.Combine(standaloneRoot, "Stellaris");
            var standaloneStellarisLauncher = Path.Combine(standaloneStellaris, "launcher");
            fs.EnsureDirectory(standaloneStellarisLauncher);
            fs.WriteAllTextAtomic(
                Path.Combine(standaloneStellarisLauncher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("stellaris/launcher-settings.json")));

            // 独立库里与 Steam 重复的 gameId（ck3）：必须保留 Steam 那份
            var standaloneCk3 = Path.Combine(standaloneRoot, "Crusader Kings III");
            var standaloneCk3Launcher = Path.Combine(standaloneCk3, "launcher");
            fs.EnsureDirectory(standaloneCk3Launcher);
            fs.WriteAllTextAtomic(
                Path.Combine(standaloneCk3Launcher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("ck3/launcher-settings.json")));

            // 库根本身就是一个游戏安装目录（配置的对象条目直接指向它）
            var selfVictoria3Launcher = Path.Combine(selfGameRoot, "launcher");
            fs.EnsureDirectory(selfVictoria3Launcher);
            fs.WriteAllTextAtomic(
                Path.Combine(selfVictoria3Launcher, "launcher-settings.json"),
                fs.ReadAllText(ChecksContext.FixturePath("victoria3/launcher-settings.json")));

            var manifestReader = new AppManifestReader(fs, logger);
            var settingsReader = new LauncherSettingsReader(fs, logger);
            var themeReader = new ThemeReader(fs, logger);
            var catalog = new GameCatalog(
                fs,
                logger,
                manifestReader,
                settingsReader,
                themeReader,
                new[] { steamLibrary },
                new[] { standaloneRoot, selfGameRoot, Path.Combine(tempRoot, "does-not-exist") });

            var games = catalog.Discover(CancellationToken.None);
            ChecksContext.Equal("独立库: 双来源共发现 3 个", 3, games.Count);
            ChecksContext.Equal(
                "独立库: gameId 排序",
                "ck3,stellaris,victoria3",
                string.Join(",", games.Select(static g => g.GameId)));
            ChecksContext.Equal("独立库: 重复 gameId 只保留一个", 1, games.Count(static g => g.GameId == "ck3"));

            var ck3 = games.FirstOrDefault(static g => g.GameId == "ck3");
            ChecksContext.Check("独立库: 找到 ck3", ck3 is not null);
            if (ck3 is not null)
            {
                ChecksContext.Equal("独立库: 重复时保留 Steam 的 SteamAppId", (int?)1158310, ck3.SteamAppId);
                ChecksContext.Check(
                    "独立库: 重复时保留 Steam 的安装根",
                    string.Equals(Norm(ck3.InstallRoot), Norm(steamCk3), StringComparison.OrdinalIgnoreCase),
                    ck3.InstallRoot);
            }

            var stellaris = games.FirstOrDefault(static g => g.GameId == "stellaris");
            ChecksContext.Check("独立库: 找到仅独立安装的 stellaris", stellaris is not null);
            if (stellaris is not null)
            {
                ChecksContext.Equal("独立库: 独立安装的 SteamAppId 为 null", (int?)null, stellaris.SteamAppId);
                ChecksContext.Equal("独立库: 独立安装的 Support", GameSupport.Full, stellaris.Support);
                ChecksContext.Check(
                    "独立库: stellaris 安装根正确",
                    string.Equals(Norm(stellaris.InstallRoot), Norm(standaloneStellaris), StringComparison.OrdinalIgnoreCase),
                    stellaris.InstallRoot);
            }

            var victoria3 = games.FirstOrDefault(static g => g.GameId == "victoria3");
            ChecksContext.Check("独立库: 库根本身作为游戏目录被发现", victoria3 is not null);
            if (victoria3 is not null)
            {
                ChecksContext.Equal("独立库: 库根游戏的 SteamAppId 为 null", (int?)null, victoria3.SteamAppId);
                ChecksContext.Check(
                    "独立库: 库根游戏的 InstallRoot 等于库根",
                    string.Equals(Norm(victoria3.InstallRoot), Norm(selfGameRoot), StringComparison.OrdinalIgnoreCase),
                    victoria3.InstallRoot);
            }
        }
        finally
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
        }
    }

    // ------------------------------------ 官方启动器 gameLibraryPaths（定位器）

    private static void CheckLauncherLibraryPaths(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 官方启动器 gameLibraryPaths（ParadoxLauncherLibraryLocator）");
        var tempRoot = Path.Combine(Path.GetTempPath(), "pdxnext-launcherlib-" + Guid.NewGuid().ToString("N"));
        try
        {
            var launcherRoot = Path.Combine(tempRoot, "Programs", "Paradox Interactive", "launcher");
            var userSettingsPath = Path.Combine(tempRoot, "appdata", "Paradox Interactive", "launcher-v2", "userSettings.json");
            // 相对路径按官方启动器目录解析：launcher\..\lib-relative = Programs\Paradox Interactive\lib-relative
            var relativeLibrary = Path.Combine(tempRoot, "Programs", "Paradox Interactive", "lib-relative");
            var absoluteLibrary = Path.Combine(tempRoot, "lib-absolute");
            var objectLibrary = Path.Combine(tempRoot, "lib-object");
            var defaultGamesRoot = Path.Combine(tempRoot, "Programs", "Paradox Interactive", "games");
            var missingLibrary = Path.Combine(tempRoot, "lib-missing");

            foreach (var dir in new[] { launcherRoot, relativeLibrary, absoluteLibrary, objectLibrary, defaultGamesRoot })
            {
                fs.EnsureDirectory(dir);
            }

            fs.WriteAllTextAtomic(
                userSettingsPath,
                "{\"gameLibraryPaths\":[\"../lib-relative\",\""
                + absoluteLibrary.Replace('\\', '/')
                + "\",{\"installationPath\":\""
                + objectLibrary.Replace('\\', '/')
                + "\"},42,{\"foo\":\"bar\"},\"../games\",\"../GAMES\",\""
                + missingLibrary.Replace('\\', '/')
                + "\"]}");

            var locator = new ParadoxLauncherLibraryLocator(fs, logger, userSettingsPath, launcherRoot);
            ChecksContext.Equal("库路径: DefaultGamesRoot", Norm(defaultGamesRoot), Norm(locator.DefaultGamesRoot));

            var libraries = locator.FindLibraries();
            ChecksContext.Equal("库路径: 去重且只留存在的目录（4 个）", 4, libraries.Count);
            ChecksContext.Check(
                "库路径: 相对路径按启动器目录解析",
                libraries.Count > 0 && string.Equals(Norm(libraries[0]), Norm(relativeLibrary), StringComparison.OrdinalIgnoreCase),
                libraries.Count > 0 ? libraries[0] : "<none>");
            ChecksContext.Check(
                "库路径: 绝对路径条目",
                libraries.Count > 1 && string.Equals(Norm(libraries[1]), Norm(absoluteLibrary), StringComparison.OrdinalIgnoreCase),
                libraries.Count > 1 ? libraries[1] : "<none>");
            ChecksContext.Check(
                "库路径: 对象条目取 installationPath",
                libraries.Count > 2 && string.Equals(Norm(libraries[2]), Norm(objectLibrary), StringComparison.OrdinalIgnoreCase),
                libraries.Count > 2 ? libraries[2] : "<none>");
            ChecksContext.Check(
                "库路径: 默认 ../games 与配置去重后只出现一次",
                libraries.Count(g => string.Equals(Norm(g), Norm(defaultGamesRoot), StringComparison.OrdinalIgnoreCase)) == 1,
                string.Join(" | ", libraries));

            fs.WriteAllTextAtomic(userSettingsPath, "{\"gameLibraryPaths\":[\"../lib-relative\",],}");
            var trailingComma = new ParadoxLauncherLibraryLocator(fs, logger, userSettingsPath, launcherRoot).FindLibraries();
            ChecksContext.Equal("库路径: 允许尾随逗号", 2, trailingComma.Count);

            fs.WriteAllTextAtomic(userSettingsPath, "{ \"gameLibraryPaths\": [ }");
            var corrupt = new ParadoxLauncherLibraryLocator(fs, logger, userSettingsPath, launcherRoot).FindLibraries();
            ChecksContext.Equal("库路径: 配置损坏时只回退默认库根", 1, corrupt.Count);

            fs.WriteAllTextAtomic(userSettingsPath, "[1,2,3]");
            var notObject = new ParadoxLauncherLibraryLocator(fs, logger, userSettingsPath, launcherRoot).FindLibraries();
            ChecksContext.Equal("库路径: 根节点不是对象时只回退默认库根", 1, notObject.Count);

            fs.WriteAllTextAtomic(userSettingsPath, "{\"language\":\"en\"}");
            var noArray = new ParadoxLauncherLibraryLocator(fs, logger, userSettingsPath, launcherRoot).FindLibraries();
            ChecksContext.Equal("库路径: 没有 gameLibraryPaths 时只回退默认库根", 1, noArray.Count);

            var notInstalledRoot = Path.Combine(tempRoot, "not-installed", "launcher");
            var noConfig = new ParadoxLauncherLibraryLocator(
                fs,
                logger,
                Path.Combine(tempRoot, "nope", "userSettings.json"),
                notInstalledRoot).FindLibraries();
            ChecksContext.Equal("库路径: 配置与默认库都不存在时返回 0", 0, noConfig.Count);
        }
        finally
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
        }
    }

    // ------------------------------------------------------- 支持等级（纯逻辑）

    private static void CheckSupportTiers()
    {
        Console.WriteLine("[GamesChecks] 支持等级（KnownGames.GetSupport）");

        ChecksContext.Equal("支持等级: ck3", GameSupport.Full, KnownGames.GetSupport("ck3"));
        ChecksContext.Equal("支持等级: CK3 忽略大小写", GameSupport.Full, KnownGames.GetSupport("CK3"));
        ChecksContext.Equal("支持等级: prison_architect_pc_console", GameSupport.LaunchOnly, KnownGames.GetSupport("prison_architect_pc_console"));
        ChecksContext.Equal("支持等级: 未知 gameId", GameSupport.Unsupported, KnownGames.GetSupport("some_other_game"));
        ChecksContext.Equal("支持等级: null", GameSupport.Unsupported, KnownGames.GetSupport(null));
        ChecksContext.Equal("支持等级: 空串", GameSupport.Unsupported, KnownGames.GetSupport("   "));
        ChecksContext.Equal("支持等级: IsFullySupported(ck3)", true, KnownGames.IsFullySupported("ck3"));
        ChecksContext.Equal("支持等级: IsFullySupported(PA)", false, KnownGames.IsFullySupported("prison_architect_pc_console"));
        ChecksContext.Equal("支持等级: IsKnown(PA)", true, KnownGames.IsKnown("prison_architect_pc_console"));
        ChecksContext.Equal("支持等级: 官方 id prison_architect 仍是 Unsupported", GameSupport.Unsupported, KnownGames.GetSupport("prison_architect"));
        ChecksContext.Equal("支持等级: ck2 仍是 Unsupported", GameSupport.Unsupported, KnownGames.GetSupport("ck2"));

        // Cities: Skylines 1：官方启动器写 cities_v2，本启动器归一到 cities_skylines 后是「仅启动」。
        ChecksContext.Equal("支持等级: cities_skylines", GameSupport.LaunchOnly, KnownGames.GetSupport("cities_skylines"));
        ChecksContext.Equal("支持等级: 官方 id cities_v2 归一后同答案", GameSupport.LaunchOnly, KnownGames.GetSupport("cities_v2"));
        ChecksContext.Equal("支持等级: CS1 大小写不敏感", GameSupport.LaunchOnly, KnownGames.GetSupport("Cities_V2"));
        ChecksContext.Equal("支持等级: IsFullySupported(CS1)", false, KnownGames.IsFullySupported("cities_skylines"));
        ChecksContext.Equal("支持等级: IsKnown(cities_v2)", true, KnownGames.IsKnown("cities_v2"));

        // 身份归一：只折别名，不折未登记的 id，也不改支持等级的来源。
        ChecksContext.Equal("归一: cities_v2 → cities_skylines", "cities_skylines", KnownGames.CanonicalizeGameId("cities_v2"));
        ChecksContext.Equal("归一: 正名原样返回", "cities_skylines", KnownGames.CanonicalizeGameId("cities_skylines"));
        ChecksContext.Equal("归一: 大小写不敏感", "cities_skylines", KnownGames.CanonicalizeGameId("CITIES_V2"));
        ChecksContext.Equal("归一: 未登记 id 原样返回", "ck3", KnownGames.CanonicalizeGameId("ck3"));
        ChecksContext.Equal("归一: null → 空串", string.Empty, KnownGames.CanonicalizeGameId(null));
        ChecksContext.Equal("归一: 空白 → 空串", string.Empty, KnownGames.CanonicalizeGameId("   "));
        ChecksContext.Equal("归一: PA 别名仍不是支持等级来源", GameSupport.Unsupported, KnownGames.GetSupport("prison_architect"));

        // 公告接口的反向特例：只有 CS1 要发原始 id，其余原样。
        ChecksContext.Equal("公告 id: CS1 用原始 id", "cities_v2", KnownGames.GetNewsFeedGameId("cities_skylines"));
        ChecksContext.Equal("公告 id: 已是原始 id 时保持", "cities_v2", KnownGames.GetNewsFeedGameId("cities_v2"));
        ChecksContext.Equal("公告 id: 其它游戏原样", "ck3", KnownGames.GetNewsFeedGameId("ck3"));
        ChecksContext.Equal("公告 id: null → 空串", string.Empty, KnownGames.GetNewsFeedGameId(null));

        ChecksContext.Equal("展示名: 别名 infinite", "Star Trek: Infinite", KnownGames.GetDisplayName("infinite"));
        ChecksContext.Equal("展示名: 别名 prison_architect", "Prison Architect", KnownGames.GetDisplayName("prison_architect"));
        ChecksContext.Equal("展示名: 未登记 gameId 返回 null", (string?)null, KnownGames.GetDisplayName("some_other_game"));
        ChecksContext.Equal("展示名: IsKnown(别名) 只表示登记过", true, KnownGames.IsKnown("tyranny"));
    }

    // ---------------------------------------------------------------- 真机

    private static void CheckRealMachine(IFileSystem fs, IAppLogger logger)
    {
        Console.WriteLine("[GamesChecks] 真机 Steam 库与游戏发现");

        var locator = new SteamLibraryLocator(fs, logger);
        var steamRoot = locator.FindSteamRoot();
        Console.WriteLine("  info Steam 安装目录: " + (steamRoot ?? "<未找到>"));

        var libraries = locator.FindLibraries();
        foreach (var library in libraries)
        {
            Console.WriteLine("  info 库: " + library);
        }

        if (libraries.Count == 0)
        {
            Console.WriteLine("  skip 本机未发现 Steam 库，跳过真机断言（不记为失败）");
            return;
        }

        ChecksContext.Equal("真机: Steam 库数量", 3, libraries.Count);

        var standaloneLibraries = new ParadoxLauncherLibraryLocator(fs, logger).FindLibraries();
        foreach (var standaloneLibrary in standaloneLibraries)
        {
            Console.WriteLine("  info 官方启动器库: " + standaloneLibrary);
        }

        var catalog = new GameCatalog(fs, logger);
        var games = catalog.Discover(CancellationToken.None);
        foreach (var game in games)
        {
            Console.WriteLine(
                "  info " + game.GameId
                + " | " + game.DisplayName
                + " | formatVersion=" + (game.Format == LoadFileFormat.DlcLoadV0 ? "0" : game.Format == LoadFileFormat.ContentLoadV1 ? "1" : "1.1")
                + " | support=" + game.Support
                + " | appId=" + (game.SteamAppId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "<null>")
                + " | root=" + game.InstallRoot
                + " | dlcPath=" + game.DlcPath
                + " | exePath=" + game.ExePath
                + " | theme=" + game.Theme.Source);
        }

        var ids = games.Select(g => g.GameId).OrderBy(id => id, StringComparer.Ordinal).ToArray();
        // 本机游戏会随用户在 Steam 装上/首次运行新游戏而变化（官方启动器会在游戏目录写
        // launcher-settings.json），所以这里只断言「已知这几款必须在」而不是锁死总数。
        ChecksContext.Check("真机: 至少发现 5 个游戏", games.Count >= 5, games.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
        var requiredIds = new[] { "ck3", "hoi4", "prison_architect_pc_console", "stellaris", "victoria3", "cities_skylines" };
        var missing = requiredIds.Where(id => !ids.Contains(id, StringComparer.Ordinal)).ToArray();
        ChecksContext.Check(
            "真机: 必需 gameId 都在",
            missing.Length == 0,
            string.Join(",", ids) + " / 缺少 " + string.Join(",", missing));
        // EU5 不写 launcher-settings.json，靠「已知布局 + 主程序名」特征识别（Level 2）。
        ChecksContext.Check("真机: 已识别 EU5（无 launcher-settings.json）", ids.Contains("eu5"), string.Join(",", ids));
        var eu5 = games.FirstOrDefault(g => g.GameId == "eu5");
        ChecksContext.Check("真机: 找到 eu5", eu5 is not null);
        if (eu5 is not null)
        {
            ChecksContext.Equal("真机: eu5 Support", GameSupport.Full, eu5.Support);
            ChecksContext.Equal("真机: eu5 支持内容管理", true, eu5.SupportsContentManagement);
            ChecksContext.Equal("真机: eu5 存档格式为 playsets.json", LoadFileFormat.PlaysetsJsonV1, eu5.Format);
            ChecksContext.Equal("真机: eu5 不写加载文件", false, eu5.WritesLoadFile);
            ChecksContext.Equal("真机: eu5 无 launcher-settings", string.Empty, eu5.LauncherSettingsPath);
            ChecksContext.Check(
                "真机: eu5 exePath 以 binaries/eu5.exe 结尾",
                Norm(eu5.ExePath).EndsWith("binaries/eu5.exe", StringComparison.OrdinalIgnoreCase),
                eu5.ExePath);
            ChecksContext.Equal("真机: eu5 SteamAppId", (int?)3450310, eu5.SteamAppId);
        }

        foreach (var game in games)
        {
            if (string.Equals(game.GameId, "eu5", StringComparison.Ordinal))
            {
                continue;
            }

            ChecksContext.Check(
                "真机: " + game.GameId + " 的 launcher-settings.json 存在",
                !string.IsNullOrWhiteSpace(game.LauncherSettingsPath) && fs.FileExists(game.LauncherSettingsPath),
                game.LauncherSettingsPath);
        }

        var ck3 = games.FirstOrDefault(g => g.GameId == "ck3");
        ChecksContext.Check("真机: 发现 ck3", ck3 is not null);
        if (ck3 is not null)
        {
            ChecksContext.Equal("真机: ck3 Support", GameSupport.Full, ck3.Support);
            ChecksContext.Equal("真机: ck3 Format", LoadFileFormat.DlcLoadV0, ck3.Format);
            ChecksContext.Check(
                "真机: ck3 dlcPath 以 Crusader Kings III/game 结尾",
                Norm(ck3.DlcPath).EndsWith("Crusader Kings III/game", StringComparison.OrdinalIgnoreCase),
                ck3.DlcPath);
            ChecksContext.Check(
                "真机: ck3 exePath 以 ck3.exe 结尾",
                Norm(ck3.ExePath).EndsWith("ck3.exe", StringComparison.OrdinalIgnoreCase),
                ck3.ExePath);
            ChecksContext.Equal("真机: ck3 SteamAppId", (int?)1158310, ck3.SteamAppId);
            ChecksContext.Equal("真机: ck3 主题来自安装目录", ThemeAssetSource.InstallDir, ck3.Theme.Source);
            ChecksContext.Check(
                "真机: ck3 主题色是 hsla",
                ck3.Theme.ButtonTextColor.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase),
                ck3.Theme.ButtonTextColor);
            ChecksContext.Check(
                "真机: ck3 modDir 以 Crusader Kings III/mod 结尾",
                Norm(ck3.ModDir).EndsWith("Crusader Kings III/mod", StringComparison.OrdinalIgnoreCase),
                ck3.ModDir);
        }

        var v3 = games.FirstOrDefault(g => g.GameId == "victoria3");
        ChecksContext.Check("真机: 发现 victoria3", v3 is not null);
        if (v3 is not null)
        {
            ChecksContext.Equal("真机: victoria3 Support", GameSupport.Full, v3.Support);
            ChecksContext.Equal("真机: victoria3 Format", LoadFileFormat.ContentLoadV1_1, v3.Format);
            ChecksContext.Check(
                "真机: victoria3 dlcPath 以 Victoria 3/game 结尾",
                Norm(v3.DlcPath).EndsWith("Victoria 3/game", StringComparison.OrdinalIgnoreCase),
                v3.DlcPath);
            ChecksContext.Equal("真机: victoria3 SteamAppId", (int?)529340, v3.SteamAppId);
            ChecksContext.Check(
                "真机: victoria3 主题色是 hsla",
                v3.Theme.ButtonTextColor.StartsWith("hsla(", StringComparison.OrdinalIgnoreCase),
                v3.Theme.ButtonTextColor);
        }

        var stellaris = games.FirstOrDefault(g => g.GameId == "stellaris");
        ChecksContext.Check("真机: 发现 stellaris", stellaris is not null);
        if (stellaris is not null)
        {
            ChecksContext.Equal("真机: stellaris Support", GameSupport.Full, stellaris.Support);
            ChecksContext.Equal("真机: stellaris Format", LoadFileFormat.DlcLoadV0, stellaris.Format);
            ChecksContext.Check(
                "真机: stellaris dlcPath 等于安装根（无 dlcPath 键）",
                string.Equals(Norm(stellaris.DlcPath), Norm(stellaris.InstallRoot), StringComparison.OrdinalIgnoreCase),
                stellaris.DlcPath);
            ChecksContext.Check(
                "真机: stellaris 残留坏副本未被收录（只有 1 个 stellaris）",
                games.Count(g => g.GameId == "stellaris") == 1,
                games.Count(g => g.GameId == "stellaris").ToString(System.Globalization.CultureInfo.InvariantCulture));
            ChecksContext.Equal("真机: stellaris SteamAppId", (int?)281990, stellaris.SteamAppId);
            ChecksContext.Equal("真机: stellaris 主题色 #ffffff", "#ffffff", stellaris.Theme.ButtonTextColor);
        }

        var pa = games.FirstOrDefault(g => g.GameId == "prison_architect_pc_console");
        ChecksContext.Check("真机: 发现 prison_architect_pc_console", pa is not null);
        if (pa is not null)
        {
            ChecksContext.Equal("真机: PA Support=LaunchOnly", GameSupport.LaunchOnly, pa.Support);
            ChecksContext.Equal("真机: PA IsSupported=true", true, pa.IsSupported);
            ChecksContext.Equal("真机: PA SupportsContentManagement=false", false, pa.SupportsContentManagement);
            ChecksContext.Equal("真机: PA Format", LoadFileFormat.ContentLoadV1, pa.Format);
            ChecksContext.Check(
                "真机: PA 主题来自 Launcher/Launcher-assets",
                pa.Theme.Source == ThemeAssetSource.InstallDir
                    && pa.Theme.AppIcon is not null
                    && Norm(pa.Theme.AppIcon).EndsWith("Launcher-assets/icon.ico", StringComparison.OrdinalIgnoreCase),
                pa.Theme.AppIcon ?? "<null>");
            ChecksContext.Equal("真机: PA 主题色 #474747", "#474747", pa.Theme.ButtonTextColor);
            ChecksContext.Equal("真机: PA SteamAppId", (int?)233450, pa.SteamAppId);
        }

        // Cities: Skylines 1：真机 launcher-settings.json 写的是 cities_v2，归一后必须是「仅启动」，
        // 且 SteamAppId 仍然来自 appmanifest_255710.acf。
        var cs1 = games.FirstOrDefault(g => g.GameId == "cities_skylines");
        ChecksContext.Check("真机: 发现 cities_skylines（原始 id cities_v2 已归一）", cs1 is not null);
        if (cs1 is not null)
        {
            ChecksContext.Equal("真机: CS1 Support=LaunchOnly", GameSupport.LaunchOnly, cs1.Support);
            ChecksContext.Equal("真机: CS1 IsSupported=true", true, cs1.IsSupported);
            ChecksContext.Equal("真机: CS1 SupportsContentManagement=false", false, cs1.SupportsContentManagement);
            ChecksContext.Equal("真机: CS1 Format", LoadFileFormat.ContentLoadV1, cs1.Format);
            ChecksContext.Equal("真机: CS1 SteamAppId", (int?)255710, cs1.SteamAppId);
            ChecksContext.Check(
                "真机: CS1 exePath 以 Cities.exe 结尾",
                Norm(cs1.ExePath).EndsWith("Cities.exe", StringComparison.OrdinalIgnoreCase),
                cs1.ExePath);
            ChecksContext.Check(
                "真机: CS1 主题来自 LauncherAssets",
                cs1.Theme.Source == ThemeAssetSource.InstallDir,
                cs1.Theme.Source.ToString());
        }

        // 补丁链路：CS1 的 appId 不靠安装目录 appmanifest 也能查出来（独立安装同样能打补丁）。
        ChecksContext.Equal("真机: CS1 appId 映射（补丁用）", true, GameSteamAppIds.TryGet("cities_skylines", out var cs1AppId));
        ChecksContext.Equal("真机: CS1 appId 映射值", 255710, cs1AppId);
    }

    // ---------------------------------------------------------------- 工具

    private static string Get(IReadOnlyDictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var value) ? value : "<missing>";

    private static string Norm(string path) =>
        string.IsNullOrEmpty(path) ? string.Empty : path.Replace(Path.DirectorySeparatorChar, '/');

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
