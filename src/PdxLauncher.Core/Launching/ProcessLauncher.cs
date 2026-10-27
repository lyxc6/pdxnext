namespace PdxLauncher.Core.Launching;

using System.ComponentModel;
using System.Diagnostics;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// <see cref="IProcessLauncher"/> 实现：用 <see cref="Process.Start(ProcessStartInfo)"/> 分离启动游戏进程。
/// <para>要求（与官方启动器一致）：<c>UseShellExecute = false</c>、<c>CreateNoWindow = false</c>、
/// stdio 全部不重定向、参数走 <see cref="ProcessStartInfo.ArgumentList"/>（自动处理引号）、
/// 启动后立即 Dispose 不 WaitForExit（启动器退出不能带走游戏）。</para>
/// </summary>
public sealed class ProcessLauncher : IProcessLauncher
{
    private readonly IAppLogger _logger;

    /// <summary>创建进程启动器。</summary>
    public ProcessLauncher(IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ProcessStartResult> StartDetachedAsync(
        string exePath,
        IReadOnlyList<string> args,
        string workingDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            return Task.FromResult(ProcessStartResult.Failure("找不到可执行文件: " + exePath));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = string.IsNullOrEmpty(workingDirectory) ? string.Empty : workingDirectory,
        };

        // 逐个添加，交给 ArgumentList 处理引号与转义；绝不拼字符串命令行。
        if (args is not null)
        {
            foreach (var arg in args)
            {
                if (!string.IsNullOrEmpty(arg))
                {
                    startInfo.ArgumentList.Add(arg);
                }
            }
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                _logger.Warn("启动进程失败：Process.Start 返回 null，路径 " + exePath);
                return Task.FromResult(ProcessStartResult.Failure("启动进程失败: " + exePath));
            }

            var processId = process.Id;

            // 不 WaitForExit：启动器必须能立刻退出，游戏继续独立运行。
            return Task.FromResult(ProcessStartResult.Success(processId));
        }
        catch (Win32Exception exception)
        {
            _logger.Error("启动进程失败（Win32）：" + exePath, exception);
            return Task.FromResult(ProcessStartResult.Failure("启动进程失败: " + exception.Message));
        }
        catch (InvalidOperationException exception)
        {
            _logger.Error("启动进程失败（无效操作）：" + exePath, exception);
            return Task.FromResult(ProcessStartResult.Failure("启动进程失败: " + exception.Message));
        }
        catch (IOException exception)
        {
            _logger.Error("启动进程失败（IO）：" + exePath, exception);
            return Task.FromResult(ProcessStartResult.Failure("启动进程失败: " + exception.Message));
        }
    }
}
