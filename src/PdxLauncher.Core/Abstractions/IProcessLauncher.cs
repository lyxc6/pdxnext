using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>进程启动抽象；实现必须 detached 启动且不重定向 stdio。</summary>
public interface IProcessLauncher
{
    Task<ProcessStartResult> StartDetachedAsync(
        string exePath,
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken);
}
