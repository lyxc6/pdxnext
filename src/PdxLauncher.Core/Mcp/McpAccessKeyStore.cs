namespace PdxLauncher.Core.Mcp;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using PdxLauncher.Core.Storage;

/// <summary>
/// MCP 本地访问密钥存储。明文只在创建/轮换后返回一次；SQLite 只保存 SHA-256 哈希与前缀。
/// </summary>
public sealed class McpAccessKeyStore
{
    private const string TokenPrefix = "pdxmcp_";

    private readonly PdxDatabase _database;

    public McpAccessKeyStore(PdxDatabase database) =>
        _database = database ?? throw new ArgumentNullException(nameof(database));

    /// <summary>读取全部密钥摘要。</summary>
    public IReadOnlyList<McpAccessKey> List()
    {
        using var connection = _database.Open();
        return connection.Query(
            "SELECT id, display_name, key_prefix, enabled, allowed_tools, expires_utc, created_utc, last_used_utc "
                + "FROM mcp_access_keys ORDER BY created_utc DESC, id DESC",
            Project);
    }

    /// <summary>创建密钥；原始 token 只通过返回值暴露一次。</summary>
    public McpCreatedAccessKey Create(
        string displayName,
        IReadOnlyList<string> capabilities,
        DateTimeOffset? expiresUtc)
    {
        var id = Guid.NewGuid().ToString("N");
        var secret = CreateSecret();
        var token = TokenPrefix + id + "_" + secret;
        var key = new McpAccessKey(
            id,
            NormalizeName(displayName),
            TokenPrefix + id[..8],
            true,
            NormalizeCapabilities(capabilities),
            expiresUtc,
            DateTimeOffset.UtcNow,
            null);

        _database.Write(connection => connection.Execute(
            "INSERT INTO mcp_access_keys "
                + "(id, display_name, key_hash, key_prefix, enabled, allowed_tools, expires_utc, created_utc, last_used_utc) "
                + "VALUES (?, ?, ?, ?, 1, ?, ?, ?, NULL)",
            key.Id,
            key.DisplayName,
            HashToken(token),
            key.KeyPrefix,
            key.Capabilities,
            key.ExpiresUtc?.ToUnixTimeMilliseconds(),
            key.CreatedUtc.ToUnixTimeMilliseconds()));

        return new McpCreatedAccessKey(key, token);
    }

