namespace PdxLauncher.Core.Playsets;

using Microsoft.Win32.SafeHandles;

/// <summary>
/// sqlite3 语句句柄（sqlite3_stmt*）的 SafeHandle 包装。ReleaseHandle 调用 sqlite3_finalize。
/// </summary>
public sealed class SqliteStatementHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>创建一个空的（无效）句柄。</summary>
    public SqliteStatementHandle()
        : base(ownsHandle: true)
    {
    }

    /// <summary>包装 sqlite3_prepare_v2 返回的原始指针。</summary>
    public SqliteStatementHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        // sqlite3_finalize 的返回值是"最近一次 step 的结果码"，不代表 finalize 本身失败，
        // 所以这里固定返回 true。
        SqliteNative.Finalize(handle);
        return true;
    }
}
