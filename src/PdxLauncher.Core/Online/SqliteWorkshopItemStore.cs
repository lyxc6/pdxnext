namespace PdxLauncher.Core.Online;

using System.Globalization;
using System.Text.Json;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Playsets;
using PdxLauncher.Core.Storage;

/// <summary>
/// <see cref="IWorkshopItemStore"/> 的 SQLite 实现：数据落在自有库的 <c>workshop_items</c> 表。
/// <para>
/// 一行一个「来源 + id」，写一条只动一行；读取按需查询，启动时不再整份反序列化 348KB JSON。
/// 所有异常都吞在内部（记 Warn、按空结果返回），与旧的文档式缓存保持同一套容错策略。
/// </para>
/// </summary>
public sealed class SqliteWorkshopItemStore : IWorkshopItemStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly PdxDatabase _database;
    private readonly IAppLogger _logger;

    /// <summary>构造。</summary>
    /// <param name="database">自有库。</param>
    /// <param name="logger">日志。</param>
    public SqliteWorkshopItemStore(PdxDatabase database, IAppLogger logger)
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
                return ReadInt(connection.ExecuteScalar("SELECT COUNT(*) FROM workshop_items"));
            }
            catch (Exception exception) when (IsExpected(exception))
            {
                _logger.Warn("读取创意工坊缓存条目数失败：" + exception.Message);
                return 0;
            }
        }
    }

    /// <inheritdoc />
    public WorkshopInfoCacheEntry? Find(string key)
    {
        if (!TrySplitKey(key, out var source, out var id))
        {
            return null;
        }

        try
        {
            using var connection = _database.Open();
            var rows = connection.Query(
                "SELECT * FROM workshop_items WHERE source = ? AND id = ? LIMIT 1",
                source,
                id);
            return rows.Count == 0 ? null : MapEntry(rows[0]);
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("读取创意工坊缓存条目失败：" + exception.Message);
            return null;
        }
    }

    /// <inheritdoc />
    public void Upsert(WorkshopInfoCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (string.IsNullOrWhiteSpace(entry.Id))
        {
            return;
        }

        var source = string.Equals(entry.Source, "steam", StringComparison.OrdinalIgnoreCase)
            ? WorkshopInfoCache.SourceTag(WorkshopSource.Steam)
            : WorkshopInfoCache.SourceTag(WorkshopSource.Pdx);

        try
        {
            _database.Write(connection =>
            {
                InsertEntry(connection, source, entry.Id, entry, DateTimeOffset.UtcNow);
                TrimOn(connection, WorkshopInfoCache.MaxEntries);
            });
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("写入创意工坊缓存条目失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public void SetCover(string key, string fileName, string? url, DateTimeOffset fetchedUtc)
    {
        if (!TrySplitKey(key, out var source, out var id))
        {
            return;
        }

        try
        {
            _database.Write(connection => connection.Execute(
                "INSERT INTO workshop_items (source, id, cover_file, cover_url, cover_fetched_utc, fetched_utc) " +
                "VALUES (?, ?, ?, ?, ?, ?) " +
                "ON CONFLICT(source, id) DO UPDATE SET " +
                "cover_file = excluded.cover_file, " +
                "cover_url = COALESCE(excluded.cover_url, workshop_items.cover_url), " +
                "cover_fetched_utc = excluded.cover_fetched_utc",
                source,
                id,
                fileName,
                url,
                ToStamp(fetchedUtc),
                ToStamp(fetchedUtc)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("记录创意工坊封面失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public void SetDetail(
        string key,
        string? text,
        IReadOnlyList<string>? screenshots,
        string? creatorSteamId,
        IReadOnlyList<string>? tags,
        long views,
        long lifetimeSubscriptions,
        DateTimeOffset fetchedUtc)
    {
        if (!TrySplitKey(key, out var source, out var id))
        {
            return;
        }

        try
        {
            _database.Write(connection => connection.Execute(
                "INSERT INTO workshop_items (" +
                "source, id, detail_text, detail_screenshots, detail_fetched_utc, creator_steam_id, " +
                "tags, views, lifetime_subscriptions, fetched_utc) " +
                "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?) " +
                "ON CONFLICT(source, id) DO UPDATE SET " +
                "detail_text = excluded.detail_text, " +
                "detail_screenshots = excluded.detail_screenshots, " +
                "detail_fetched_utc = excluded.detail_fetched_utc, " +
                "creator_steam_id = COALESCE(excluded.creator_steam_id, workshop_items.creator_steam_id), " +
                "tags = COALESCE(excluded.tags, workshop_items.tags), " +
                "views = CASE WHEN excluded.views > 0 THEN excluded.views ELSE workshop_items.views END, " +
                "lifetime_subscriptions = CASE WHEN excluded.lifetime_subscriptions > 0 " +
                "THEN excluded.lifetime_subscriptions ELSE workshop_items.lifetime_subscriptions END",
                source,
                id,
                text,
                SerializeList(screenshots),
                ToStamp(fetchedUtc),
                string.IsNullOrWhiteSpace(creatorSteamId) ? null : creatorSteamId,
                SerializeList(tags),
                views,
                lifetimeSubscriptions,
                ToStamp(fetchedUtc)));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("记录创意工坊详情失败：" + exception.Message);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<WorkshopInfoCacheEntry> List(WorkshopSource source, string? gameId, int limit)
    {
        var tag = WorkshopInfoCache.SourceTag(source);
        try
        {
            using var connection = _database.Open();
            var rows = connection.Query(
                "SELECT * FROM workshop_items WHERE source = ? " +
                "AND (? = '' OR game_id = ?) " +
                "ORDER BY COALESCE(updated_utc, 0) DESC, COALESCE(fetched_utc, 0) DESC " +
                "LIMIT ?",
                tag,
                gameId ?? string.Empty,
                gameId ?? string.Empty,
                Math.Max(limit, 1));

            var entries = new List<WorkshopInfoCacheEntry>(rows.Count);
            foreach (var row in rows)
            {
                entries.Add(MapEntry(row));
            }

            return entries;
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("读取创意工坊缓存列表失败：" + exception.Message);
            return [];
        }
    }

    /// <inheritdoc />
    public void Clear()
    {
        try
        {
            _database.Write(connection => connection.Execute("DELETE FROM workshop_items"));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("清空创意工坊缓存失败：" + exception.Message);
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
            _database.Write(connection => TrimOn(connection, maxEntries));
        }
        catch (Exception exception) when (IsExpected(exception))
        {
            _logger.Warn("淘汰创意工坊缓存条目失败：" + exception.Message);
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
            _logger.Warn("旧创意工坊缓存索引无法解析，跳过导入：" + exception.Message);
            return false;
        }

        if (file?.Entries is null || file.Entries.Count == 0)
        {
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        foreach (var pair in file.Entries)
        {
            var entry = pair.Value;
            if (entry is null)
            {
                continue;
            }

            var source = string.Equals(entry.Source, "steam", StringComparison.OrdinalIgnoreCase)
                ? WorkshopInfoCache.SourceTag(WorkshopSource.Steam)
                : WorkshopInfoCache.SourceTag(WorkshopSource.Pdx);

            var id = entry.Id;
            if (string.IsNullOrWhiteSpace(id))
            {
                var index = pair.Key.IndexOf(':', StringComparison.Ordinal);
                if (index <= 0 || index >= pair.Key.Length - 1)
                {
                    continue;
                }

                id = pair.Key[(index + 1)..];
            }

            InsertEntry(connection, source, id, entry, now);
        }

        TrimOn(connection, maxEntries);
        return true;
    }

    private static void InsertEntry(
        SqliteConnection connection,
        string source,
        string id,
        WorkshopInfoCacheEntry entry,
        DateTimeOffset now)
    {
        connection.Execute(
            "INSERT INTO workshop_items (" +
            "source, id, game_id, name, author, rating, ratings_total, subscriptions, " +
            "updated_utc, size_bytes, cover_url, fetched_utc) " +
            "VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?) " +
            "ON CONFLICT(source, id) DO UPDATE SET " +
            "game_id = COALESCE(excluded.game_id, workshop_items.game_id), " +
            "name = COALESCE(excluded.name, workshop_items.name), " +
            "author = COALESCE(excluded.author, workshop_items.author), " +
            "rating = COALESCE(excluded.rating, workshop_items.rating), " +
            "ratings_total = COALESCE(excluded.ratings_total, workshop_items.ratings_total), " +
            "subscriptions = COALESCE(excluded.subscriptions, workshop_items.subscriptions), " +
            "updated_utc = COALESCE(excluded.updated_utc, workshop_items.updated_utc), " +
            "size_bytes = COALESCE(excluded.size_bytes, workshop_items.size_bytes), " +
            "cover_url = COALESCE(excluded.cover_url, workshop_items.cover_url), " +
            "fetched_utc = excluded.fetched_utc",
            source,
            id,
            entry.GameId,
            entry.Name,
            entry.Author,
            entry.Rating,
            entry.RatingsTotal,
            entry.Subscriptions,
            ToStamp(entry.UpdatedUtc),
            entry.SizeBytes,
            entry.CoverUrl,
            ToStamp(now));

        // 封面文件名与详情字段不在列表刷新里覆盖，避免把已抓到的正文抹掉。
        if (!string.IsNullOrWhiteSpace(entry.CoverFileName))
        {
            connection.Execute(
                "UPDATE workshop_items SET cover_file = ?, cover_fetched_utc = ? WHERE source = ? AND id = ?",
                entry.CoverFileName,
                ToStamp(entry.CoverFetchedUtc ?? now),
                source,
                id);
        }

        if (!string.IsNullOrWhiteSpace(entry.DetailText))
        {
            connection.Execute(
                "UPDATE workshop_items SET detail_text = ?, detail_screenshots = ?, detail_fetched_utc = ?, " +
                "creator_steam_id = COALESCE(?, creator_steam_id), tags = COALESCE(?, tags), " +
                "views = CASE WHEN ? > 0 THEN ? ELSE views END, " +
                "lifetime_subscriptions = CASE WHEN ? > 0 THEN ? ELSE lifetime_subscriptions END " +
                "WHERE source = ? AND id = ?",
                entry.DetailText,
                SerializeList(entry.DetailScreenshots),
                ToStamp(entry.DetailFetchedUtc ?? now),
                entry.CreatorSteamId,
                SerializeList(entry.Tags),
                entry.Views ?? 0,
                entry.Views ?? 0,
                entry.LifetimeSubscriptions ?? 0,
                entry.LifetimeSubscriptions ?? 0,
                source,
                id);
        }
    }

    private static void TrimOn(SqliteConnection connection, int maxEntries)
    {
        if (maxEntries <= 0)
        {
            return;
        }

        var count = ReadInt(connection.ExecuteScalar("SELECT COUNT(*) FROM workshop_items"));
        var overflow = count - maxEntries;
        if (overflow <= 0)
        {
            return;
        }

        connection.Execute(
            "DELETE FROM workshop_items WHERE rowid IN (" +
            "SELECT rowid FROM workshop_items ORDER BY COALESCE(fetched_utc, 0) ASC LIMIT ?)",
            overflow);
    }

    private static bool TrySplitKey(string key, out string source, out string id)
    {
        source = string.Empty;
        id = string.Empty;
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var index = key.IndexOf(':', StringComparison.Ordinal);
        if (index <= 0 || index >= key.Length - 1)
        {
            return false;
        }

        source = key[..index];
        id = key[(index + 1)..];
        return true;
    }

    private static WorkshopInfoCacheEntry MapEntry(IReadOnlyDictionary<string, object?> row) => new()
    {
        Source = GetString(row, "source"),
        Id = GetString(row, "id") ?? string.Empty,
        GameId = GetString(row, "game_id"),
        Name = GetString(row, "name"),
        Author = GetString(row, "author"),
        Rating = GetDouble(row, "rating"),
        RatingsTotal = GetInt32(row, "ratings_total"),
        Subscriptions = GetInt64(row, "subscriptions"),
        UpdatedUtc = GetStamp(row, "updated_utc"),
        SizeBytes = GetInt64(row, "size_bytes"),
        CoverUrl = GetString(row, "cover_url"),
        CoverFileName = GetString(row, "cover_file"),
        CoverFetchedUtc = GetStamp(row, "cover_fetched_utc"),
        DetailText = GetString(row, "detail_text"),
        DetailScreenshots = DeserializeList(GetString(row, "detail_screenshots")),
        DetailFetchedUtc = GetStamp(row, "detail_fetched_utc"),
        FetchedUtc = GetStamp(row, "fetched_utc"),
        CreatorSteamId = GetString(row, "creator_steam_id"),
        Tags = DeserializeList(GetString(row, "tags")),
        Views = GetInt64(row, "views"),
        LifetimeSubscriptions = GetInt64(row, "lifetime_subscriptions"),
    };

    private static long? ToStamp(DateTimeOffset? value) =>
        value is { } stamp ? stamp.ToUnixTimeMilliseconds() : null;

    private static long ToStamp(DateTimeOffset value) => value.ToUnixTimeMilliseconds();

    private static string? SerializeList(IReadOnlyList<string>? values) =>
        values is { Count: > 0 } ? JsonSerializer.Serialize(values, JsonOptions) : null;

    private static List<string>? DeserializeList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) ? value as string : null;

    private static long? GetInt64(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToInt64(value, CultureInfo.InvariantCulture)
            : null;

    private static int? GetInt32(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
            : null;

    private static double? GetDouble(IReadOnlyDictionary<string, object?> row, string column) =>
        row.TryGetValue(column, out var value) && value is not null
            ? Convert.ToDouble(value, CultureInfo.InvariantCulture)
            : null;

    private static DateTimeOffset? GetStamp(IReadOnlyDictionary<string, object?> row, string column)
    {
        var stamp = GetInt64(row, column);
        return stamp is { } value ? DateTimeOffset.FromUnixTimeMilliseconds(value) : null;
    }

    private static int ReadInt(object? value) =>
        value is null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);

    private static bool IsExpected(Exception exception) =>
        exception is IOException
            or UnauthorizedAccessException
            or InvalidOperationException
            or FormatException
            or OverflowException;

    /// <summary>v1 旧索引的形状：<c>{ "version": 1, "entries": { key: entry } }</c>。</summary>
    private sealed class LegacyIndex
    {
        public int Version { get; set; } = 1;

        public Dictionary<string, WorkshopInfoCacheEntry>? Entries { get; set; }
    }
}
