namespace PdxLauncher.Core.Playsets;

using System.Runtime.InteropServices;

/// <summary>
/// C:\Windows\System32\winsqlite3.dll（本机实测 SQLite 3.51.1）的 P/Invoke 声明。
/// 离线环境不引入任何 NuGet 包，也不依赖 System.Data.SQLite。
/// 字符串统一按 UTF-8 marshalling：sqlite3_column_text / sqlite3_column_name / sqlite3_errmsg
/// 返回的是 UTF-8 字节指针，用 Marshal.PtrToStringUTF8 还原（中文安全）。
/// </summary>
public static class SqliteNative
{
    /// <summary>Windows 自带的 SQLite 动态库名（位于 System32，加载器可直接解析）。</summary>
    public const string LibraryName = "winsqlite3.dll";

    /// <summary>SQLITE_OK。</summary>
    public const int Ok = 0;

    /// <summary>SQLITE_ROW：step 取到一行。</summary>
    public const int Row = 100;

    /// <summary>SQLITE_DONE：step 执行完毕。</summary>
    public const int Done = 101;

    /// <summary>SQLITE_OPEN_READONLY：只读打开，绝不因为读而锁库。</summary>
    public const int OpenReadOnly = 0x00000001;

    /// <summary>SQLITE_OPEN_READWRITE。</summary>
    public const int OpenReadWrite = 0x00000002;

    /// <summary>SQLITE_OPEN_CREATE。</summary>
    public const int OpenCreate = 0x00000004;

    /// <summary>SQLITE_OPEN_URI。</summary>
    public const int OpenUri = 0x00000040;

    /// <summary>sqlite3_column_type：INTEGER。</summary>
    public const int TypeInteger = 1;

    /// <summary>sqlite3_column_type：FLOAT。</summary>
    public const int TypeFloat = 2;

    /// <summary>sqlite3_column_type：TEXT（UTF-8）。</summary>
    public const int TypeText = 3;

    /// <summary>sqlite3_column_type：BLOB。</summary>
    public const int TypeBlob = 4;

    /// <summary>sqlite3_column_type：NULL（= 5，必须显式处理，不能当成空字符串）。</summary>
    public const int TypeNull = 5;

    /// <summary>SQLITE_TRANSIENT：让 sqlite 立刻复制绑定值，调用返回后无需保留托管缓冲区。</summary>
    public static readonly IntPtr Transient = new(-1);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_open_v2")]
    public static extern int OpenV2(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string filename,
        out IntPtr database,
        int flags,
        IntPtr vfs);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_close_v2")]
    public static extern int CloseV2(IntPtr database);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_prepare_v2")]
    public static extern int PrepareV2(
        SqliteDatabaseHandle database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        int byteCount,
        out IntPtr statement,
        IntPtr tail);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_step")]
    public static extern int Step(SqliteStatementHandle statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_finalize")]
    public static extern int Finalize(IntPtr statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_reset")]
    public static extern int Reset(SqliteStatementHandle statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_clear_bindings")]
    public static extern int ClearBindings(SqliteStatementHandle statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_text")]
    public static extern int BindText(
        SqliteStatementHandle statement,
        int index,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string value,
        int byteCount,
        IntPtr destructor);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_int64")]
    public static extern int BindInt64(SqliteStatementHandle statement, int index, long value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_double")]
    public static extern int BindDouble(SqliteStatementHandle statement, int index, double value);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_null")]
    public static extern int BindNull(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_bind_blob")]
    public static extern int BindBlob(
        SqliteStatementHandle statement,
        int index,
        byte[] value,
        int byteCount,
        IntPtr destructor);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_count")]
    public static extern int ColumnCount(SqliteStatementHandle statement);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_type")]
    public static extern int ColumnType(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_name")]
    public static extern IntPtr ColumnName(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_text")]
    public static extern IntPtr ColumnText(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int")]
    public static extern int ColumnInt(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_int64")]
    public static extern long ColumnInt64(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_double")]
    public static extern double ColumnDouble(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_blob")]
    public static extern IntPtr ColumnBlob(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_column_bytes")]
    public static extern int ColumnBytes(SqliteStatementHandle statement, int index);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_changes")]
    public static extern int Changes(SqliteDatabaseHandle database);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errcode")]
    public static extern int ErrCode(SqliteDatabaseHandle database);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_errmsg")]
    public static extern IntPtr ErrMsg(SqliteDatabaseHandle database);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_exec")]
    public static extern int Exec(
        SqliteDatabaseHandle database,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string sql,
        IntPtr callback,
        IntPtr userData,
        IntPtr errorMessage);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_busy_timeout")]
    public static extern int BusyTimeout(SqliteDatabaseHandle database, int milliseconds);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_free")]
    public static extern void Free(IntPtr pointer);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sqlite3_libversion")]
    private static extern IntPtr LibVersionPointer();

    /// <summary>返回原生 SQLite 版本号（本机实测 "3.51.1"）。</summary>
    public static string LibraryVersion()
    {
        var pointer = LibVersionPointer();
        return pointer == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
    }
}
