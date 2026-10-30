namespace PdxLauncher.Core.Storage;

using System.Globalization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 把「整份 JSON 文档」写进自有库（<see cref="PdxDatabase"/>）。
/// <para>
/// <c>settings</c> / <c>session</c> 是单行表（<c>id = 1</c>），其余键都落在 <c>documents</c> 表。
/// 键名约定见 <see cref="JsonDocumentKeys"/>；本类不解析 JSON，只负责存取。
/// </para>
/// </summary>
public sealed class SqliteJsonDocumentStore : IJsonDocumentStore
{
    private const string DocumentsTable = "documents";

    private readonly PdxDatabase _database;
    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    /// <summary>创建文档存储。</summary>
    /// <param name="database">自有库。</param>
    /// <param name="logger">日志。</param>
    /// <param name="clock">时间源（写 <c>updatedAt</c>）。</param>
    public SqliteJsonDocumentStore(PdxDatabase database, IAppLogger logger, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(clock);
        _database = database;
        _logger = logger;
        _clock = clock;
    }

    /// <inheritdoc />
    public bool TryRead(string key, out string? json)
    {
        json = null;
        if (!TryNormalizeKey(key, out var normalized))
        {
            return false;
        }

        try
        {
            using var connection = _database.Open();
            var value = JsonDocumentKeys.IsSingleton(normalized)
                ? connection.ExecuteScalar(
                    "SELECT json FROM " + TableFor(normalized) + " WHERE id = 1")
                : connection.ExecuteScalar("SELECT json FROM documents WHERE key = ?", normalized);
            if (value is not string text)
            {
                return false;
            }

            json = text;
            return true;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("读文档失败（" + normalized + "）：" + exception.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public bool Write(string key, string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        if (!TryNormalizeKey(key, out var normalized))
        {
            return false;
        }

        var updatedAt = _clock.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            _database.Write(connection =>
            {
                if (JsonDocumentKeys.IsSingleton(normalized))
                {
                    var table = TableFor(normalized);
                    connection.Execute(
                        "INSERT INTO " + table + " (id, json, updatedAt) VALUES (1, ?, ?) " +
                        "ON CONFLICT(id) DO UPDATE SET json = excluded.json, updatedAt = excluded.updatedAt",
                        json,
                        updatedAt);
                    return;
                }

                connection.Execute(
                    "INSERT INTO documents (key, json, updatedAt) VALUES (?, ?, ?) " +
                    "ON CONFLICT(key) DO UPDATE SET json = excluded.json, updatedAt = excluded.updatedAt",
                    normalized,
                    json,
                    updatedAt);
            });

            return true;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("写文档失败（" + normalized + "）：" + exception.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public bool Delete(string key)
    {
        if (!TryNormalizeKey(key, out var normalized))
        {
            return false;
        }

        try
        {
            _database.Write(connection =>
            {
                if (JsonDocumentKeys.IsSingleton(normalized))
                {
                    connection.Execute("DELETE FROM " + TableFor(normalized) + " WHERE id = 1");
                    return;
                }

                connection.Execute("DELETE FROM documents WHERE key = ?", normalized);
            });

            return true;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("删文档失败（" + normalized + "）：" + exception.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public bool Exists(string key)
    {
        if (!TryNormalizeKey(key, out var normalized))
        {
            return false;
        }

        try
        {
            using var connection = _database.Open();
            var value = JsonDocumentKeys.IsSingleton(normalized)
                ? connection.ExecuteScalar("SELECT 1 FROM " + TableFor(normalized) + " WHERE id = 1")
                : connection.ExecuteScalar("SELECT 1 FROM documents WHERE key = ?", normalized);
            return value is not null;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("探测文档失败（" + normalized + "）：" + exception.Message);
            return false;
        }
    }

    /// <inheritdoc />
    public long MeasureBytes(string prefix)
    {
        var pattern = BuildLikePattern(prefix);
        try
        {
            using var connection = _database.Open();
            var total = 0L;
            foreach (var row in connection.Query(
                         "SELECT COALESCE(SUM(length(json)), 0) AS bytes FROM documents WHERE key LIKE ? ESCAPE '\\'",
                         pattern))
            {
                total += ReadInt64(row, "bytes");
            }

            // 单行表也要算进来（前缀为空或正好指到 settings/session 前缀时）。
            foreach (var key in SingletonKeysMatching(prefix))
            {
                if (TryRead(key, out var json) && json is not null)
                {
                    total += System.Text.Encoding.UTF8.GetByteCount(json);
                }
            }

            return total;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("统计文档占用失败（" + prefix + "）：" + exception.Message);
            return 0;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> List(string prefix)
    {
        var pattern = BuildLikePattern(prefix);
        try
        {
            using var connection = _database.Open();
            var keys = new List<string>();
            foreach (var row in connection.Query(
                         "SELECT key FROM documents WHERE key LIKE ? ESCAPE '\\' ORDER BY key",
                         pattern))
            {
                if (row.TryGetValue("key", out var value) && value is string key)
                {
                    keys.Add(key);
                }
            }

            keys.AddRange(SingletonKeysMatching(prefix));
            keys.Sort(StringComparer.Ordinal);
            return keys;
        }
        catch (SqliteException exception)
        {
            _logger.Warn("列出文档失败（" + prefix + "）：" + exception.Message);
            return Array.Empty<string>();
        }
    }

    /// <returns><c>settings</c> 或 <c>session</c>；只接受这两个保留键。</returns>
    private static string TableFor(string key) =>
        string.Equals(key, JsonDocumentKeys.Settings, StringComparison.OrdinalIgnoreCase)
            ? JsonDocumentKeys.Settings
            : JsonDocumentKeys.Session;

    private static bool TryNormalizeKey(string? key, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        normalized = key.Trim();
        return true;
    }

    private static IEnumerable<string> SingletonKeysMatching(string? prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim();
        foreach (var key in new[] { JsonDocumentKeys.Settings, JsonDocumentKeys.Session })
        {
            if (normalized.Length == 0 || key.StartsWith(normalized, StringComparison.OrdinalIgnoreCase))
            {
                yield return key;
            }
        }
    }

    private static string BuildLikePattern(string? prefix)
    {
        var normalized = (prefix ?? string.Empty).Trim();
        // LIKE 的通配符要转义，否则前缀里的 % / _ 会误匹配。
        var escaped = normalized
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
        return escaped + "%";
    }

    private static long ReadInt64(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : 0;
}
