namespace PdxLauncher.Core.Playsets;

using System.Runtime.InteropServices;

/// <summary>
/// 极简的 SQLite 封装（基于 winsqlite3.dll）。
/// <para>只读打开时使用 SQLITE_OPEN_READONLY，绝不会因为读取而阻塞官方启动器的写入。</para>
/// <para>字符串一律走 UTF-8；NULL（column_type = 5）映射为 <c>null</c>，不会被误当成空字符串。</para>
/// </summary>
public sealed class SqliteConnection : IDisposable
{
    private readonly SqliteDatabaseHandle _database;
    private readonly string _path;
    private readonly bool _readOnly;
    private bool _disposed;

    private SqliteConnection(SqliteDatabaseHandle database, string path, bool readOnly)
    {
        _database = database;
        _path = path;
        _readOnly = readOnly;
    }

    /// <summary>是否以只读模式打开。</summary>
    public bool IsReadOnly => _readOnly;

    /// <summary>原生 SQLite 版本号，例如 "3.51.1"。</summary>
    public static string LibraryVersion => SqliteNative.LibraryVersion();

    /// <summary>以只读模式打开数据库（SQLITE_OPEN_READONLY）。文件不存在时抛 <see cref="SqliteException"/>。</summary>
    public static SqliteConnection OpenReadOnly(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Open(path, readOnly: true, create: false);
    }

    /// <summary>以读写模式打开数据库（SQLITE_OPEN_READWRITE，不创建新文件）。</summary>
    public static SqliteConnection OpenReadWrite(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Open(path, readOnly: false, create: false);
    }

    /// <summary>以读写模式打开数据库；文件不存在时创建（SQLITE_OPEN_READWRITE | SQLITE_OPEN_CREATE）。</summary>
    public static SqliteConnection OpenOrCreate(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return Open(path, readOnly: false, create: true);
    }

