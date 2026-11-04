namespace PdxLauncher.Core.ModStudio;

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 把一次 <see cref="ModDescriptorEdit"/> 写回本地描述符。
/// <para>
/// 只改用户在界面上看到的字段：V3 的 <c>metadata.json</c> 用 <see cref="JsonNode"/> 保留未知键
/// （<c>id</c>、<c>relationships</c>、<c>game_custom_data</c> 等）；CK3 的 <c>.mod</c> 用行级替换，
/// 保留注释、未知键与 <c>path</c>。写入沿用 <see cref="IFileSystem.WriteAllTextAtomic"/>，
/// legacy 双文件任一失败时把已写文件恢复成原文。
/// </para>
/// </summary>
public static class ModDescriptorEditor
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>保存描述符字段；失败返回稳定错误码，不抛异常。</summary>
    public static ModDescriptorEditResult Save(
        ModDescriptorInfo descriptor,
        ModDescriptorEdit edit,
        IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(edit);
        ArgumentNullException.ThrowIfNull(fileSystem);

        if (descriptor.Family == ModDescriptorFamily.None)
        {
            return ModDescriptorEditResult.Fail("unsupported-descriptor");
        }

        var path = descriptor.DescriptorPath;
        if (string.IsNullOrWhiteSpace(path) || !fileSystem.FileExists(path!))
        {
            return ModDescriptorEditResult.Fail("no-descriptor");
        }

        var name = (edit.Name ?? string.Empty).Trim();
        if (name.Length == 0 || name.Contains('\r') || name.Contains('\n'))
        {
            return ModDescriptorEditResult.Fail("invalid-name");
        }

        var version = string.IsNullOrWhiteSpace(edit.Version) ? "0.1.0" : edit.Version.Trim();
        var supported = (edit.SupportedVersion ?? string.Empty).Trim();
        var tags = CleanTags(edit.Tags);
        var description = (edit.Description ?? string.Empty).Trim();

        try
        {
            return descriptor.Family == ModDescriptorFamily.Metadata
                ? SaveMetadata(path!, name, version, supported, tags, description, fileSystem)
                : SaveLegacy(descriptor, path!, name, version, supported, tags, fileSystem);
        }
        catch (Exception exception)
        {
            return ModDescriptorEditResult.Fail("io:" + exception.Message);
        }
    }

    private static ModDescriptorEditResult SaveMetadata(
        string path,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        string description,
        IFileSystem fileSystem)
    {
        var original = fileSystem.ReadAllText(path);
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(
                original,
                documentOptions: new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Disallow,
                });
        }
        catch (JsonException)
        {
            return ModDescriptorEditResult.Fail("invalid-json");
        }

        if (node is not JsonObject root)
        {
            return ModDescriptorEditResult.Fail("invalid-json");
        }

        root["name"] = name;
        root["version"] = version;
        root["supported_game_version"] = supported;
        root["short_description"] = description;

        var array = new JsonArray();
        foreach (var tag in tags)
        {
            array.Add(tag);
        }

        root["tags"] = array;

        // System.Text.Json 的缩进换行跟着平台走（Windows 是 \r\n）；这里统一成 \n，
        // 既与旧式 .mod 的写回一致，也避免同一文件在不同机器上反复整篇改写。
        var updated = root.ToJsonString(JsonOptions).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
        if (!string.Equals(updated, original, StringComparison.Ordinal))
        {
            fileSystem.WriteAllTextAtomic(path, updated);
        }

        return ModDescriptorEditResult.Success(new[] { path });
    }

    private static ModDescriptorEditResult SaveLegacy(
        ModDescriptorInfo descriptor,
        string path,
        string name,
        string version,
        string supported,
        IReadOnlyList<string> tags,
        IFileSystem fileSystem)
    {
        var targets = ResolveLegacyTargets(descriptor, path, fileSystem);
        if (targets.Count == 0)
        {
            return ModDescriptorEditResult.Fail("no-descriptor");
        }

        var pending = new List<(string Path, string Original, string Updated)>(targets.Count);
        foreach (var target in targets)
        {
            var original = fileSystem.ReadAllText(target);
            var updated = LegacyDescriptorText.Apply(original, name, version, supported, tags);
            pending.Add((target, original, updated));
        }

        var written = new List<string>(pending.Count);
        try
        {
            foreach (var item in pending)
            {
                if (string.Equals(item.Original, item.Updated, StringComparison.Ordinal))
                {
                    continue;
                }

                fileSystem.WriteAllTextAtomic(item.Path, item.Updated);
                written.Add(item.Path);
            }
        }
        catch (Exception exception)
        {
            // 原子写本身不会留下半截文件；这里处理「第二个文件失败」时把第一个恢复原样。
            foreach (var item in pending)
            {
                if (!written.Contains(item.Path, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                try
                {
                    fileSystem.WriteAllTextAtomic(item.Path, item.Original);
                }
                catch (Exception)
                {
                    // 回滚失败不能掩盖原始错误；调用方至少能看到 io: 信息。
                }
            }

            return ModDescriptorEditResult.Fail("io:" + exception.Message);
        }

        return ModDescriptorEditResult.Success(written);
    }

    /// <summary>
    /// legacy 描述符通常是一对文件：外层 <c>&lt;modDir&gt;/&lt;folder&gt;.mod</c>（带 path）与
    /// 内层 <c>&lt;folder&gt;/descriptor.mod</c>（不带 path）。只更新真实存在的那几个，不新建。
    /// </summary>
    private static List<string> ResolveLegacyTargets(
        ModDescriptorInfo descriptor,
        string path,
        IFileSystem fileSystem)
    {
        var targets = new List<string>(2);
        var directory = fileSystem.GetDirectoryName(path);
        var fileName = fileSystem.GetFileName(path);
        if (string.IsNullOrWhiteSpace(directory) || string.IsNullOrWhiteSpace(fileName))
        {
            targets.Add(path);
            return targets;
        }

        var outerName = descriptor.FolderName + ".mod";
        if (string.Equals(fileName, outerName, StringComparison.OrdinalIgnoreCase))
        {
            // 外层优先写内层，这样第二个文件写失败时能安全回滚外层。
            var inner = fileSystem.Combine(directory!, descriptor.FolderName, "descriptor.mod");
            if (fileSystem.FileExists(inner))
            {
                targets.Add(inner);
            }

            targets.Add(path);
            return targets;
        }

        if (string.Equals(fileName, "descriptor.mod", StringComparison.OrdinalIgnoreCase))
        {
            targets.Add(path);
            var parent = fileSystem.GetDirectoryName(directory!);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                var outer = fileSystem.Combine(parent!, outerName);
                if (fileSystem.FileExists(outer))
                {
                    targets.Add(outer);
                }
            }

            return targets;
        }

        targets.Add(path);
        return targets;
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
}
