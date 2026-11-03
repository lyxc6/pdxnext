namespace PdxLauncher.Core.ModStudio;

using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Parsing;

/// <summary>
/// 从本地 mod 目录读取描述符（只读）。
/// <para>
/// 旧式 <c>.mod</c> 用 <see cref="ClausewitzParser"/> 读标量；它会把 <c>tags={...}</c> 整块跳过，
/// 所以标签单独用正则从原文里取。V3/EU5 的 <c>metadata.json</c> 用 <see cref="JsonDocument"/> 读。
/// </para>
/// </summary>
public static class ModDescriptorReader
{
    private static readonly Regex TagsBlock = new(
        @"(?<![A-Za-z0-9_])tags\s*=\s*\{(?<body>[^}]*)\}",
        RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Quoted = new(
        @"""([^""]*)""",
        RegexOptions.Compiled);

    /// <summary>读一个本地 mod 的描述符；目录不存在或游戏不支持时返回 false。</summary>
    public static bool TryRead(
        GameInstallation game,
        string? dirPath,
        string fallbackName,
        IFileSystem fileSystem,
        out ModDescriptorInfo? info)
    {
        info = null;
        if (string.IsNullOrWhiteSpace(dirPath) || !fileSystem.DirectoryExists(dirPath!))
        {
            return false;
        }

        var family = ModDescriptorRules.FamilyFor(game.GameId);
        if (family == ModDescriptorFamily.None)
        {
            return false;
        }

        var folderName = fileSystem.GetFileName(dirPath!);
        if (string.IsNullOrWhiteSpace(folderName))
        {
            folderName = fallbackName;
        }

        try
        {
            info = family == ModDescriptorFamily.Metadata
                ? ReadMetadata(dirPath!, folderName, fallbackName, fileSystem)
                : ReadLegacy(game, dirPath!, folderName, fallbackName, fileSystem);
            return true;
        }
        catch (Exception)
        {
            // 描述符坏了不应该让整页打不开：退回只显示目录名。
            info = new ModDescriptorInfo(
                family,
                null,
                folderName,
                fallbackName,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                null,
                null);
            return true;
        }
    }

    private static ModDescriptorInfo ReadMetadata(
        string dirPath,
        string folderName,
        string fallbackName,
        IFileSystem fileSystem)
    {
        var metadataDir = fileSystem.Combine(dirPath, ".metadata");
        var descriptorPath = fileSystem.Combine(metadataDir, "metadata.json");
        if (!fileSystem.FileExists(descriptorPath))
        {
            return new ModDescriptorInfo(
                ModDescriptorFamily.Metadata,
                null,
                folderName,
                fallbackName,
                string.Empty,
                string.Empty,
                Array.Empty<string>(),
                null,
                null);
        }

        using var document = JsonDocument.Parse(fileSystem.ReadAllText(descriptorPath));
        var root = document.RootElement;
        var name = GetString(root, "name") ?? fallbackName;
        var version = GetString(root, "version") ?? string.Empty;
        var supported = GetString(root, "supported_game_version") ?? string.Empty;
        var description = GetString(root, "short_description");
        var tags = ReadJsonTags(root);
        var picture = ResolvePicture(fileSystem, metadataDir, GetString(root, "picture"));

        return new ModDescriptorInfo(
            ModDescriptorFamily.Metadata,
            descriptorPath,
            folderName,
            name,
            version,
            supported,
            tags,
            description,
            picture);
    }

    private static ModDescriptorInfo ReadLegacy(
        GameInstallation game,
        string dirPath,
        string folderName,
        string fallbackName,
        IFileSystem fileSystem)
    {
        var modRoot = string.IsNullOrWhiteSpace(game.ModDir)
            ? fileSystem.GetDirectoryName(dirPath) ?? dirPath
            : game.ModDir;
        var descriptorPath = fileSystem.Combine(modRoot, folderName + ".mod");
        if (!fileSystem.FileExists(descriptorPath))
        {
            descriptorPath = FindDescriptorByPath(fileSystem, modRoot, dirPath) ?? descriptorPath;
        }

        var values = fileSystem.FileExists(descriptorPath)
            ? ClausewitzParser.ParseFile(fileSystem, descriptorPath)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var name = GetValue(values, "name") ?? fallbackName;
        var version = GetValue(values, "version") ?? string.Empty;
        var supported = GetValue(values, "supported_version") ?? string.Empty;
        var text = fileSystem.FileExists(descriptorPath) ? fileSystem.ReadAllText(descriptorPath) : string.Empty;
        var tags = ReadClausewitzTags(text);
        var picture = ResolvePicture(fileSystem, dirPath, GetValue(values, "picture"));

        return new ModDescriptorInfo(
            ModDescriptorFamily.Legacy,
            fileSystem.FileExists(descriptorPath) ? descriptorPath : null,
            folderName,
            name,
            version,
            supported,
            tags,
            null,
            picture);
    }

    private static string? FindDescriptorByPath(IFileSystem fileSystem, string modRoot, string dirPath)
    {
        if (!fileSystem.DirectoryExists(modRoot))
        {
            return null;
        }

        var target = Normalize(fileSystem, dirPath);
        foreach (var candidate in fileSystem.EnumerateFiles(modRoot, "*.mod", recursive: false))
        {
            IReadOnlyDictionary<string, string> values;
            try
            {
                values = ClausewitzParser.ParseFile(fileSystem, candidate);
            }
            catch (Exception)
            {
                continue;
            }

            var path = GetValue(values, "path");
            if (!string.IsNullOrWhiteSpace(path)
                && string.Equals(Normalize(fileSystem, path!), target, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string Normalize(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static string? GetValue(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IReadOnlyList<string> ReadJsonTags(JsonElement root)
    {
        if (!root.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Array)
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind == JsonValueKind.String)
            {
                var text = tag.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text!);
                }
            }
            else if (tag.ValueKind == JsonValueKind.Object
                && tag.TryGetProperty("name", out var name)
                && name.ValueKind == JsonValueKind.String)
            {
                var text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text!);
                }
            }
        }

        return result;
    }

    private static IReadOnlyList<string> ReadClausewitzTags(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        var block = TagsBlock.Match(text);
        if (!block.Success)
        {
            return Array.Empty<string>();
        }

        var body = block.Groups["body"].Value;
        var result = new List<string>();
        foreach (Match match in Quoted.Matches(body))
        {
            var value = match.Groups[1].Value;
            if (!string.IsNullOrWhiteSpace(value))
            {
                result.Add(value);
            }
        }

        if (result.Count == 0)
        {
            result.AddRange(body
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                .Where(static part => part != "=" && part != "{")
                .Take(16));
        }

        return result;
    }

    private static string? ResolvePicture(IFileSystem fileSystem, string dirPath, string? picture)
    {
        if (string.IsNullOrWhiteSpace(picture))
        {
            return null;
        }

        var candidates = new[]
        {
            fileSystem.Combine(dirPath, picture!),
            fileSystem.Combine(dirPath, ".metadata", picture!),
        };

        foreach (var candidate in candidates)
        {
            if (fileSystem.FileExists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