    private static SqliteConnection Open(string path, bool readOnly, bool create)
    {
        var flags = readOnly
            ? SqliteNative.OpenReadOnly
            : SqliteNative.OpenReadWrite | (create ? SqliteNative.OpenCreate : 0);

        var result = SqliteNative.OpenV2(path, out var rawHandle, flags, IntPtr.Zero);
        if (result != SqliteNative.Ok)
        {
            // 失败时 sqlite 仍会返回一个句柄，必须先关闭它再抛异常。
            if (rawHandle != IntPtr.Zero)
            {
                SqliteNative.CloseV2(rawHandle);
            }

            throw new SqliteException(result, "打开 SQLite 数据库失败（错误码 " + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）：" + path);
        }

        var handle = new SqliteDatabaseHandle(rawHandle);
        var connection = new SqliteConnection(handle, path, readOnly);
        // 5 秒忙等待，避免官方启动器短暂持有写锁时我们立刻失败。
        SqliteNative.BusyTimeout(handle, 5000);
        return connection;
    }

    /// <summary>数据库文件路径（打开时传入的原始路径）。</summary>
    public string FilePath => _path;

    /// <summary>当前是否处在显式事务中。</summary>
    public bool IsInTransaction { get; private set; }

    /// <summary>执行一条不返回结果集的语句，返回受影响行数。</summary>
    public int Execute(string sql, params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ThrowIfDisposed();
        using var statement = Prepare(sql);
        BindAll(statement, parameters);
        StepToCompletion(statement);
        return SqliteNative.Changes(_database);
    }

    /// <summary>执行查询，返回按列名索引的行集合（列名保持原样，便于调用方按真实列名取值）。</summary>
    public IReadOnlyList<Dictionary<string, object?>> Query(string sql, params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ThrowIfDisposed();
        using var statement = Prepare(sql);
        BindAll(statement, parameters);

        var columns = ReadColumnNames(statement);
        var rows = new List<Dictionary<string, object?>>();
        while (true)
        {
            var result = SqliteNative.Step(statement);
            if (result == SqliteNative.Done)
            {
                break;
            }

            if (result != SqliteNative.Row)
            {
                throw Error("读取数据失败", result);
            }

            var row = new Dictionary<string, object?>(columns.Count, StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < columns.Count; i++)
            {
                row[columns[i]] = ReadValue(statement, i);
            }

            rows.Add(row);
        }

        return rows;
    }

    /// <summary>执行查询并把每行投影成强类型对象；投影函数收到的是按列名索引的只读字典。</summary>
    public IReadOnlyList<T> Query<T>(
        string sql,
        Func<IReadOnlyDictionary<string, object?>, T> projector,
        params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(projector);
        var rows = Query(sql, parameters);
        var projected = new List<T>(rows.Count);
        foreach (var row in rows)
        {
            projected.Add(projector(row));
        }

        return projected;
    }

    /// <summary>执行查询并返回第一行第一列；无结果时返回 <c>null</c>。</summary>
    public object? ExecuteScalar(string sql, params object?[] parameters)
    {
        ArgumentNullException.ThrowIfNull(sql);
        ThrowIfDisposed();
        using var statement = Prepare(sql);
        BindAll(statement, parameters);
        var result = SqliteNative.Step(statement);
        if (result == SqliteNative.Done)
        {
            return null;
        }

        if (result != SqliteNative.Row)
        {
            throw Error("读取标量失败", result);
        }

        return SqliteNative.ColumnCount(statement) == 0 ? null : ReadValue(statement, 0);
    }

    /// <summary>判断表是否存在（Stellaris 没有 playsets_dlcs 表，调用方必须先探测）。</summary>
    public bool TableExists(string tableName)
    {
        ArgumentNullException.ThrowIfNull(tableName);
        var value = ExecuteScalar(
            "SELECT 1 FROM sqlite_master WHERE type = 'table' AND name = ? LIMIT 1",
            tableName);
        return value is not null;
    }

    /// <summary>BEGIN IMMEDIATE：立刻拿写锁，避免与官方启动器交错。</summary>
    public void BeginImmediate()
    {
        ThrowIfDisposed();
        if (IsInTransaction)
        {
            throw new InvalidOperationException("事务已开始，不能重复 BEGIN IMMEDIATE。");
        }

        ExecRaw("BEGIN IMMEDIATE", "开启事务失败");
        IsInTransaction = true;
    }

    /// <summary>提交事务。</summary>
    public void Commit()
    {
        ThrowIfDisposed();
        if (!IsInTransaction)
        {
            return;
        }

        ExecRaw("COMMIT", "提交事务失败");
        IsInTransaction = false;
    }

    /// <summary>回滚事务；未处于事务中时是空操作。</summary>
    public void Rollback()
    {
        ThrowIfDisposed();
        if (!IsInTransaction)
        {
            return;
        }

        IsInTransaction = false;
        // 回滚失败通常意味着连接已不可用，此时吞掉异常没有意义，但仍要尽力尝试。
        var result = SqliteNative.Exec(_database, "ROLLBACK", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result != SqliteNative.Ok)
        {
            throw Error("回滚事务失败", result);
        }
    }

    private void ExecRaw(string sql, string action)
    {
        var result = SqliteNative.Exec(_database, sql, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (result != SqliteNative.Ok)
        {
            throw Error(action, result);
        }
    }

    private SqliteStatementHandle Prepare(string sql)
    {
        var result = SqliteNative.PrepareV2(_database, sql, -1, out var rawStatement, IntPtr.Zero);
        if (result != SqliteNative.Ok || rawStatement == IntPtr.Zero)
        {
            if (rawStatement != IntPtr.Zero)
            {
                SqliteNative.Finalize(rawStatement);
            }

            throw Error("准备 SQL 失败：" + sql, result);
        }

        return new SqliteStatementHandle(rawStatement);
    }

    private static List<string> ReadColumnNames(SqliteStatementHandle statement)
    {
        var count = SqliteNative.ColumnCount(statement);
        var names = new List<string>(count);
        for (var i = 0; i < count; i++)
        {
            var pointer = SqliteNative.ColumnName(statement, i);
            var name = pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
            names.Add(name);
        }

        return names;
    }

    private static object? ReadValue(SqliteStatementHandle statement, int index)
    {
        var type = SqliteNative.ColumnType(statement, index);
        switch (type)
        {
            case SqliteNative.TypeNull:
                // column_type = 5：必须映射成 null，不能返回空字符串。
                return null;

            case SqliteNative.TypeInteger:
                return SqliteNative.ColumnInt64(statement, index);

            case SqliteNative.TypeFloat:
                return SqliteNative.ColumnDouble(statement, index);

            case SqliteNative.TypeText:
            {
                // 先取字节长度再取指针，保证多字节中文被完整读取。
                var length = SqliteNative.ColumnBytes(statement, index);
                var pointer = SqliteNative.ColumnText(statement, index);
                if (pointer == IntPtr.Zero)
                {
                    return null;
                }

                return length <= 0 ? string.Empty : Marshal.PtrToStringUTF8(pointer, length) ?? string.Empty;
            }

            case SqliteNative.TypeBlob:
            {
                var length = SqliteNative.ColumnBytes(statement, index);
                var pointer = SqliteNative.ColumnBlob(statement, index);
                if (pointer == IntPtr.Zero || length <= 0)
                {
                    return Array.Empty<byte>();
                }

                var buffer = new byte[length];
                Marshal.Copy(pointer, buffer, 0, length);
                return buffer;
            }

            default:
                return null;
        }
    }

    private static void BindAll(SqliteStatementHandle statement, object?[]? parameters)
    {
        if (parameters is null || parameters.Length == 0)
        {
            return;
        }

        for (var i = 0; i < parameters.Length; i++)
        {
            BindOne(statement, i + 1, parameters[i]);
        }
    }

    private static void BindOne(SqliteStatementHandle statement, int index, object? value)
    {
        int result;
        switch (value)
        {
            case null:
            case DBNull:
                result = SqliteNative.BindNull(statement, index);
                break;

            case string text:
                result = SqliteNative.BindText(statement, index, text, -1, SqliteNative.Transient);
                break;

            case bool flag:
                result = SqliteNative.BindInt64(statement, index, flag ? 1 : 0);
                break;

            case byte[] blob:
                result = SqliteNative.BindBlob(statement, index, blob, blob.Length, SqliteNative.Transient);
                break;

            case Guid guid:
                result = SqliteNative.BindText(statement, index, guid.ToString("D"), -1, SqliteNative.Transient);
                break;

            case DateTimeOffset timestamp:
                result = SqliteNative.BindInt64(statement, index, timestamp.ToUnixTimeMilliseconds());
                break;

            case DateTime dateTime:
                result = SqliteNative.BindInt64(
                    statement,
                    index,
                    new DateTimeOffset(dateTime.ToUniversalTime(), TimeSpan.Zero).ToUnixTimeMilliseconds());
                break;

            case float single:
                result = SqliteNative.BindDouble(statement, index, single);
                break;

            case double number:
                result = SqliteNative.BindDouble(statement, index, number);
                break;

            case decimal decimalValue:
                result = SqliteNative.BindDouble(statement, index, (double)decimalValue);
                break;

            case sbyte or byte or short or ushort or int or uint or long or ulong:
                result = SqliteNative.BindInt64(statement, index, Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture));
                break;

            default:
                result = SqliteNative.BindText(
                    statement,
                    index,
                    Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                    -1,
                    SqliteNative.Transient);
                break;
        }

        if (result != SqliteNative.Ok)
        {
            throw new SqliteException(result, "绑定第 " + index.ToString(System.Globalization.CultureInfo.InvariantCulture) + " 个参数失败（错误码 " + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）。");
        }
    }

    private static void StepToCompletion(SqliteStatementHandle statement)
    {
        while (true)
        {
            var result = SqliteNative.Step(statement);
            if (result == SqliteNative.Done)
            {
                return;
            }

            if (result != SqliteNative.Row)
            {
                throw new SqliteException(result, "执行 SQL 失败（错误码 " + result.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）。");
            }
        }
    }

    private SqliteException Error(string action, int resultCode)
    {
        var messagePointer = SqliteNative.ErrMsg(_database);
        var message = messagePointer == IntPtr.Zero
            ? string.Empty
            : Marshal.PtrToStringUTF8(messagePointer) ?? string.Empty;

        var code = resultCode == SqliteNative.Ok ? SqliteNative.ErrCode(_database) : resultCode;
        var text = action + "（错误码 " + code.ToString(System.Globalization.CultureInfo.InvariantCulture) + "）";
        if (!string.IsNullOrEmpty(message))
        {
            text += "：" + message;
        }

        return new SqliteException(code, text);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (IsInTransaction)
        {
            // 未提交的事务必须回滚，否则连接关闭时会留下未完成状态。
            IsInTransaction = false;
            SqliteNative.Exec(_database, "ROLLBACK", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }

        _database.Dispose();
    }
}
