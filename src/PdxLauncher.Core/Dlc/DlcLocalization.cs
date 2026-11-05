namespace PdxLauncher.Core.Dlc;

using System.Text;
using System.Text.RegularExpressions;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// EU5 的 DLC 显示名不在描述文件里（<c>.dlc.json</c> 只有 <c>localizable_name</c> 这种 key），
/// 而在 <c>&lt;DlcPath&gt;\*\main_menu\localization\english\*.yml</c> 里。这里把英文 yml 读成
/// key → 纯文本的字典，供 <see cref="DlcCatalog"/> 拼显示名。
/// <para>只做「够用」的解析：单行 <c>key: "value"</c> / <c>key:0 "value"</c>，其余（嵌套块、注释）跳过。</para>
/// </summary>
public static partial class DlcLocalization
{
    private const string LocalizationFolder = "localization";
    private const string EnglishLanguage = "english";

    /// <summary>某语言的本地化文件通配符（如 <c>*_l_simp_chinese.yml</c>）；语言名由调用方归一化。</summary>
    internal static string PatternForLanguage(string language) => "*_l_" + language + ".yml";

    /// <summary>读 <paramref name="dlcPath"/> 下所有英文 yml，返回 key → 去掉富文本标记的显示名。</summary>
    /// <param name="fileSystem">文件系统。</param>
    /// <param name="dlcPath">DLC 根目录（GameInstallation.DlcPath）。</param>
    /// <param name="logger">日志。</param>
    /// <returns>key（大小写不敏感）→ 显示名；读不到任何文件时为空字典。</returns>
    public static IReadOnlyDictionary<string, string> Load(
        IFileSystem fileSystem,
        string dlcPath,
        IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(logger);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(dlcPath) || !fileSystem.DirectoryExists(dlcPath))
        {
            return result;
        }

        IEnumerable<string> files;
        try
        {
            files = fileSystem
                .EnumerateFiles(dlcPath, PatternForLanguage(EnglishLanguage), recursive: true)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch (IOException)
        {
            return result;
        }

        foreach (var file in files)
        {
            // 只认 localization 目录下的文件（DLC 包里也可能有别的 *\_l_english.yml）。
            if (!file.Replace('\\', '/').Contains('/' + LocalizationFolder + '/', StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            string text;
            try
            {
                text = Encoding.UTF8.GetString(fileSystem.ReadAllBytes(file));
            }
            catch (IOException exception)
            {
                logger.Warn("读取 DLC 本地化失败，已跳过：" + file + "：" + exception.Message);
                continue;
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.Warn("读取 DLC 本地化无权限，已跳过：" + file + "：" + exception.Message);
                continue;
            }

            ParseInto(text, result);
        }

        return result;
    }

    /// <summary>把一段 yml 文本里的 <c>key: "value"</c> 灌进字典（已存在的 key 保留先读到的值）。</summary>
    /// <remarks>成就本地化（<see cref="Steam.SteamAchievementLocalization"/>）复用同一套解析。</remarks>
    internal static void ParseInto(string text, Dictionary<string, string> result)
    {
        foreach (var (key, raw) in EnumerateEntries(text))
        {
            var value = CleanMarkup(raw);
            if (value.Length == 0)
            {
                continue;
            }

            result.TryAdd(key, value);
        }
    }

    /// <summary>
    /// 只解析出 <c>key → 原始值</c>，不剥标记。
    /// <para>
    /// 给「先还原引用、再剥标记」的调用方用：CK3 的
    /// <c>ACHIEVEMENT_ep1_01_achievement: "$ACHIEVEMENT_patronage_achievement$"</c>
    /// 是别名，先剥标记就什么都不剩了。去重规则与 <see cref="ParseInto"/> 一致（先读到的赢）。
    /// </para>
    /// </summary>
    internal static void ParseRawInto(string text, Dictionary<string, string> result)
    {
        foreach (var (key, raw) in EnumerateEntries(text))
        {
            if (raw.Length == 0)
            {
                continue;
            }

            result.TryAdd(key, raw);
        }
    }

    /// <summary>按行吐出 <c>(key, 原始值)</c>；key 或值为空的跳过。</summary>
    private static IEnumerable<(string Key, string Raw)> EnumerateEntries(string text)
    {
        foreach (var line in text.Split('\n'))
        {
            var match = EntryLine().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            if (key.Length > 0)
            {
                yield return (key, match.Groups["value"].Value);
            }
        }
    }

    /// <summary>去掉 Clausewitz 的富文本标记（<c>#italic</c>、<c>#!</c>、<c>#bold</c>、<c>$var$</c>、<c>[link]</c>、<c>§Y</c>、<c>£icon£</c> 等），压缩空白。</summary>
    internal static string CleanMarkup(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);

        // 顺序有讲究：$var$ 里可能嵌 [..]，先把变量引用整个拿掉，避免只留半截方括号。
        var withoutVariables = VariableRef().Replace(raw, string.Empty);
        var withoutIcons = IconToken().Replace(withoutVariables, string.Empty);
        var withoutLinks = ConceptLink().Replace(withoutIcons, string.Empty);
        var withoutColors = ColorCode().Replace(withoutLinks, string.Empty);
        var withoutMarkup = MarkupToken().Replace(withoutColors, string.Empty).Replace("#!", string.Empty, StringComparison.Ordinal);
        var unescaped = withoutMarkup.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\n", " ", StringComparison.Ordinal);

        // 其它收尾标记（"#bold ...#!" 之外还有零散的 #）不是单词语义的一部分，直接去掉。
        var trimmed = unescaped.Replace("#", string.Empty, StringComparison.Ordinal);
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;
        foreach (var ch in trimmed.Trim())
        {
            var isSpace = char.IsWhiteSpace(ch);
            if (isSpace && lastWasSpace)
            {
                continue;
            }

            builder.Append(isSpace ? ' ' : ch);
            lastWasSpace = isSpace;
        }

        return builder.ToString().Trim();
    }

    [GeneratedRegex("""^\s*(?<key>[A-Za-z0-9_.-]+?):\d*\s*"(?<value>(?:[^"\\]|\\.)*)"\s*(?:#.*)?$""")]
    private static partial Regex EntryLine();

    [GeneratedRegex("""#[A-Za-z_]\S*""")]
    private static partial Regex MarkupToken();

    [GeneratedRegex("""\$[^$\r\n]*\$""")]
    private static partial Regex VariableRef();

    [GeneratedRegex("""£[^£\r\n]*£""")]
    private static partial Regex IconToken();

    [GeneratedRegex("""\[[^\]\r\n]*\]""")]
    private static partial Regex ConceptLink();

    [GeneratedRegex("""§[^\r\n]""")]
    private static partial Regex ColorCode();
}
