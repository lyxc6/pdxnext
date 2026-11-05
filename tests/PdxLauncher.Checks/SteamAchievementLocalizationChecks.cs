namespace PdxLauncher.Checks;

using System.Reflection;
using System.Text;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 成就本地化自检。这里钉住四件事：
/// <list type="number">
/// <item>yml 解析：<c>ACHIEVEMENT_&lt;api&gt;</c> / <c>ACHIEVEMENT_DESC_&lt;api&gt;</c> 能命中，标记被剥干净。</item>
/// <item>语言回退链：<c>pdx_settings</c> → 界面语言 → <c>english</c>；本地化缺语言目录时退回英文。</item>
/// <item>映射：本地化优先，其次 Steam 显示名，API 名永远不露给用户。</item>
/// <item>globalPercent 的 CallResult 绑定：703 常量、1110 结构体尺寸、GetAPICallResult 确实绑定了。</item>
/// </list>
/// 全部用临时目录自造的 yml，不读真实游戏目录。
/// </summary>
public static class SteamAchievementLocalizationChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = Path.Combine(Path.GetTempPath(), "pdxnext-ach-loc-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(root);
            CheckLanguageResolution(fs, root);
            CheckLoadAndMarkup(fs, root);
            CheckMappingWithLocalization(fs, root);
            CheckCallResultBinding();
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
                // 临时目录清不掉不影响断言结果。
            }
        }

        return ChecksContext.Failures - before;
    }

    private static void CheckLanguageResolution(IFileSystem fs, string root)
    {
        var logger = new SilentLogger();

        // CK3 的 pdx_settings.txt：块里塞 value="l_simp_chinese"。
        var txtDir = Path.Combine(root, "txt-settings");
        Directory.CreateDirectory(txtDir);
        File.WriteAllText(
            Path.Combine(txtDir, "pdx_settings.txt"),
            "\"language\"={\n\tversion=0\n\tvalue=\"l_simp_chinese\"\n}\n",
            new UTF8Encoding(false));
        ChecksContext.Equal(
            "成就本地化: pdx_settings.txt 的 l_ 前缀被归一化",
            "simp_chinese",
            SteamAchievementLocalization.ResolveLanguage(fs, txtDir, "en-US"));

        // VIC3 的 pdx_settings.json：System.language。
        var jsonDir = Path.Combine(root, "json-settings");
        Directory.CreateDirectory(jsonDir);
        File.WriteAllText(
            Path.Combine(jsonDir, "pdx_settings.json"),
            "{ \"System\": { \"language\": \"l_english\", \"tooltip\": \"x\" } }",
            new UTF8Encoding(false));
        ChecksContext.Equal(
            "成就本地化: pdx_settings.json 的 System.language 生效",
            "english",
            SteamAchievementLocalization.ResolveLanguage(fs, jsonDir, "zh-CN"));

        // 没有任何设置文件时用界面语言。
        var emptyDir = Path.Combine(root, "empty-settings");
        Directory.CreateDirectory(emptyDir);
        ChecksContext.Equal(
            "成就本地化: 设置缺失时 zh-CN 界面 → simp_chinese",
            "simp_chinese",
            SteamAchievementLocalization.ResolveLanguage(fs, emptyDir, "zh-CN"));
        ChecksContext.Equal(
            "成就本地化: 设置和界面语言都没有 → english",
            "english",
            SteamAchievementLocalization.ResolveLanguage(fs, emptyDir, null));

        ChecksContext.Equal(
            "成就本地化: l_braz_por 归一化",
            "braz_por",
            SteamAchievementLocalization.NormalizeGameLanguage("l_braz_por"));
        ChecksContext.Equal(
            "成就本地化: 非法语言不落地成通配符",
            string.Empty,
            SteamAchievementLocalization.NormalizeGameLanguage("../../evil*"));

        // 顺带确认 logger 参数没被忽略（这里只构造，不触发日志）。
        _ = logger;
    }

    private static void CheckLoadAndMarkup(IFileSystem fs, string root)
    {
        var logger = new SilentLogger();
        var installRoot = Path.Combine(root, "game-install");
        var chineseDir = Path.Combine(installRoot, "game", "localization", "simp_chinese");
        var englishDir = Path.Combine(installRoot, "game", "localization", "english");
        Directory.CreateDirectory(chineseDir);
        Directory.CreateDirectory(englishDir);

        File.WriteAllText(
            Path.Combine(chineseDir, "achievements_l_simp_chinese.yml"),
            "l_simp_chinese:\n"
            + " ACHIEVEMENT_achievement_foo:0 \"§Y黄金时代§! #bold $gold$[GetGold] 达成\"\n"
            + " ACHIEVEMENT_DESC_achievement_foo:0 \"描述：\\\"引号\\\" #italic 斜体#! 文本\"\n"
            + " ACHIEVEMENT_achievement_commented:0 \"卑躬屈膝\" #I'm judging you\n"
            + " OTHER_KEY:0 \"不是成就\"\n",
            new UTF8Encoding(true));

        // 同语言目录下的非成就文件：可以解析，但不能让覆盖率判定误以为有成就键。
        File.WriteAllText(
            Path.Combine(chineseDir, "tooltips_l_simp_chinese.yml"),
            "TOOLTIP_KEY:0 \"提示\"\n",
            new UTF8Encoding(false));

        File.WriteAllText(
            Path.Combine(englishDir, "achievements_l_english.yml"),
            "l_english:\n"
            + " ACHIEVEMENT_achievement_foo:0 \"Golden Age\"\n"
            + " ACHIEVEMENT_DESC_achievement_foo:0 \"English description\"\n",
            new UTF8Encoding(true));

        var chinese = SteamAchievementLocalization.Load(fs, installRoot, "simp_chinese", logger);
        ChecksContext.Equal(
            "成就本地化: 中文名命中且 §/#/$/[] 标记被剥掉",
            "黄金时代 达成",
            SteamAchievementLocalization.Name(chinese, "achievement_foo"));
        ChecksContext.Equal(
            "成就本地化: 中文描述命中且转义引号还原",
            "描述：\"引号\" 斜体 文本",
            SteamAchievementLocalization.Description(chinese, "achievement_foo"));
        ChecksContext.Equal(
            "成就本地化: 键大小写不敏感",
            "黄金时代 达成",
            SteamAchievementLocalization.Name(chinese, "ACHIEVEMENT_FOO"));

        ChecksContext.Equal(
            "成就本地化: 值后带 # 注释仍能解析（VIC3 bootlicker 实况）",
            "卑躬屈膝",
            SteamAchievementLocalization.Name(chinese, "achievement_commented"));

        // 没有法语目录 → 自动回退英文，而不是给一页空白。
        var fallback = SteamAchievementLocalization.Load(fs, installRoot, "french", logger);
        ChecksContext.Equal(
            "成就本地化: 缺失语言目录时回退 english",
            "Golden Age",
            SteamAchievementLocalization.Name(fallback, "achievement_foo"));

        // 目录在、但里面只有非成就词条 → 同样回退英文（不能只看目录是否存在）。
        var germanDir = Path.Combine(installRoot, "game", "localization", "german");
        Directory.CreateDirectory(germanDir);
        File.WriteAllText(
            Path.Combine(germanDir, "tooltips_l_german.yml"),
            "l_german:\n TOOLTIP_KEY:0 \"Tipp\"\n",
            new UTF8Encoding(false));
        var german = SteamAchievementLocalization.Load(fs, installRoot, "german", logger);
        ChecksContext.Equal(
            "成就本地化: 目录存在但没有成就键时回退 english",
            "Golden Age",
            SteamAchievementLocalization.Name(german, "achievement_foo"));

        // 文件名不带 achiev：快速路径读不到键时必须退回全量扫描，否则改过名的包会整页英文。
        var oddRoot = Path.Combine(root, "odd-name-install");
        var oddDir = Path.Combine(oddRoot, "localization", "simp_chinese");
        Directory.CreateDirectory(oddDir);
        File.WriteAllText(
            Path.Combine(oddDir, "zz_misc_l_simp_chinese.yml"),
            "l_simp_chinese:\n ACHIEVEMENT_achievement_foo:0 \"改过名的文件\"\n",
            new UTF8Encoding(true));
        var odd = SteamAchievementLocalization.Load(fs, oddRoot, "simp_chinese", logger);
        ChecksContext.Equal(
            "成就本地化: 文件名不带 achiev 也能读到（快速路径失败后全量扫描）",
            "改过名的文件",
            SteamAchievementLocalization.Name(odd, "achievement_foo"));

        // 目录根本不存在时不抛异常。
        var missing = SteamAchievementLocalization.Load(
            fs,
            Path.Combine(root, "no-such-install"),
            "simp_chinese",
            logger);
        ChecksContext.Equal("成就本地化: 目录不存在返回空字典", 0, missing.Count);
    }

    private static void CheckMappingWithLocalization(IFileSystem fs, string root)
    {
        var logger = new SilentLogger();
        var installRoot = Path.Combine(root, "mapping-install");
        var locDir = Path.Combine(installRoot, "localization", "simp_chinese");
        Directory.CreateDirectory(locDir);
        File.WriteAllText(
            Path.Combine(locDir, "achievements_l_simp_chinese.yml"),
            "l_simp_chinese:\n"
            + " ACHIEVEMENT_achievement_foo:0 \"黄金时代\"\n"
            + " ACHIEVEMENT_DESC_achievement_foo:0 \"本地化描述\"\n",
            new UTF8Encoding(true));

        var localization = SteamAchievementLocalization.Load(fs, installRoot, "simp_chinese", logger);
        var snapshot = SteamAchievementMapping.FromHostResult(
            SteamHostAchievementsResult.Success(
            [
                new SteamHostAchievementItem
                {
                    Name = "achievement_foo",
                    DisplayName = "English Foo",
                    Description = "English description",
                },
                new SteamHostAchievementItem
                {
                    Name = "achievement_bar",
                    DisplayName = "English Bar",
                },
                new SteamHostAchievementItem
                {
                    Name = "achievement_baz",
                    DisplayName = "achievement_baz",
                },
            ]),
            DateTimeOffset.UnixEpoch,
            localization);

        ChecksContext.Equal("成就映射: 有本地化时用本地化名", "黄金时代", snapshot.Entries[0].Name);
        ChecksContext.Equal("成就映射: 有本地化时用本地化描述", "本地化描述", snapshot.Entries[0].Description);
        ChecksContext.Equal(
            "成就映射: 没本地化时保留 Steam 显示名",
            "English Bar",
            snapshot.Entries[1].Name);
        ChecksContext.Equal(
            "成就映射: displayName 等于 API 名时改用占位符",
            SteamAchievementMapping.UnknownName,
            snapshot.Entries[2].Name);

        var noDisplayName = SteamAchievementMapping.MapEntries(
            [new SteamHostAchievementItem { Name = "achievement_qux" }]);
        ChecksContext.Equal(
            "成就映射: 连 displayName 都没有时绝不显示 API 名",
            SteamAchievementMapping.UnknownName,
            noDisplayName[0].Name);
    }

    private static void CheckCallResultBinding()
    {
        ChecksContext.Equal(
            "globalPercent: CallResult 统一回调 id 为 703",
            703,
            SteamAchievementsHostOperation.SteamApiCallCompletedCallbackId);
        ChecksContext.Equal(
            "globalPercent: 结果回调 id 为 1110",
            1110,
            SteamAchievementsHostOperation.GlobalPercentagesReadyCallbackId);
        ChecksContext.Equal(
            "globalPercent: GlobalAchievementPercentagesReady_t x64 布局为 16 字节",
            16,
            SteamAchievementsHostOperation.GlobalPercentagesReadySize);

        ChecksContext.Equal(
            "globalPercent: SteamAPICallCompleted_t x64 布局为 16 字节",
            16,
            SteamAchievementsHostOperation.SteamApiCallCompletedSize);

        var field = typeof(SteamAchievementsHostOperation).GetField(
            "_manualDispatchGetApiCallResult",
            BindingFlags.Instance | BindingFlags.NonPublic);
        ChecksContext.Check(
            "globalPercent: 已声明 GetAPICallResult 委托字段",
            field is not null && field.FieldType.Name.Contains("ManualDispatchGetApiCallResultFn", StringComparison.Ordinal),
            field?.FieldType.Name ?? "null");

        var sourcePath = FindRepoFile("src/PdxLauncher.Core/Steam/SteamAchievementsHostOperation.cs");
        ChecksContext.Check(
            "globalPercent: 源码绑定了 SteamAPI_ManualDispatch_GetAPICallResult",
            sourcePath is not null
                && File.ReadAllText(sourcePath).Contains(
                    "\"SteamAPI_ManualDispatch_GetAPICallResult\"",
                    StringComparison.Ordinal),
            sourcePath ?? "找不到源码");
    }

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                var candidate = Path.Combine(dir.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(candidate) ? candidate : null;
            }

            dir = dir.Parent;
        }

        return null;
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
