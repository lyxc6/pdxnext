namespace PdxLauncher.Core.Mods.Sorting;

using System.Text;
using System.Text.Json;

/// <summary>
/// 解析 V3 / EU5 的 <c>.metadata/metadata.json</c>：只取排序需要的 <c>id</c>，以及
/// <c>relationships[]</c> 里 <c>rel_type == "dependency"</c> 的目标 id。
/// </summary>
/// <remarks>
/// 坏 JSON 一律返回 <see cref="ModMetadataDescriptor.Empty"/> 而不抛异常：几十个 mod 里混一个
/// 手改坏的描述文件，不应该让整次自动排序预览失败。
/// </remarks>
public static class ModMetadataParser
{
    private const string DependencyRelation = "dependency";

    /// <summary>解析 JSON 文本。</summary>
    public static ModMetadataDescriptor Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        return Parse(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>解析 UTF-8 字节；调用方负责先剥 BOM（见 <c>JsonBytes.WithoutBom</c>）。</summary>
    public static ModMetadataDescriptor Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty)
        {
            return ModMetadataDescriptor.Empty;
        }

        try
        {
            using var document = JsonDocument.Parse(utf8Json);
            return Read(document.RootElement);
        }
        catch (JsonException)
        {
            return ModMetadataDescriptor.Empty;
        }
    }

    private static ModMetadataDescriptor Read(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return ModMetadataDescriptor.Empty;
        }

        var dependencies = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (root.TryGetProperty("relationships", out var relationships) && relationships.ValueKind == JsonValueKind.Array)
        {
            foreach (var relationship in relationships.EnumerateArray())
            {
                if (relationship.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                if (!string.Equals(GetString(relationship, "rel_type"), DependencyRelation, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var target = GetString(relationship, "id");
                if (target is not null && seen.Add(target))
                {
                    dependencies.Add(target);
                }
            }
        }

        return new ModMetadataDescriptor(GetString(root, "id"), dependencies);
    }

    private static string? GetString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
