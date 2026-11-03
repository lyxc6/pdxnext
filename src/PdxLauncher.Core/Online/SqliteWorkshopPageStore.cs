namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// <see cref="IWorkshopPageStore"/> 的 SQLite 实现：数据落在自有库的 <c>workshop_pages</c> 表。
/// <para>
/// 一页一个 publishedfileid；<c>payload</c> 存解析结果整段 JSON，<c>fetched_utc</c> 单独成列用于
/// TTL 与容量淘汰。写一条只动一行，不再整份重写索引。
/// </para>
/// </summary>
public sealed class SqliteWorkshopPageStore : IWorkshopPageStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly PdxDatabase _database;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="database">自有库。</param>
    /// <param name="logger">日志。</param>
    public SqliteWorkshopPageStore(PdxDatabase database, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(logger);
        _database = database;
        _logger = logger;
    }

    /// <inheritdoc />
    public int Count
    {
        get
        {
            try
            {
                using var connection = _database.Open();
                var value = connection.ExecuteScalar("SELECT COUNT(*) FROM workshop_pages");
                return value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _logger.Warn("读取创意工坊条目页缓存数失败：" + exception.Message);
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public CachedSteamItemPage? Find(string publishedFileId)
    {
        if (string.IsNullOrWhiteSpace(publishedFileId))
        {
            return null;
        }

        try
        {
            using var connection = _database.Open();
            var rows = connection.Query(
                "SELECT payload, fetched_utc FROM workshop_pages WHERE published_file_id = ? LIMIT 1",
                publishedFileId);
            if (rows.Count == 0)
            {
                return null;
            }

            var payload = rows[0]["payload"] as string;
            if (string.IsNullOrWhiteSpace(payload))
            {
                return null;
            }

            var entry = JsonSerializer.Deserialize<CachedSteamItemPage>(payload, JsonOptions);
            if (entry is null)
            {
                return null;
            }

            if (rows[0].TryGetValue("fetched_utc", out var stamp) && stamp is not null)
            {
                entry.FetchedUtc = DateTimeOffset.FromUnixTimeMilliseconds(
                    Convert.ToInt64(stamp, CultureInfo.InvariantCulture));
            }

            entry.ImageUrls ??= [];
            entry.ScreenshotUrls ??= [];
            entry.Comments ??= [];
            return entry;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("读取创意工坊条目页缓存失败：" + exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public void Set(string publishedFileId, CachedSteamItemPage entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publishedFileId);
        ArgumentNullException.ThrowIfNull(entry);

        try
        {
            var payload = JsonSerializer.Serialize(entry, JsonOptions);
            _database.Write(connection => connection.Execute(
                "INSERT INTO workshop_pages (published_file_id, fetched_utc, payload) VALUES (?, ?, ?) " +
                "ON CONFLICT(published_file_id) DO UPDATE SET " +
                "fetched_utc = excluded.fetched_utc, payload = excluded.payload",
                publishedFileId,
                entry.FetchedUtc.ToUnixTimeMilliseconds(),
                payload));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("写入创意工坊条目页缓存失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            _database.Write(connection => connection.Execute("DELETE FROM workshop_pages"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("清空创意工坊条目页缓存失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public void Trim(int maxEntries)
    {
        if (maxEntries <= 0)
        {
            return;
        }

        try
        {
            _database.Write(connection =>
            {
                var value = connection.ExecuteScalar("SELECT COUNT(*) FROM workshop_pages");
                var count = value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
                var overflow = count - maxEntries;
                if (overflow <= 0)
                {
                    return 0;
                }

                return connection.Execute(
                    "DELETE FROM workshop_pages WHERE published_file_id IN (" +
                    "SELECT published_file_id FROM workshop_pages ORDER BY fetched_utc ASC LIMIT ?)",
                    overflow);
            });
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("淘汰创意工坊条目页缓存失败：" + exception.Message);
        }
    }

    /// <summary>
    /// 把 v1 遗留的 <c>documents</c> JSON 导入真表；由 <see cref="PdxDatabase"/> 的迁移流程调用。
    /// 解析失败只记 Warn 并返回 false，调用方保留旧行下次再试。
    /// </summary>
    /// <param name="connection">已打开的连接（调用方负责事务与提交）。</param>
    /// <param name="json">旧索引 JSON。</param>
    /// <param name="maxEntries">导入后保留上限。</param>
    internal bool Import(SqliteConnection connection, string json, int maxEntries)
    {
        LegacyIndex? file;
        try
        {
            file = JsonSerializer.Deserialize<LegacyIndex>(json, JsonOptions);
        }
        catch (JsonException exception)
        {
            _logger.Warn("旧创意工坊分页缓存无法解析，跳过导入：" + exception.Message);
            return false;
        }

        if (file?.Entries is null || file.Entries.Count == 0)
        {
            return true;
        }

        foreach (var pair in file.Entries)
        {
            var entry = pair.Value;
            if (entry is null || string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            var payload = JsonSerializer.Serialize(entry, JsonOptions);
            connection.Execute(
                "INSERT INTO workshop_pages (published_file_id, fetched_utc, payload) VALUES (?, ?, ?) " +
                "ON CONFLICT(published_file_id) DO UPDATE SET " +
                "fetched_utc = excluded.fetched_utc, payload = excluded.payload",
                pair.Key,
                entry.FetchedUtc.ToUnixTimeMilliseconds(),
                payload);
        }

        if (maxEntries > 0)
        {
            var count = connection.ExecuteScalar("SELECT COUNT(*) FROM workshop_pages");
            var total = count is null ? 0 : Convert.ToInt32(count, CultureInfo.InvariantCulture);
            var overflow = total - maxEntries;
            if (overflow > 0)
            {
                connection.Execute(
                    "DELETE FROM workshop_pages WHERE published_file_id IN (" +
                    "SELECT published_file_id FROM workshop_pages ORDER BY fetched_utc ASC LIMIT ?)",
                    overflow);
            }
        }

        return true;
    }

    private static bool IsExpected(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FormatException
            or OverflowException
            or JsonException;

    /// <summary>v1 旧索引的形状：<c>{ "version": 1, "entries": { id: page } }</c>。</summary>
    private sealed class LegacyIndex
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, CachedSteamItemPage>? Entries { get; set; }
    }
}
