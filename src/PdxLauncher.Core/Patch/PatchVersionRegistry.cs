namespace PdxLauncher.Core.Patch;

using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using PdxLauncher.Core.Games;

/// <summary>当前「原版 steam_api」在已知版本登记表里的匹配情况。</summary>
public enum PatchVersionState
{
    /// <summary>没读到文件，没做判断（例如未检测到安装）。</summary>
    NotChecked,

    /// <summary>命中登记表：游戏、版本、buildid、大小与 sha256 都能对上。</summary>
    Known,

    /// <summary>不在登记表里（未知版本或未知哈希）：打补丁前需要用户确认。</summary>
    Unknown,
}

/// <summary>一条已知 Steam 原版登记：哪个游戏、哪个版本/buildid、原版文件名、sha256 与字节数。</summary>
public sealed record KnownApiVersion(string GameId, string Version, string BuildId, string FileName, string Sha256, long Size);

/// <summary>
/// 「已知原版 DLL 哈希 + build id」登记表（内嵌资源 <c>Patch/Assets/known-versions.json</c>）。
/// <para>
/// 用途：打补丁前判断目标 <c>steam_api*.dll</c> 是不是官方原版。命中说明版本已知（可以正常打），
/// 未命中说明版本未验证（UI 提示后允许继续）。清单只登记本机能采集到的真实条目，采不到的不猜。
/// </para>
/// </summary>
public sealed class PatchVersionRegistry
{
    /// <summary>内嵌资源逻辑名，必须与 csproj 的 LogicalName 一致。</summary>
    public const string EmbeddedResource = "PdxLauncher.Patch.known-versions.json";

    private static readonly Lazy<PatchVersionRegistry> LazyDefault = new(LoadEmbedded, LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly IReadOnlyList<KnownApiVersion> _entries;

    private PatchVersionRegistry(IReadOnlyList<KnownApiVersion> entries, string? loadError)
    {
        _entries = entries;
        LoadError = loadError;
    }

    /// <summary>内嵌登记表（只读、进程内缓存）。</summary>
    public static PatchVersionRegistry Default => LazyDefault.Value;

    /// <summary>空登记表：任何文件都判为未知版本。</summary>
    public static PatchVersionRegistry Empty { get; } = new(Array.Empty<KnownApiVersion>(), null);

    /// <summary>全部登记条目。</summary>
    public IReadOnlyList<KnownApiVersion> Entries => _entries;

    /// <summary>加载内嵌资源失败时的原因；正常为 null。</summary>
    public string? LoadError { get; }

    /// <summary>解析登记表 JSON；结构不合法时抛 <see cref="InvalidDataException"/>。</summary>
    public static PatchVersionRegistry FromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("known-versions.json is empty.");
        }

        RegistryDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<RegistryDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("known-versions.json is not valid JSON: " + ex.Message, ex);
        }

        if (dto is null)
        {
            throw new InvalidDataException("known-versions.json is null.");
        }

        var entries = new List<KnownApiVersion>();
        foreach (var entry in dto.Entries ?? new List<EntryDto>())
        {
            var gameId = entry.GameId ?? string.Empty;
            var fileName = entry.FileName ?? string.Empty;
            var sha256 = (entry.Sha256 ?? string.Empty).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(gameId))
            {
                throw new InvalidDataException("known-versions.json: an entry is missing gameId.");
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                throw new InvalidDataException("known-versions.json: " + gameId + " is missing fileName.");
            }

            if (sha256.Length != 64 || !IsHex(sha256))
            {
                throw new InvalidDataException("known-versions.json: " + gameId + " has an invalid sha256.");
            }

            if (entry.Size <= 0)
            {
                throw new InvalidDataException("known-versions.json: " + gameId + " has an invalid size.");
            }

            entries.Add(new KnownApiVersion(gameId, entry.Version ?? string.Empty, entry.BuildId ?? string.Empty, fileName, sha256, entry.Size));
        }

        if (entries.Count == 0)
        {
            throw new InvalidDataException("known-versions.json has no entries.");
        }

        return new PatchVersionRegistry(entries, null);
    }

    /// <summary>
    /// 找当前游戏对应的一条登记；文件名、字节数、sha256 全对上才算命中（gameId 先做正名归一）。
    /// </summary>
    public KnownApiVersion? Match(string? gameId, string fileName, ReadOnlySpan<byte> data)
    {
        var canonical = KnownGames.CanonicalizeGameId(gameId);
        var sha256 = Hash(data);
        foreach (var entry in _entries)
        {
            if (!string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                || entry.Size != data.Length
                || !string.Equals(entry.Sha256, sha256, StringComparison.Ordinal))
            {
                continue;
            }

            if (string.Equals(KnownGames.CanonicalizeGameId(entry.GameId), canonical, StringComparison.Ordinal))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>这个文件是否是任意游戏登记过的官方原版（用于判断 <c>_o</c> 备份的来源）。</summary>
    public bool IsKnownOriginal(string fileName, ReadOnlySpan<byte> data)
    {
        var sha256 = Hash(data);
        foreach (var entry in _entries)
        {
            if (string.Equals(entry.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                && entry.Size == data.Length
                && string.Equals(entry.Sha256, sha256, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
            {
                return false;
            }
        }

        return true;
    }

    private static string Hash(ReadOnlySpan<byte> data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static PatchVersionRegistry LoadEmbedded()
    {
        try
        {
            using var stream = typeof(PatchVersionRegistry).Assembly.GetManifestResourceStream(EmbeddedResource);
            if (stream is null)
            {
                return new PatchVersionRegistry(Array.Empty<KnownApiVersion>(), "embedded resource " + EmbeddedResource + " is missing");
            }

            using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            return FromJson(reader.ReadToEnd());
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or JsonException)
        {
            return new PatchVersionRegistry(Array.Empty<KnownApiVersion>(), ex.Message);
        }
    }

    private sealed class RegistryDto
    {
        public int Schema { get; set; }

        public string? Updated { get; set; }

        public List<EntryDto>? Entries { get; set; }
    }

    private sealed class EntryDto
    {
        public string? GameId { get; set; }

        public string? Version { get; set; }

        public string? BuildId { get; set; }

        public string? FileName { get; set; }

        public string? Sha256 { get; set; }

        public long Size { get; set; }
    }
}
