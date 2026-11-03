namespace PdxLauncher.Core.Mods.Sorting;

using System.Text.RegularExpressions;

/// <summary>descriptor 依赖解析器：从 mod descriptor 文本里提取依赖的 Steam 工坊 ID。</summary>
public static class DescriptorDependencyParser
{
    private static readonly Regex DependenciesBlockRegex = new(
        @"dependencies\s*=\s*\{([^}]*)\}",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    private static readonly Regex WorkshopIdRegex = new(
        @"workshop_item_id\s*=\s*(\d+)",
        RegexOptions.CultureInvariant);

    /// <summary>
    /// 解析 dependencies = { "workshop_item_id = 123" } 块（引号/无引号/多行皆可），
    /// 返回工坊 ID 列表（按出现顺序去重）。无依赖块或解析不到时返回空列表。
    /// </summary>
    public static IReadOnlyList<string> ParseSteamIds(string? descriptorText)
    {
        var text = descriptorText ?? string.Empty;
        if (text.Trim().Length == 0)
        {
            return Array.Empty<string>();
        }

        var blockMatch = DependenciesBlockRegex.Match(text);
        if (!blockMatch.Success)
        {
            return Array.Empty<string>();
        }

        var ids = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in WorkshopIdRegex.Matches(blockMatch.Groups[1].Value))
        {
            var id = match.Groups[1].Value;
            if (seen.Add(id))
            {
                ids.Add(id);
            }
        }

        return ids;
    }
}
