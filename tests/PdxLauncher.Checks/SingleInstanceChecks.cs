namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 单实例保护的源码契约（D8）。
/// <para>
/// 以前 <c>Program.Main</c> 只创建同名 Mutex 给安装器探测，从不检查 <c>createdNew</c>，
/// 快速 kill → 重启会并存两个进程，共享 data 目录并发写 settings / SQLite。
/// 两个实例的行为只能在真机上双击验证，但「锁 / 管道 / 前置窗口 / 资源键」写丢是重排
/// 启动流程时很容易发生的回归，所以用源码断言钉住。
/// </para>
/// </summary>
public static class SingleInstanceChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var program = Read(root, "src", "PdxLauncher.App", "Program.cs");
        var app = Read(root, "src", "PdxLauncher.App", "App.xaml.cs");
        var guard = Read(root, "src", "PdxLauncher.App", "Services", "SingleInstanceGuard.cs");

        ChecksContext.Check(
            "单实例: 启动入口检查 createdNew 并唤醒已有实例",
            program.Contains("SingleInstanceGuard.TryAcquire", StringComparison.Ordinal)
                && program.Contains("SingleInstanceGuard.SignalExistingInstance", StringComparison.Ordinal)
                && program.Contains("SingleInstanceGuard.ShowAlreadyRunning", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 更新器仍能释放 Mutex",
            program.Contains("ReleaseAppMutex() => SingleInstanceGuard.ReleaseMutex()", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 安装器 AppMutex 名字未改",
            guard.Contains(@"Local\PdxNext.App", StringComparison.Ordinal)
                && program.Contains("SingleInstanceGuard.ReleaseMutex", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 第一个实例在窗口起来后启动管道监听",
            app.Contains("SingleInstanceGuard.StartActivationServer", StringComparison.Ordinal)
                && app.Contains("SingleInstanceGuard.StopActivationServer", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 管道服务器只接受 show",
            guard.Contains("WaitForConnectionAsync", StringComparison.Ordinal)
                && guard.Contains("\"show\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 激活时恢复并抢前台",
            guard.Contains("ShowWindow", StringComparison.Ordinal)
                && guard.Contains("SetForegroundWindow", StringComparison.Ordinal)
                && guard.Contains("SwRestore", StringComparison.Ordinal));
        ChecksContext.Check(
            "单实例: 管道连不上有兜底提示",
            guard.Contains("MessageBoxW", StringComparison.Ordinal)
                && guard.Contains("Keys.SingleInstanceAlreadyRunning", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        ChecksContext.Check(
            "单实例: 中英都有「已在运行」文案",
            I18nResw.Get(zh, "singleInstance.alreadyRunning") is { Length: > 0 }
                && I18nResw.Get(en, "singleInstance.alreadyRunning") is { Length: > 0 });

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static string Read(string root, params string[] parts) =>
        File.ReadAllText(Path.Combine(root, Path.Combine(parts)));

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("找不到仓库根（PdxLauncher.sln）");
    }
}
