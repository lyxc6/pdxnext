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

    /// <summary>别名链最多跟这么多跳，防止病态数据把解析拖住。</summary>
    private const int MaxAliasDepth = 8;

    /// <summary>本地化缓存最多留这几款游戏的语言。</summary>
    private const int CacheCapacity = 8;

    /// <summary>
    /// 缓存有效期。不用目录 mtime 做失效判断：CK3 的 yml 散在 <c>localization/&lt;lang&gt;/dlc/ep1/</c>
    /// 这类子目录里，父目录 mtime 不随子目录内容变，而枚举一遍全部 yml 拿最新时间戳的代价
    /// 和重新解析差不多（1 秒级），等于白缓存。游戏更新是低频事件，10 分钟陈旧可接受。
    /// </summary>
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);

    private static readonly object CacheGate = new();

    private static readonly Dictionary<string, CacheEntry> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>一条缓存：解析结果 + 过期时间。</summary>
    private sealed class CacheEntry
    {
        public required IReadOnlyDictionary<string, string> Value { get; init; }

        public required DateTime ExpiresUtc { get; init; }
    }

    private static string CacheKey(string installRoot, string language, int expectedNames)
        => (installRoot ?? string.Empty).Trim().TrimEnd('\\', '/') + "|" + language + "|" + expectedNames;

    private static IReadOnlyDictionary<string, string>? TryGetCached(string key)
    {
        lock (CacheGate)
        {
            if (!Cache.TryGetValue(key, out var entry))
            {
                return null;
            }

            if (entry.ExpiresUtc > DateTime.UtcNow)
            {
                return entry.Value;
            }

            Cache.Remove(key);
            return null;
        }
    }

    private static void Store(string key, IReadOnlyDictionary<string, string> value)
    {
        lock (CacheGate)
        {
            if (Cache.Count >= CacheCapacity && !Cache.ContainsKey(key))
            {
                // 满了先清过期的；还满就丢最早过期的那个（缓存只影响速度，不影响正确性）。
                var now = DateTime.UtcNow;
                var expired = new List<string>();
                var oldestKey = string.Empty;
                var oldestExpiry = DateTime.MaxValue;
                foreach (var pair in Cache)
                {
                    if (pair.Value.ExpiresUtc <= now)
                    {
                        expired.Add(pair.Key);
                    }
                    else if (pair.Value.ExpiresUtc < oldestExpiry)
                    {
                        oldestExpiry = pair.Value.ExpiresUtc;
                        oldestKey = pair.Key;
                    }
                }

                foreach (var stale in expired)
                {
                    Cache.Remove(stale);
                }

                if (Cache.Count >= CacheCapacity && oldestKey.Length > 0)
                {
                    Cache.Remove(oldestKey);
                }
            }

            Cache[key] = new CacheEntry
            {
                Value = value,
                ExpiresUtc = DateTime.UtcNow.Add(CacheTtl),
            };
        }
    }

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
    /// 该语言一个 <c>ACHIEVEMENT_</c> 名键都没有时（例如只装了英文）自动改用英文，避免把整页变空。
    /// </summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="installRoot">游戏安装根目录（<see cref="Contracts.GameInstallation.InstallRoot"/>）。</param>
    /// <param name="language">已归一化的语言目录名。</param>
    /// <param name="logger">日志。</param>
    /// <param name="expectedNames">
    /// 宿主返回的成就条数。给了就按「读到的名键数是否够用」决定要不要补扫非成就命名的 yml；
    /// 不给则退化成「一条都没有才补扫」。
    /// </param>
    /// <returns>key → 纯文本，大小写不敏感；读不到时为空字典。</returns>
    public static IReadOnlyDictionary<string, string> Load(
        IFileSystem fileSystem,
        string installRoot,
        string language,
        IAppLogger logger,
        int expectedNames = 0)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        var normalized = NormalizeGameLanguage(language);
        if (normalized.Length == 0)
        {
            normalized = EnglishLanguage;
        }

        var cacheKey = CacheKey(installRoot, normalized, expectedNames);
        if (TryGetCached(cacheKey) is { } cached)
        {
            return cached;
        }

        var result = LoadWithFallback(fileSystem, installRoot, normalized, logger, expectedNames);
        Store(cacheKey, result);
        return result;
    }

    /// <summary>清掉本地化缓存（自检用；真实使用中靠 TTL 过期）。</summary>
    internal static void ClearCache()
    {
        lock (CacheGate)
        {
            Cache.Clear();
        }
    }

    private static IReadOnlyDictionary<string, string> LoadWithFallback(
        IFileSystem fileSystem,
        string installRoot,
        string normalized,
        IAppLogger logger,
        int expectedNames)
    {
        var result = LoadCore(fileSystem, installRoot, normalized, logger, expectedNames);
        if (!string.Equals(normalized, EnglishLanguage, StringComparison.Ordinal)
            && !HasAchievementKeys(result))
        {
            var fallback = LoadCore(fileSystem, installRoot, EnglishLanguage, logger, expectedNames);
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
        => CountNameKeys(localization) > 0;

    /// <summary>数「名键」条数：<c>ACHIEVEMENT_&lt;api&gt;</c>，不含 <c>ACHIEVEMENT_DESC_</c>。</summary>
    private static int CountNameKeys(IReadOnlyDictionary<string, string> localization)
    {
        var count = 0;
        foreach (var key in localization.Keys)
        {
            if (key.StartsWith(NameKeyPrefix, StringComparison.OrdinalIgnoreCase)
                && !key.StartsWith(DescriptionKeyPrefix, StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count;
    }

    private static IReadOnlyDictionary<string, string> LoadCore(
        IFileSystem fileSystem,
        string installRoot,
        string language,
        IAppLogger logger,
        int expectedNames)
    {
        var raw = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(installRoot) || !fileSystem.DirectoryExists(installRoot))
        {
            return raw;
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
            return raw;
        }
        catch (UnauthorizedAccessException exception)
        {
            logger.Warn("枚举成就本地化无权限，已跳过：" + installRoot + "：" + exception.Message);
            return raw;
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
        var remaining = new List<string>(candidates.Count);
        foreach (var file in candidates)
        {
            if (LooksLikeAchievementFile(file))
            {
                ParseFile(fileSystem, file, logger, raw);
            }
            else
            {
                remaining.Add(file);
            }
        }

        ResolveAliases(raw, logger);

        // 文件名不按惯例起的本地化（旧版本、改过名的包）照样要能用：名键不够覆盖宿主条目数就补扫。
        // 没有宿主条目数时退化成「一条名键都没有才补扫」。
        var needed = expectedNames > 0 ? expectedNames : 1;
        if (CountNameKeys(raw) < needed)
        {
            foreach (var file in remaining)
            {
                ParseFile(fileSystem, file, logger, raw);
            }

            ResolveAliases(raw, logger);
        }

        return Clean(raw);
    }

    /// <summary>
    /// 把原始值里的别名还原掉，再统一剥标记；剥成空串的条目丢弃。
    /// <para>
    /// 混在句子里的 <c>$var$</c>（整值别名已在上一步还原）在这里剥掉：
    /// 它只在成就文本里是占位符，DLC 名里的 <c>$PRICE$</c> 则要保留，所以不能放进共享的
    /// <see cref="DlcLocalization.CleanMarkup"/>。
    /// </para>
    /// </summary>
    private static IReadOnlyDictionary<string, string> Clean(Dictionary<string, string> raw)
    {
        var result = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var withoutVariables = VariableRef().Replace(value, string.Empty);
            var cleaned = DlcLocalization.CleanMarkup(withoutVariables);
            if (cleaned.Length > 0)
            {
                result[key] = cleaned;
            }
        }

        return result;
    }

    /// <summary>
    /// 还原 <c>"$ACHIEVEMENT_patronage_achievement$"</c> 这种整值别名。
    /// <para>
    /// CK3 的 DLC 成就名不在自己的 yml 里写死，而是引用基础成就的键；先剥标记会把整值清空，
    /// 于是这类条目全退回 Steam 英文名。这里按链式还原：带深度上限与环检测，还原不出来就保持原样
    /// （外层会剥成空串，最终回退 Steam 显示名），绝不为了「有值」而输出内部键名。
    /// </para>
    /// </summary>
    private static void ResolveAliases(Dictionary<string, string> raw, IAppLogger logger)
    {
        if (raw.Count == 0)
        {
            return;
        }

        var aliases = new List<string>();
        foreach (var pair in raw)
        {
            if (AliasTarget(pair.Value) is not null)
            {
                aliases.Add(pair.Key);
            }
        }

        if (aliases.Count == 0)
        {
            return;
        }

        var unresolved = 0;
        foreach (var key in aliases)
        {
            if (FollowAlias(raw, key) is { } resolved)
            {
                raw[key] = resolved;
            }
            else
            {
                unresolved++;
            }
        }

        if (unresolved > 0)
        {
            logger.Info("成就本地化：" + unresolved + " 条别名无法还原（目标缺失或成环），保留 Steam 显示名");
        }
    }

    /// <summary>顺着别名链走到最终值；目标缺失、成环或链过长时返回 <c>null</c>。</summary>
    private static string? FollowAlias(Dictionary<string, string> raw, string key)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { key };
        var current = raw[key];
        for (var depth = 0; depth < MaxAliasDepth; depth++)
        {
            var target = AliasTarget(current);
            if (target is null)
            {
                return current;
            }

            if (!visited.Add(target) || !raw.TryGetValue(target, out var next))
            {
                return null;
            }

            current = next;
        }

        return null;
    }

    private static string? AliasTarget(string value)
    {
        var match = AliasValue().Match(value.Trim());
        return match.Success ? match.Groups["key"].Value : null;
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

        DlcLocalization.ParseRawInto(text, result);
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

    [GeneratedRegex("""^\s*\x22language\x22\s*=\s*\{[^}]*?value\s*=\s*\x22(?<language>[^\x22]+)\x22""", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex LanguageBlock();

    [GeneratedRegex("""^\s*\x22language\x22\s*=\s*\x22(?<language>[^\x22]+)\x22""", RegexOptions.Multiline)]
    private static partial Regex LanguageInline();

    [GeneratedRegex("^[a-z0-9_]+$")]
    private static partial Regex LanguageToken();

    /// <summary>整值别名：<c>$ACHIEVEMENT_foo$</c>。混在句子里的 <c>$var$</c> 不算，那由 <see cref="Clean"/> 在还原之后剥。</summary>
    [GeneratedRegex("""^\$(?<key>[A-Za-z0-9_.-]+)\$$""")]
    private static partial Regex AliasValue();

    /// <summary>句子里残留的变量引用（<c>$COUNTRY$</c> 之类），在成就文本里当占位符剥掉。</summary>
    [GeneratedRegex("""\$[^$\r\n]*\$""")]
    private static partial Regex VariableRef();
}
