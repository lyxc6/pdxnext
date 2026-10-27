namespace PdxLauncher.Core.Playsets;

using Microsoft.Win32.SafeHandles;

/// <summary>
/// sqlite3 数据库句柄的 SafeHandle 包装。ReleaseHandle 调用 sqlite3_close_v2，
/// 保证即使调用方忘记 Dispose，GC 终结时也不会泄漏原生连接。
/// </summary>
public sealed class SqliteDatabaseHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    /// <summary>创建一个空的（无效）句柄，供 P/Invoke 出参场景使用。</summary>
    public SqliteDatabaseHandle()
        : base(ownsHandle: true)
    {
    }

    /// <summary>包装 sqlite3_open_v2 返回的原始指针。</summary>
    public SqliteDatabaseHandle(IntPtr handle)
        : base(ownsHandle: true)
    {
        SetHandle(handle);
    }

    protected override bool ReleaseHandle()
    {
        // sqlite3_close_v2 对僵尸连接总是返回 OK；即使返回非 OK 也没有重试意义，
        // 因此这里始终报告成功，避免 SafeHandle 反复调用释放逻辑。
        SqliteNative.CloseV2(handle);
        return true;
    }
}
