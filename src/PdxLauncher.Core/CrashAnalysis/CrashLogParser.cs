namespace PdxLauncher.Core.CrashAnalysis;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

/// <summary><c>exception.txt</c> 的解析结果。</summary>
public sealed record ExceptionInfo(
    string? Code,
    string? Name,
    string? Address,
    DateTimeOffset? Timestamp,
    string? AppName,
    string? Version,
    string Excerpt)
{
    /// <summary>空结果。</summary>
    public static ExceptionInfo Empty { get; } = new(null, null, null, null, null, null, string.Empty);
}

/// <summary>Mod 的 <c>.metadata/metadata.json</c> 摘要。</summary>
public sealed record ModMetadata(string? Name, string? Version, string? SupportedGameVersion)
{
    /// <summary>空结果。</summary>
    public static ModMetadata Empty { get; } = new(null, null, null);
}

/// <summary>
/// 一条以时间戳开头的日志记录；没有时间戳的续行跟随所属记录。
/// <see cref="RawLines"/> 保留原始行，<see cref="Text"/> 是整块文本，供模式匹配与证据展示。
/// </summary>
public sealed record CrashLogBlock(
    DateTimeOffset? Time,
    string Origin,
    string Message,
    IReadOnlyList<string> RawLines)
{
    /// <summary>整块原始文本（含续行）。</summary>
    public string Text => string.Join("\n", RawLines);

    /// <summary>块里出现的第一个脚本来源路径。</summary>
    public string? SourcePath => CrashLogParser.ExtractSourcePath(Text);
}

/// <summary>P 社游戏日志与崩溃文件的纯文本解析器。</summary>
public static class CrashLogParser
{
    private static readonly Regex LogHeaderPattern = new(
        @"^\[(?<hour>\d{2}):(?<minute>\d{2}):(?<second>\d{2})\]\[(?<origin>[^\]]+)\]:\s?(?<message>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex ExceptionPattern = new(
        @"Unhandled Exception\s+(?<code>[0-9A-Fa-f]{8})\s*\((?<name>[^)]+)\)\s+at\s+address\s+(?<address>0x[0-9A-Fa-f]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex SourcePathPattern = new(
        @"(?<path>(?:common|events|gui|localization|gfx|map_data|music|sound|dlc|game)/[^:\r\n'""]+?\.[A-Za-z0-9]+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    private static readonly Regex MetaLinePattern = new(
        @"^\s*(?<key>[A-Za-z0-9_]+)\s*:\s*(?<value>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>解析 <c>exception.txt</c>；格式不认识时仍返回可用的标题/摘要。</summary>
    public static ExceptionInfo ParseException(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return ExceptionInfo.Empty;
        }

        string normalized = Normalize(text);
        string? code = null;
        string? name = null;
        string? address = null;
        DateTimeOffset? timestamp = null;
        string? appName = null;
        string? version = null;
        var excerpt = new StringBuilder();

        foreach (string rawLine in normalized.Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.StartsWith("Application:", StringComparison.OrdinalIgnoreCase))
            {
                appName ??= ValueAfterColon(line);
                continue;
            }

            if (line.StartsWith("Version:", StringComparison.OrdinalIgnoreCase))
            {
                version ??= ValueAfterColon(line);
                continue;
            }

            if (line.StartsWith("Date/Time:", StringComparison.OrdinalIgnoreCase))
            {
                timestamp ??= TryParseDateTime(ValueAfterColon(line));
                continue;
            }

            if (line.StartsWith("Stack Trace:", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            var match = ExceptionPattern.Match(line);
            if (match.Success)
            {
                code ??= match.Groups["code"].Value.ToUpperInvariant();
                name ??= match.Groups["name"].Value.Trim();
                address ??= match.Groups["address"].Value;
                continue;
            }

            if (excerpt.Length < 600)
            {
                if (excerpt.Length > 0)
                {
                    excerpt.Append('\n');
                }

                excerpt.Append(line);
            }
        }

        return new ExceptionInfo(code, name, address, timestamp, appName, version, excerpt.ToString());
    }

    /// <summary>
    /// 解析 <c>meta.yml</c> 的扁平 <c>Key: Value</c> 行。值两端的引号会去掉；
    /// 注释与无法识别的行直接跳过，绝不因为一行怪格式整份失败。
    /// </summary>
    public static IReadOnlyDictionary<string, string> ParseMetaYaml(string? text)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(text))
        {
            return values;
        }

        foreach (string rawLine in Normalize(text).Split('\n'))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var match = MetaLinePattern.Match(line);
            if (!match.Success)
            {
                continue;
            }

