namespace PdxLauncher.Core.Contracts;

/// <summary>启动进程的结果。</summary>
public sealed record ProcessStartResult(bool Started, int? ProcessId, string? ErrorMessage)
{
    /// <summary>成功。</summary>
    public static ProcessStartResult Success(int processId) => new(true, processId, null);

    /// <summary>失败。</summary>
    public static ProcessStartResult Failure(string message) => new(false, null, message);
}
