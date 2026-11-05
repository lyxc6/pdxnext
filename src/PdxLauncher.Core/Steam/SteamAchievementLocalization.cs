namespace PdxLauncher.Core.Steam;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Dlc;

/// <summary>
/// 从游戏安装目录的 <c>localization/&lt;lang&gt;/*_l_&lt;lang&gt;.yml</c> 读成就名/描述。
/// <para>
/// Steamworks 的 <c>GetAchievementDisplayAttribute</c> 用的是**每游戏语言**
/// （<c>appmanifest_&lt;appid&gt;.acf</c> 的 <c>UserConfig.language</c>），不随 Steam 客户端语言，
/// 所以成就页常常是英文而游戏里是中文。这里直接复用游戏自己的本地化文件，
/// 键为 <c>ACHIEVEMENT_&lt;apiName&gt;</c> / <c>ACHIEVEMENT_DESC_&lt;apiName&gt;</c>。
/// </para>
/// <para>
/// 语言取值顺序：<c>GameDataDir\pdx_settings.txt</c> / <c>pdx_settings.json</c> 里的
/// <c>"language"</c> → 启动器界面语言 → <c>english</c>。游戏设置里写的是 <c>l_simp_chinese</c>
/// 这种带 <c>l_</c> 前缀的形式，这里统一归一化成目录名 <c>simp_chinese</c>。
/// </para>
/// </summary>
public static partial class SteamAchievementLocalization
{
    /// <summary>最终回退语言，也是本地化文件的语言目录名。</summary>
    public const string EnglishLanguage = "english";

    /// <summary>成就名在 yml 里的键前缀，后面直接拼 API 名（VIC3 的 API 名自带 <c>achievement_</c>，照样直拼）。</summary>
    public const string NameKeyPrefix = "ACHIEVEMENT_";

    /// <summary>成就描述在 yml 里的键前缀。</summary>
    public const string DescriptionKeyPrefix = "ACHIEVEMENT_DESC_";

    private const string SettingsFileName = "pdx_settings.txt";
    private const string SettingsJsonFileName = "pdx_settings.json";
    private const string LocalizationFolder = "localization";

    /// <summary>
    /// 取游戏本地化语言：先读 <c>pdx_settings</c>，读不到用界面语言映射，再退回 <c>english</c>。
    /// </summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="gameDataDir">游戏数据目录（<see cref="Contracts.GameInstallation.GameDataDir"/>）。</param>
    /// <param name="uiLanguage">启动器界面语言（如 <c>zh-CN</c> / <c>en-US</c>）；可为空。</param>
    /// <returns>已归一化的语言目录名，绝不为空。</returns>
    public static string ResolveLanguage(IFileSystem fileSystem, string? gameDataDir, string? uiLanguage)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (!string.IsNullOrWhiteSpace(gameDataDir) && fileSystem.DirectoryExists(gameDataDir))
        {
            var fromSettings = ReadGameLanguage(fileSystem, gameDataDir);
            if (fromSettings.Length > 0)
            {
                return fromSettings;
            }
        }