            var key = match.Groups["key"].Value;
            var value = Unquote(match.Groups["value"].Value.Trim());
            if (key.Length > 0)
            {
                values[key] = value;
            }
        }

        return values;
    }

    /// <summary>解析 Mod 的 <c>.metadata/metadata.json</c>；字段缺失或损坏时返回空结果。</summary>
    public static ModMetadata ParseModMetadata(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return ModMetadata.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return ModMetadata.Empty;
            }

            var root = document.RootElement;
            return new ModMetadata(
                ReadString(root, "name"),
                ReadString(root, "version"),
                ReadString(root, "supported_game_version") ?? ReadString(root, "supportedGameVersion"));
        }
        catch (JsonException)
        {
            return ModMetadata.Empty;
        }
        catch (ArgumentException)
        {
            return ModMetadata.Empty;
        }
    }

    /// <summary>
    /// 按行切出日志块。时间只带 <c>HH:mm:ss</c>，用 <paramref name="anchor"/> 的日期补齐；
    /// 没有时间戳的行作为上一块的续行保留。
    /// </summary>
    public static IReadOnlyList<CrashLogBlock> ParseLogBlocks(string? text, DateTimeOffset anchor)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<CrashLogBlock>();
        }

        var blocks = new List<CrashLogBlock>();
        DateTimeOffset? currentTime = null;
        string currentOrigin = string.Empty;
        string currentMessage = string.Empty;
        List<string>? currentLines = null;

        void Flush()
        {
            if (currentLines is { Count: > 0 })
            {
                blocks.Add(new CrashLogBlock(currentTime, currentOrigin, currentMessage, currentLines.ToArray()));
            }

            currentLines = null;
        }

        foreach (string rawLine in Normalize(text).Split('\n'))
        {
            var match = LogHeaderPattern.Match(rawLine);
            if (match.Success)
            {
                Flush();
                currentTime = ResolveClockTime(
                    anchor,
                    int.Parse(match.Groups["hour"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["minute"].Value, CultureInfo.InvariantCulture),
                    int.Parse(match.Groups["second"].Value, CultureInfo.InvariantCulture));
                currentOrigin = match.Groups["origin"].Value.Trim();
                currentMessage = match.Groups["message"].Value;
                currentLines = [rawLine];
                continue;
            }

            if (currentLines is not null)
            {
                currentLines.Add(rawLine);
            }
        }

        Flush();
        return blocks;
    }

    /// <summary>把 <c>HH:mm:ss</c> 对齐到锚点日期；跨午夜时自动减/加一天。</summary>
    public static DateTimeOffset ResolveClockTime(DateTimeOffset anchor, int hour, int minute, int second)
    {
        var candidate = new DateTimeOffset(anchor.Year, anchor.Month, anchor.Day, hour, minute, second, anchor.Offset);
        var delta = candidate - anchor;
        if (delta > TimeSpan.FromHours(12))
        {
            return candidate.AddDays(-1);
        }

        if (delta < TimeSpan.FromHours(-12))
        {
            return candidate.AddDays(1);
        }

        return candidate;
    }

    /// <summary>提取块里的所有脚本来源路径（去重）。</summary>
    public static IReadOnlyList<string> ExtractSourcePaths(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var paths = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in SourcePathPattern.Matches(text))
        {
            var path = match.Groups["path"].Value.Trim();
            if (path.Length > 0 && seen.Add(path))
            {
                paths.Add(path);
            }
        }

        return paths;
    }

    /// <summary>提取块里的第一个脚本来源路径。</summary>
    public static string? ExtractSourcePath(string? text)
    {
        var paths = ExtractSourcePaths(text);
        return paths.Count > 0 ? paths[0] : null;
    }

    /// <summary>把一条错误消息归一成稳定的聚类键（变量名、数字不进入键）。</summary>
    public static string Signature(CrashLogBlock block)
    {
        var message = block.Message;
        if (Contains(message, "Attempting to manually create auto-placed building"))
        {
            return block.Origin + " · create_building auto-placed building";
        }

        if (Contains(message, "is used but is never set"))
        {
            return block.Origin + " · variable used but never set";
        }

        if (Contains(message, "is orphaned"))
        {
            return block.Origin + " · orphaned event";
        }

        if (Contains(message, "defined in script but not in code"))
        {
            return block.Origin + " · modifier type not in code";
        }

        if (Contains(message, "already defined"))
        {
            return block.Origin + " · duplicate definition";
        }

        if (Contains(message, "Missing localization key"))
        {
            return block.Origin + " · missing localization key";
        }

        if (Contains(message, "Missing expected"))
        {
            return block.Origin + " · missing expected definition";
        }

        if (Contains(message, "is incomplete") || Contains(message, "Cannot find a port node"))
        {
            return block.Origin + " · map locator issue";
        }

        if (Contains(message, "should be in utf8-bom encoding"))
        {
            return block.Origin + " · non-BOM encoding";
        }

        var prefix = message.Length > 96 ? message[..96] : message;
        prefix = Regex.Replace(prefix, @"\d+", "#");
        return block.Origin + " · " + prefix.Trim();
    }

    /// <summary>该块是否包含错误/异常信号（用于日志分区的「仅错误」与错误计数）。</summary>
    public static bool IsErrorBlock(CrashLogBlock block)
    {
        var text = block.Text;
        return Contains(text, "error")
            || Contains(text, "exception")
            || Contains(text, "failed")
            || Contains(text, "cannot")
            || Contains(text, "missing")
            || Contains(text, "orphaned")
            || Contains(text, "never set")
            || Contains(text, "not in code")
            || Contains(text, "incomplete")
            || Contains(text, "crash");
    }

    private static string Normalize(string text) =>
        text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').TrimStart('\uFEFF');

    private static string ValueAfterColon(string line)
    {
        int colon = line.IndexOf(':');
        return colon >= 0 ? line[(colon + 1)..].Trim() : string.Empty;
    }

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }

    private static string? ReadString(JsonElement root, string property)
    {
        if (!root.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private static bool Contains(string text, string value) =>
        text.Contains(value, StringComparison.OrdinalIgnoreCase);

    private static DateTimeOffset? TryParseDateTime(string text)
    {
        if (DateTimeOffset.TryParse(
                text,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal,
                out var offset))
        {
            return offset;
        }

        return null;
    }
}
