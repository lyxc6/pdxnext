namespace PdxLauncher.Core.ModStudio;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Storage;

/// <summary>
/// 「本地 mod → 创意工坊条目」记录的持久化。
/// <para>
/// 键里带路径哈希而不是 ModId：ModId 对未登记 mod 是稳定派生值，但换目录后仍会变；
/// 路径哈希能跟上目录搬家，同时避免把长中文路径直接塞进键。
/// </para>
/// </summary>
public sealed class ModPublicationStore
{
    private const string Prefix = "modstudio.publish";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly IJsonDocumentStore _documents;

    /// <summary>创建存储。</summary>
    public ModPublicationStore(IJsonDocumentStore documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <summary>读一条记录；没有或 JSON 坏掉返回 null。</summary>
    public ModPublication? TryGet(string gameId, string modPath)
    {
        if (!_documents.TryRead(KeyFor(gameId, modPath), out var json) || string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ModPublication>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>写一条记录。</summary>
    public bool Save(ModPublication publication)
    {
        ArgumentNullException.ThrowIfNull(publication);
        var json = JsonSerializer.Serialize(publication, JsonOptions);
        return _documents.Write(KeyFor(publication.GameId, publication.ModPath), json);
    }

    /// <summary>删一条记录。</summary>
    public bool Delete(string gameId, string modPath) =>
        _documents.Delete(KeyFor(gameId, modPath));

    /// <summary>列出某款游戏已有的所有记录（读不出的条目跳过）。</summary>
    public IReadOnlyList<ModPublication> List(string gameId)
    {
        var prefix = Prefix + "." + gameId + ".";
        var result = new List<ModPublication>();
        foreach (var key in _documents.List(prefix))
        {
            if (!_documents.TryRead(key, out var json) || string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            try
            {
                var publication = JsonSerializer.Deserialize<ModPublication>(json!, JsonOptions);
                if (publication is not null)
                {
                    result.Add(publication);
                }
            }
            catch (JsonException)
            {
                // 单条坏记录不影响整页。
            }
        }

        return result;
    }

    private static string KeyFor(string gameId, string modPath)
    {
        var normalized = (modPath ?? string.Empty)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToLowerInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        return Prefix + "." + gameId + "." + Convert.ToHexString(hash)[..16].ToLowerInvariant();
    }
}