    /// <summary>按 token 校验授权；失败返回 null。</summary>
    public McpAuthorizedKey? Authenticate(string rawToken)
    {
        if (!TryParse(rawToken, out var id, out var secret))
        {
            return null;
        }

        string? hash;
        using (var connection = _database.Open())
        {
            hash = connection.ExecuteScalar(
                "SELECT key_hash FROM mcp_access_keys WHERE id = ? AND enabled = 1 "
                    + "AND (expires_utc IS NULL OR expires_utc > ?)",
                id,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()) as string;
        }

        if (hash is null || !FixedTimeEquals(hash, HashToken(rawToken)))
        {
            return null;
        }

        string displayName;
        string capabilities;
        using (var connection = _database.Open())
        {
            var row = connection.Query(
                "SELECT display_name, allowed_tools FROM mcp_access_keys WHERE id = ?",
                id).FirstOrDefault();
            if (row is null)
            {
                return null;
            }

            displayName = row["display_name"] as string ?? string.Empty;
            capabilities = row["allowed_tools"] as string ?? string.Empty;

            connection.Execute(
                "UPDATE mcp_access_keys SET last_used_utc = ? WHERE id = ?",
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                id);
        }

        var normalized = NormalizeCapabilities(
            capabilities.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return new McpAuthorizedKey(
            id,
            displayName,
            normalized.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    /// <summary>轮换密钥；原始 token 只通过返回值暴露一次。</summary>
    public McpCreatedAccessKey? Rotate(string id)
    {
        var key = Find(id);
        if (key is null)
        {
            return null;
        }

        var secret = CreateSecret();
        var token = TokenPrefix + id + "_" + secret;
        _database.Write(connection => connection.Execute(
            "UPDATE mcp_access_keys SET key_hash = ?, key_prefix = ?, last_used_utc = NULL WHERE id = ?",
            HashToken(token),
            TokenPrefix + id[..8],
            id));

        return new McpCreatedAccessKey(
            key with { KeyPrefix = TokenPrefix + id[..8], LastUsedUtc = null },
            token);
    }

    public bool SetEnabled(string id, bool enabled) =>
        UpdateField(id, "enabled", enabled ? 1 : 0);

    public bool UpdateCapabilities(string id, IReadOnlyList<string> capabilities) =>
        UpdateField(id, "allowed_tools", NormalizeCapabilities(capabilities));

    public bool UpdateExpiry(string id, DateTimeOffset? expiresUtc) =>
        UpdateField(id, "expires_utc", expiresUtc?.ToUnixTimeMilliseconds());

    public bool Rename(string id, string displayName) =>
        UpdateField(id, "display_name", NormalizeName(displayName));

    public bool Delete(string id) =>
        _database.Write(connection =>
            connection.Execute("DELETE FROM mcp_access_keys WHERE id = ?", id) > 0);

    private McpAccessKey? Find(string id)
    {
        using var connection = _database.Open();
        return connection.Query(
            "SELECT id, display_name, key_prefix, enabled, allowed_tools, expires_utc, created_utc, last_used_utc "
                + "FROM mcp_access_keys WHERE id = ?",
            Project,
            id).FirstOrDefault();
    }

    private bool UpdateField(string id, string field, object? value)
    {
        var allowed = field is "enabled" or "allowed_tools" or "expires_utc" or "display_name";
        if (!allowed)
        {
            throw new ArgumentException("字段不在白名单里：" + field, nameof(field));
        }

        return _database.Write(connection =>
            connection.Execute($"UPDATE mcp_access_keys SET {field} = ? WHERE id = ?", value, id) > 0);
    }

    private static string CreateSecret()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static string HashToken(string token) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);
        return CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    private static bool TryParse(string token, out string id, out string secret)
    {
        id = string.Empty;
        secret = string.Empty;
        if (string.IsNullOrWhiteSpace(token) || !token.StartsWith(TokenPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = token[TokenPrefix.Length..];
        var separator = rest.IndexOf('_');
        if (separator <= 0 || separator == rest.Length - 1)
        {
            return false;
        }

        id = rest[..separator];
        secret = rest[(separator + 1)..];
        return Guid.TryParseExact(id, "N", out _);
    }

    private static string NormalizeName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            throw new ArgumentException("密钥名称不能为空", nameof(displayName));
        }

        var normalized = displayName.Trim();
        return normalized.Length > 80 ? normalized[..80] : normalized;
    }

    /// <summary>
    /// 统一写入口：库里的旧作用域/工具名先归一到能力，再按能力顺序落库；
    /// 空选择写成空串，表示「没有任何能力」，不再偷偷升级成全部。
    /// </summary>
    private static string NormalizeCapabilities(IReadOnlyList<string>? capabilities) =>
        McpCapabilityCatalog.ToStorage(
            McpCapabilityCatalog.Normalize(
                (capabilities ?? Array.Empty<string>())
                    .Where(static value => !string.IsNullOrWhiteSpace(value))
                    .Select(static value => value.Trim())
                    .ToArray()));

    private static McpAccessKey Project(IReadOnlyDictionary<string, object?> row) =>
        new(
            row["id"] as string ?? string.Empty,
            row["display_name"] as string ?? string.Empty,
            row["key_prefix"] as string ?? string.Empty,
            ReadInt64(row, "enabled") != 0,
            NormalizeCapabilities(
                (row["allowed_tools"] as string ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)),
            ReadNullableDateTime(row, "expires_utc"),
            ReadDateTime(row, "created_utc"),
            ReadNullableDateTime(row, "last_used_utc"));

    private static DateTimeOffset ReadDateTime(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = ReadInt64(row, key);
        return value <= 0 ? DateTimeOffset.MinValue : DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    private static DateTimeOffset? ReadNullableDateTime(IReadOnlyDictionary<string, object?> row, string key)
    {
        var value = ReadInt64(row, key);
        return value <= 0 ? null : DateTimeOffset.FromUnixTimeMilliseconds(value);
    }

    private static long ReadInt64(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is long number ? number : 0L;
}
