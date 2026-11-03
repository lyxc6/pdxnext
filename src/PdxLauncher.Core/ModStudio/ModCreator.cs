namespace PdxLauncher.Core.ModStudio;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

/// <summary>
/// 在游戏数据目录的 <c>mod</c> 下生成一款可被游戏识别的本地 mod。
/// <para>
/// 只写描述符和默认封面，不生成任何游戏内容；已存在的目录一律不覆盖。
/// </para>
/// </summary>
public static class ModCreator
{
    private static readonly Regex FolderPattern = new(
        "^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>创建 mod；失败返回稳定错误码，不抛异常。</summary>
    public static ModCreateResult Create(GameInstallation game, ModCreateRequest request, IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var family = ModDescriptorRules.FamilyFor(game.GameId);
        if (family == ModDescriptorFamily.None)
        {
            return ModCreateResult.Fail("unsupported-game");
        }

        if (string.IsNullOrWhiteSpace(game.ModDir))
        {
            return ModCreateResult.Fail("no-mod-dir");
        }

        var name = (request.Name ?? string.Empty).Trim();
        var folder = (request.FolderName ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return ModCreateResult.Fail("invalid-name");
        }

        if (!FolderPattern.IsMatch(folder))
        {
            return ModCreateResult.Fail("invalid-folder");
        }

        string target;
        try
        {
            target = fileSystem.Combine(game.ModDir, folder);
            if (fileSystem.DirectoryExists(target))
            {
                return ModCreateResult.Fail("folder-exists");
            }

            if (target.Length > 240)
            {
                return ModCreateResult.Fail("path-too-long");
            }

            if (GamePathRules.RequiresAsciiPaths(game.GameId) && !IsAscii(target))
            {
                return ModCreateResult.Fail("ascii-required");
            }
        }
        catch (Exception exception)
        {
            return ModCreateResult.Fail("io:" + exception.Message);
        }

        var version = string.IsNullOrWhiteSpace(request.Version) ? "0.1.0" : request.Version.Trim();
        var supported = (request.SupportedVersion ?? string.Empty).Trim();
        var tags = CleanTags(request.Tags);
        var description = (request.Description ?? string.Empty).Trim();

        try
        {
            fileSystem.EnsureDirectory(target);
            return family == ModDescriptorFamily.Metadata
                ? CreateMetadata(target, name, version, supported, tags, description, request.PreviewSourcePath, fileSystem)
                : CreateLegacy(target, name, version, supported, tags, request.PreviewSourcePath, fileSystem);
        }
        catch (Exception exception)
        {
            TryCleanup(target, family, fileSystem);
            return ModCreateResult.Fail("io:" + exception.Message);
        }
    }

    private static ModCreateResult CreateLegacy(
        string target,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        string? previewSource,
        IFileSystem fileSystem)
    {
        var descriptor = fileSystem.Combine(target, "descriptor.mod");
        var outer = fileSystem.Combine(fileSystem.GetDirectoryName(target) ?? target, fileSystem.GetFileName(target) + ".mod");
        var thumbnail = fileSystem.Combine(target, "thumbnail.png");

        WritePreview(previewSource, thumbnail, fileSystem);

        var path = fileSystem.GetFullPath(target).Replace('\\', '/');
        var common = new List<string>
        {
            "version=" + Quote(version),
            "tags={" + string.Join(" ", tags.Select(Quote)) + "}",
            "name=" + Quote(name),
            "picture=" + Quote("thumbnail.png"),
            "supported_version=" + Quote(supported),
        };

        fileSystem.WriteAllTextAtomic(descriptor, string.Join("\n", common) + "\n");

        var outerLines = new List<string>(common)
        {
            "path=" + Quote(path),
        };
        fileSystem.WriteAllTextAtomic(outer, string.Join("\n", outerLines) + "\n");

        return ModCreateResult.Success(target);
    }

    private static ModCreateResult CreateMetadata(
        string target,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        string description,
        string? previewSource,
        IFileSystem fileSystem)
    {
        var metadataDir = fileSystem.Combine(target, ".metadata");
        fileSystem.EnsureDirectory(metadataDir);
        var descriptor = fileSystem.Combine(metadataDir, "metadata.json");
        var thumbnail = fileSystem.Combine(metadataDir, "thumbnail.png");
        WritePreview(previewSource, thumbnail, fileSystem);

        var payload = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["id"] = string.Empty,
            ["version"] = version,
            ["supported_game_version"] = supported,
            ["short_description"] = description,
            ["picture"] = "thumbnail.png",
            ["tags"] = tags,
            ["relationships"] = Array.Empty<object>(),
            ["game_custom_data"] = new Dictionary<string, object?>
            {
                ["multiplayer_synchronized"] = true,
            },
        };

        fileSystem.WriteAllTextAtomic(descriptor, JsonSerializer.Serialize(payload, JsonOptions) + "\n");
        return ModCreateResult.Success(target);
    }

    private static void WritePreview(string? source, string destination, IFileSystem fileSystem)
    {
        if (!string.IsNullOrWhiteSpace(source) && fileSystem.FileExists(source!))
        {
            fileSystem.WriteAllBytesAtomic(destination, fileSystem.ReadAllBytes(source!));
            return;
        }

        // 没选封面时写启动器自带的图标，而不是 1×1 透明图 —— 后者在 Steam 上等于没有封面。
        ModCoverAssets.WriteDefault(fileSystem, destination);
    }

    private static IReadOnlyList<string> CleanTags(IReadOnlyList<string>? tags)
    {
        if (tags is null || tags.Count == 0)
        {
            return Array.Empty<string>();
        }

        return tags
            .Select(static tag => (tag ?? string.Empty).Trim())
            .Where(static tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(16)
            .ToArray();
    }

    private static string Quote(string value) =>
        "\"" + value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal) + "\"";

    private static bool IsAscii(string value)
    {
        foreach (var character in value)
        {
            if (character > 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryCleanup(string target, ModDescriptorFamily family, IFileSystem fileSystem)
    {
        try
        {
            if (family == ModDescriptorFamily.Legacy)
            {
                var outer = fileSystem.Combine(
                    fileSystem.GetDirectoryName(target) ?? target,
                    fileSystem.GetFileName(target) + ".mod");
                SafeDelete(outer, fileSystem);
                SafeDelete(fileSystem.Combine(target, "descriptor.mod"), fileSystem);
                SafeDelete(fileSystem.Combine(target, "thumbnail.png"), fileSystem);
            }
            else
            {
                SafeDelete(fileSystem.Combine(target, ".metadata", "metadata.json"), fileSystem);
                SafeDelete(fileSystem.Combine(target, ".metadata", "thumbnail.png"), fileSystem);
            }
        }
        catch (Exception)
        {
            // 清理失败不掩盖原始错误。
        }
    }

    private static void SafeDelete(string path, IFileSystem fileSystem)
    {
        if (fileSystem.FileExists(path))
        {
            fileSystem.Delete(path);
        }
    }
}