        return NormalizeUiLanguage(uiLanguage);
    }

    /// <summary>把 <c>l_simp_chinese</c> / <c>"simp_chinese"</c> 归一化成 <c>simp_chinese</c>；不合法返回空串。</summary>
    public static string NormalizeGameLanguage(string? language)
    {
        if (string.IsNullOrWhiteSpace(language))
        {
            return string.Empty;
        }

        var value = language.Trim().Trim('"').Trim().ToLowerInvariant();
        if (value.StartsWith("l_", StringComparison.Ordinal))
        {
            value = value[2..];
        }

        return value.Length > 0 && LanguageToken().IsMatch(value) ? value : string.Empty;
    }

    /// <summary>把启动器界面语言映射成游戏语言目录名；认不出时返回 <c>english</c>。</summary>
    public static string NormalizeUiLanguage(string? uiLanguage)
    {
        if (string.IsNullOrWhiteSpace(uiLanguage))
        {
            return EnglishLanguage;
        }

        var value = uiLanguage.Trim().ToLowerInvariant().Replace('_', '-');
        var dash = value.IndexOf('-');
        var primary = dash > 0 ? value[..dash] : value;
        return primary switch
        {
            "zh" => "simp_chinese",
            "en" => EnglishLanguage,
            "fr" => "french",
            "de" => "german",
            "es" => "spanish",
            "ru" => "russian",
            "pl" => "polish",
            "pt" => value.Contains("br", StringComparison.Ordinal) ? "braz_por" : "portuguese",
            "ja" => "japanese",
            "ko" => "korean",
            "tr" => "turkish",
            _ => EnglishLanguage,
        };
    }

    /// <summary>
    /// 读 <paramref name="installRoot"/> 下指定语言的成就本地化。
    /// 该语言一个 <c>ACHIEVEMENT_</c> 键都没有时（例如只装了英文）自动改用英文，避免把整页变空。
    /// </summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="installRoot">游戏安装根目录（<see cref="Contracts.GameInstallation.InstallRoot"/>）。</param>
    /// <param name="language">已归一化的语言目录名。</param>
    /// <param name="logger">日志。</param>
    /// <returns>key → 纯文本，大小写不敏感；读不到时为空字典。</returns>
    public static IReadOnlyDictionary<string, string> Load(
        IFileSystem fileSystem,
        string installRoot,
        string language,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        var normalized = NormalizeGameLanguage(language);
        if (normalized.Length == 0)
        {
            normalized = EnglishLanguage;
        }

        var result = LoadCore(fileSystem, installRoot, normalized, logger);
        if (!string.Equals(normalized, EnglishLanguage, StringComparison.Ordinal)
            && !HasAchievementKeys(result))
        {
            var fallback = LoadCore(fileSystem, installRoot, EnglishLanguage, logger);
            if (HasAchievementKeys(fallback))
            {
                logger.Info("成就本地化：语言 " + normalized + " 没有成就键，已回退 english");
                return fallback;
            }
        }

        return result;
    }

    /// <summary>按 API 名取成就名；找不到返回 <c>null</c>（记得再回退 Steam 显示名）。</summary>
    public static string? Name(IReadOnlyDictionary<string, string>? localization, string? apiName)
        => Lookup(localization, NameKeyPrefix, apiName);

    /// <summary>按 API 名取成就描述；找不到返回 <c>null</c>。</summary>
    public static string? Description(IReadOnlyDictionary<string, string>? localization, string? apiName)
        => Lookup(localization, DescriptionKeyPrefix, apiName);

    private static string? Lookup(IReadOnlyDictionary<string, string>? localization, string prefix, string? apiName)
    {
        if (localization is null || localization.Count == 0 || string.IsNullOrWhiteSpace(apiName))
        {
            return null;
        }

        return localization.TryGetValue(prefix + apiName, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : null;
    }

    private static bool HasAchievementKeys(IReadOnlyDictionary<string, string> localization)
    {
        foreach (var key in localization.Keys)
        {
            if (key.StartsWith(NameKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static IReadOnlyDictionary<string, string> LoadCore(
        IFileSystem fileSystem,
        string installRoot,
        string language,
        IAppLogger logger)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installRoot) || !fileSystem.DirectoryExists(installRoot))
        {
            return result;
        }

        List<string> files;
        try
        {
            files = fileSystem
                .EnumerateFiles(installRoot, DlcLocalization.PatternForLanguage(language), recursive: true)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException exception)
        {
            logger.Warn("枚举成就本地化失败，已跳过：" + installRoot + "：" + exception.Message);
            return result;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.Warn("枚举成就本地化无权限，已跳过：" + installRoot + "：" + exception.Message);
            return result;
        }

        var marker = "/" + LocalizationFolder + "/" + language + "/";
        var candidates = new List<string>(files.Count);
        foreach (var file in files)
        {
            // 只认 localization/<language>/ 目录下的文件；mod / jomini 里的同名文件不算。
            if (file.Replace('\\', '/').Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                candidates.Add(file);
            }
        }

        // 成就键几乎总在文件名带 achiev 的 yml 里（VIC3 只有 1 个、CK3 也只有 7 个），
        // 而 CK3 的 simp_chinese 有 1249 个文件 / 26 MB —— 全读一遍要两秒多。先只读这些。
        var preferred = new List<string>();
        foreach (var file in candidates)
        {
            if (LooksLikeAchievementFile(file))
            {
                preferred.Add(file);
            }
        }

        foreach (var file in preferred)
        {
            ParseFile(fileSystem, file, logger, result);
        }

        // 文件名不按惯例起的本地化（旧版本、改过名的包）照样要能用：
        // 一条成就键都没读到就退回全量扫描，不让「没按套路命名」变成「整页英文」。
        if (!HasAchievementKeys(result))
        {
            foreach (var file in candidates)
            {
                if (!LooksLikeAchievementFile(file))
                {
                    ParseFile(fileSystem, file, logger, result);
                }
            }
        }

        return result;
    }

    /// <summary>文件名是否带 <c>achiev</c>（<c>achievements_l_xx.yml</c> / <c>dlc_ep1_achievements_l_xx.yml</c>）。</summary>
    private static bool LooksLikeAchievementFile(string path)
        => Path.GetFileName(path).Contains("achiev", StringComparison.OrdinalIgnoreCase);

    private static void ParseFile(
        IFileSystem fileSystem,
        string file,
        IAppLogger logger,
        Dictionary<string, string> result)
    {
        string text;
        try
        {
            text = Encoding.UTF8.GetString(fileSystem.ReadAllBytes(file));
        }
        catch (IOException exception)
        {
            logger.Warn("读取成就本地化失败，已跳过：" + file + "：" + exception.Message);
            return;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.Warn("读取成就本地化无权限，已跳过：" + file + "：" + exception.Message);
            return;
        }

        DlcLocalization.ParseInto(text, result);
    }

    private static string ReadGameLanguage(IFileSystem fileSystem, string gameDataDir)
    {
        var settingsPath = fileSystem.Combine(gameDataDir, SettingsFileName);
        if (fileSystem.FileExists(settingsPath))
        {
            try
            {
                var text = fileSystem.ReadAllText(settingsPath);
                var match = LanguageBlock().Match(text);
                if (!match.Success)
                {
                    match = LanguageInline().Match(text);
                }

                if (match.Success)
                {
                    var language = NormalizeGameLanguage(match.Groups["language"].Value);
                    if (language.Length > 0)
                    {
                        return language;
                    }
                }
            }
            catch (IOException)
            {
                // 设置文件坏/被占用时静默回退，成就页不该因此不可用。
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        var jsonPath = fileSystem.Combine(gameDataDir, SettingsJsonFileName);
        if (!fileSystem.FileExists(jsonPath))
        {
            return string.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(fileSystem.ReadAllText(jsonPath));
            return TryFindLanguage(document.RootElement) is { } raw
                ? NormalizeGameLanguage(raw)
                : string.Empty;
        }
        catch (JsonException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (UnauthorizedAccessException)
        {
            return string.Empty;
        }
    }

    private static string? TryFindLanguage(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (string.Equals(property.Name, "language", StringComparison.OrdinalIgnoreCase)
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var value = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            return value;
                        }
                    }
                }

                foreach (var property in element.EnumerateObject())
                {
                    if (TryFindLanguage(property.Value) is { } nested)
                    {
                        return nested;
                    }
                }

                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (TryFindLanguage(item) is { } nested)
                    {
                        return nested;
                    }
                }

                break;
        }

        return null;
    }

    [GeneratedRegex("""^\s*\x22language\x22\s*=\s*\{[^}]*?value\s*=\s*\x22(?<language>[^\x22]+)\x22""", RegexOptions.Singleline)]
    private static partial Regex LanguageBlock();

    [GeneratedRegex("""^\s*\x22language\x22\s*=\s*\x22(?<language>[^\x22]+)\x22""", RegexOptions.Multiline)]
    private static partial Regex LanguageInline();

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex LanguageToken();
}
