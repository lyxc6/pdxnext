namespace PdxLauncher.Core.Patch;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Games;

/// <summary>清单里的一条 DLC：Steam appId + 显示名（渲染成 ini 里的 <c>id = name</c>）。</summary>
public sealed record DlcEntry(int SteamAppId, string Name);

/// <summary>
/// 一个游戏的 DLC 分组：对应 ini 里的一条 <c>; 注释</c> 加若干 <c>id = name</c>。
/// <para><see cref="Comment"/> 是分组注释（不含前导分号），<see cref="SteamAppId"/> 是该游戏本体 appId。</para>
/// </summary>
public sealed record DlcSource(string GameId, string Name, string Comment, int SteamAppId, IReadOnlyList<DlcEntry> Dlc);

/// <summary>
/// 按游戏分节的 DLC 源清单（<c>Patch/Assets/dlc-catalog.json</c>）。
/// <para>
/// 单一 490 条的大表容易在新增一行时错位，所以源数据按游戏分节维护，运行时用
/// <see cref="DlcLines"/> 拼成 CreamAPI 要求的单一 <c>[dlc]</c> 段。渲染顺序就是清单里的顺序，
/// 因此升级前后生成的 ini 语义一致（分组注释与条目内容不变）。
/// </para>
/// </summary>
public sealed class PatchDlcCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private readonly IReadOnlyList<DlcSource> _sources;

    private PatchDlcCatalog(int schema, string updated, IReadOnlyList<DlcSource> sources)
    {
        Schema = schema;
        Updated = updated;
        _sources = sources;
    }

    /// <summary>空清单（素材缺失时占位，调用方应先看 TryGetDlcCatalog 的返回值）。</summary>
    public static PatchDlcCatalog Empty { get; } = new(0, string.Empty, Array.Empty<DlcSource>());

    /// <summary>清单格式版本。</summary>
    public int Schema { get; }

    /// <summary>清单最后更新日期（<c>yyyy-MM-dd</c>）。</summary>
    public string Updated { get; }

    /// <summary>按 ini 里的分组顺序排列的游戏节。</summary>
    public IReadOnlyList<DlcSource> Sources => _sources;

    /// <summary>全部 DLC 条目数（不是游戏节数）。</summary>
    public int Count => _sources.Sum(source => source.Dlc.Count);

    /// <summary>解析清单 JSON；格式不合法时抛 <see cref="InvalidDataException"/>。</summary>
    public static PatchDlcCatalog Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("DLC catalog is empty.");
        }

        CatalogDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CatalogDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("DLC catalog is not valid JSON: " + ex.Message, ex);
        }

        if (dto is null)
        {
            throw new InvalidDataException("DLC catalog is null.");
        }

        var sources = new List<DlcSource>();
        foreach (var source in dto.Sources ?? new List<SourceDto>())
        {
            var gameId = source.GameId ?? string.Empty;
            var name = string.IsNullOrWhiteSpace(source.Name) ? gameId : source.Name;
            var comment = string.IsNullOrWhiteSpace(source.Comment) ? name : source.Comment;
            var entries = new List<DlcEntry>();
            foreach (var entry in source.Dlc ?? new List<EntryDto>())
            {
                entries.Add(new DlcEntry(entry.Id, entry.Name ?? string.Empty));
            }

            sources.Add(new DlcSource(gameId, name, comment, source.SteamAppId, entries));
        }

        return new PatchDlcCatalog(dto.Schema, dto.Updated ?? string.Empty, sources);
    }

    /// <summary>
    /// 校验清单：id 必须是正整数、同一 id 不能出现在两个游戏名下（同游戏内也不能重复）、
    /// 每个游戏节必须有注释与至少一条 DLC。返回可读的问题列表，空表示通过。
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var problems = new List<string>();
        if (_sources.Count == 0)
        {
            problems.Add("catalog has no game sections");
            return problems;
        }

        var owners = new Dictionary<int, string>();
        foreach (var source in _sources)
        {
            if (string.IsNullOrWhiteSpace(source.GameId))
            {
                problems.Add("a game section is missing gameId");
                continue;
            }

            if (source.SteamAppId <= 0)
            {
                problems.Add(source.GameId + ": steamAppId must be a positive integer");
            }

            if (string.IsNullOrWhiteSpace(source.Comment))
            {
                problems.Add(source.GameId + ": comment is empty");
            }

            if (source.Dlc.Count == 0)
            {
                problems.Add(source.GameId + ": has no DLC entries");
            }

            var local = new HashSet<int>();
            foreach (var entry in source.Dlc)
            {
                var id = entry.SteamAppId.ToString(CultureInfo.InvariantCulture);
                if (entry.SteamAppId <= 0)
                {
                    problems.Add(source.GameId + ": dlc id " + id + " is not a positive integer");
                }

                if (string.IsNullOrWhiteSpace(entry.Name))
                {
                    problems.Add(source.GameId + ": dlc id " + id + " has no name");
                }

                if (!local.Add(entry.SteamAppId))
                {
                    problems.Add(source.GameId + ": dlc id " + id + " is duplicated");
                }

                if (owners.TryGetValue(entry.SteamAppId, out var owner))
                {
                    if (!string.Equals(owner, source.GameId, StringComparison.Ordinal))
                    {
                        problems.Add("dlc id " + id + " is listed under both " + owner + " and " + source.GameId);
                    }
                }
                else
                {
                    owners.Add(entry.SteamAppId, source.GameId);
                }
            }
        }

        return problems;
    }

    /// <summary>取某个游戏的 DLC 条目（gameId 先做正名归一，例如 cities_v2 → cities_skylines）。</summary>
    public IReadOnlyList<DlcEntry> ForGame(string? gameId)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        foreach (var source in _sources)
        {
            if (string.Equals(KnownGames.CanonicalizeGameId(source.GameId), canonical, StringComparison.Ordinal))
            {
                return source.Dlc;
            }
        }

        return Array.Empty<DlcEntry>();
    }

    /// <summary>按 ini 顺序渲染 [dlc] 段的内容行（每行不含换行符，含分组注释行）。</summary>
    public IEnumerable<string> DlcLines()
    {
        foreach (var source in _sources)
        {
            yield return "; " + source.Comment;
            foreach (var entry in source.Dlc)
            {
                yield return entry.SteamAppId.ToString(CultureInfo.InvariantCulture) + " = " + entry.Name;
            }
        }
    }

    private sealed class CatalogDto
    {
        public int Schema { get; set; }

        public string? Updated { get; set; }

        public List<SourceDto>? Sources { get; set; }
    }

    private sealed class SourceDto
    {
        public string? GameId { get; set; }

        public string? Name { get; set; }

        public string? Comment { get; set; }

        public int SteamAppId { get; set; }

        public string? SourceUrl { get; set; }

        public List<EntryDto>? Dlc { get; set; }
    }

    private sealed class EntryDto
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? SourceUrl { get; set; }
    }
}
