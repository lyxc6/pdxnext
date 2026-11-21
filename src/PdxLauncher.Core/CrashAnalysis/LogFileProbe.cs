namespace PdxLauncher.Core.CrashAnalysis;

using PdxLauncher.Core.Abstractions;

/// <summary>日志文件当前状态（只读探测结果）。</summary>
public enum LogFileProbeState
{
    /// <summary>文件不存在。</summary>
    Missing,

    /// <summary>可正常读取。</summary>
    Readable,

    /// <summary>被其它进程独占锁定或区域锁定，暂时读不到。</summary>
    Locked,

    /// <summary>权限不足，无法读取。</summary>
    Unauthorized,

    /// <summary>文件带只读属性（仍可读，仅作提示）。</summary>
    ReadOnly,

    /// <summary>其它无法归类的读取失败。</summary>
    Unknown,
}

/// <summary>一份日志的探测结果。</summary>
/// <param name="FileName">日志文件名。</param>
/// <param name="State">状态。</param>
/// <param name="IsReadOnlyAttribute">是否带只读文件属性（与 <see cref="State"/> 独立）。</param>
public sealed record LogFileProbeResult(string FileName, LogFileProbeState State, bool IsReadOnlyAttribute);

/// <summary>
/// 日志文件状态只读探测：不写文件、不改变现有容错读取语义。
/// <para>
/// 现有的 CrashLogReader/LogTailReader 对锁文件一律返回空串，无法区分「缺失/被锁/无权限」；
/// 这个 probe 的唯一职责就是把状态分类出来供界面展示。永远不抛异常。
/// </para>
/// </summary>
public static class LogFileProbe
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    /// <summary>
    /// 探测一个日志文件。
    /// </summary>
    /// <param name="fileSystem">文件系统抽象（其 OpenRead 使用共享读句柄）。</param>
    /// <param name="path">绝对路径。</param>
    /// <param name="fileName">展示用的文件名，缺省取路径末段。</param>
    /// <param name="getAttributes">读取文件属性的委托，便于自检注入；缺省用 <see cref="File.GetAttributes"/>。</param>
    public static LogFileProbeResult Probe(
        IFileSystem fileSystem,
        string path,
        string? fileName = null,
        Func<string, FileAttributes>? getAttributes = null)
    {
        string name = fileName ?? Path.GetFileName(path);
        if (fileSystem is null || string.IsNullOrWhiteSpace(path))
        {
            return new LogFileProbeResult(name, LogFileProbeState.Missing, false);
        }

        if (!fileSystem.FileExists(path))
        {
            return new LogFileProbeResult(name, LogFileProbeState.Missing, false);
        }

        bool readOnly = false;
        try
        {
            var attributes = (getAttributes ?? File.GetAttributes)(path);
            readOnly = (attributes & FileAttributes.ReadOnly) != 0;
        }
        catch (Exception)
        {
            // 属性读不到不影响可读性判断。
        }

        try
        {
            // OpenRead 以 FileShare.ReadWrite|Delete 打开；只有其它进程用独占方式锁住时才会抛 IOException。
            using var stream = fileSystem.OpenRead(path);
            return new LogFileProbeResult(
                name,
                readOnly ? LogFileProbeState.ReadOnly : LogFileProbeState.Readable,
                readOnly);
        }
        catch (UnauthorizedAccessException)
        {
            return new LogFileProbeResult(name, LogFileProbeState.Unauthorized, readOnly);
        }
        catch (IOException exception)
        {
            int code = exception.HResult & 0xFFFF;
            var state = code is ErrorSharingViolation or ErrorLockViolation
                ? LogFileProbeState.Locked
                : LogFileProbeState.Unknown;
            return new LogFileProbeResult(name, state, readOnly);
        }
        catch (Exception)
        {
            return new LogFileProbeResult(name, LogFileProbeState.Unknown, readOnly);
        }
    }
}
