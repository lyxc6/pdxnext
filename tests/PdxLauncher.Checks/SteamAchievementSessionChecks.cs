namespace PdxLauncher.Checks;

using System.Diagnostics;

using PdxLauncher.App.Services;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 常驻宿主会话：复用、崩溃重启一次、失败回落、坏版本回落、结果码透传、并发串行、取消传播。
/// <para>
/// 用「自检 exe 自己当假 worker」跑真进程，口径和线上完全一致：同样走 stdin/stdout 行协议，
/// 同样会被 Kill。这里漏了 fallback 不会崩，只会让用户在某台机器上永远读不到成就，
/// 所以每条退化路径都必须有一条断言。
/// </para>
/// </summary>
public static class SteamAchievementSessionChecks
{
    private const string FakeApiPath = @"C:\fake\steam_api64.dll";

    public static int Run(IFileSystem fileSystem)
    {
        var host = FindFakeHostPath();
        if (host is null)
        {
            ChecksContext.Check("成就会话：找不到假宿主 exe", false, AppContext.BaseDirectory);
            return 1;
        }

        CheckReuse(host);
        CheckCrashRestartsOnce(host);
        CheckPersistentFailureFallsBack(host);
        CheckBadVersionFallsBack(host);
        CheckResultCodePassthrough(host);
        CheckCancellationPropagates(host);
        CheckConcurrentRequestsSerialize(host);
        CheckDisposedSessionFallsBack(host);
        return 0;
    }

    private static void CheckReuse(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("ok", state));
        using var session = NewSession(host);

        var first = Read(session);
        var second = Read(session);

        ChecksContext.Check("成就会话：首次读取成功", first.Ok && first.Items.Count == 2);
        ChecksContext.Check("成就会话：第二次读取成功", second.Ok && second.Items.Count == 2);
        ChecksContext.Equal("成就会话：两条命令只启动一次 worker", 1, session.WorkerStarts);
        ChecksContext.Equal("成就会话：正常路径不重启", 0, session.RestartCount);
        ChecksContext.Equal("成就会话：正常路径不回落", 0, session.FallbackCount);
    }

    private static void CheckCrashRestartsOnce(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("crash-first", state));
        using var session = NewSession(host);

        var result = Read(session);

        ChecksContext.Check("成就会话：worker 崩溃后重启一次仍成功", result.Ok && result.Items.Count == 2);
        ChecksContext.Equal("成就会话：崩溃后重启了一次", 1, session.RestartCount);
        ChecksContext.Equal("成就会话：重启后启动过两条 worker", 2, session.WorkerStarts);
        ChecksContext.Equal("成就会话：崩溃重启成功时不回落", 0, session.FallbackCount);
    }

    private static void CheckPersistentFailureFallsBack(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("bad-payload", state));
        using var session = NewSession(host);

        var result = Read(session);

        ChecksContext.Check("成就会话：连续坏回包仍由一次性宿主救回", result.Ok && result.Items.Count == 2);
        ChecksContext.Equal("成就会话：坏回包只重启一次", 1, session.RestartCount);
        ChecksContext.Equal("成就会话：坏回包最终回落一次", 1, session.FallbackCount);
    }

    private static void CheckBadVersionFallsBack(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("bad-version", state));
        using var session = NewSession(host);

        var result = Read(session);

        ChecksContext.Check("成就会话：协议版本不符时由一次性宿主救回", result.Ok && result.Items.Count == 2);
        ChecksContext.Equal("成就会话：坏版本握手后回落一次", 1, session.FallbackCount);
        ChecksContext.Equal("成就会话：坏版本不重启（启动即失败）", 0, session.RestartCount);
        ChecksContext.Check("成就会话：坏版本 worker 没有留下常驻进程", !session.IsWorkerRunning);
    }

    private static void CheckResultCodePassthrough(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("no-dll", state));
        using var session = NewSession(host);

        var result = Read(session);

        ChecksContext.Check(
            "成就会话：noDll 结果码原样透传",
            !result.Ok && result.Code == SteamAchievementCodes.NoDll && session.RestartCount == 0);
        ChecksContext.Equal("成就会话：noDll 不是传输失败，不触发回落", 0, session.FallbackCount);
    }

    private static void CheckCancellationPropagates(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("slow-worker", state));
        using var session = NewSession(host, requestTimeout: TimeSpan.FromSeconds(5));

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(400));
        var threw = false;
        try
        {
            session.ReadAchievementsAsync(FakeApiPath, 123, cts.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            threw = true;
        }

        ChecksContext.Check("成就会话：调用方取消失败时原样抛 OCE（不被写成 Steam 不可用）", threw);
        ChecksContext.Equal("成就会话：取消不触发回落", 0, session.FallbackCount);
    }

    private static void CheckConcurrentRequestsSerialize(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("ok-delay", state));
        using var session = NewSession(host);

        // 先付掉启动成本，只量两条命令的排队时间。
        _ = Read(session);

        var started = Stopwatch.StartNew();
        var first = Task.Run(() => Read(session));
        var second = Task.Run(() => Read(session));
        Task.WaitAll(first, second);
        started.Stop();

        ChecksContext.Check(
            "成就会话：并发两条命令都成功",
            first.Result.Ok && second.Result.Ok && session.WorkerStarts == 1);
        ChecksContext.Check(
            "成就会话：并发命令在会话层排队（总耗时 ≥ 两个 150 ms）",
            started.Elapsed >= TimeSpan.FromMilliseconds(250),
            started.ElapsedMilliseconds + " ms");
    }

    private static void CheckDisposedSessionFallsBack(string host)
    {
        var state = NewStateDirectory();
        using var env = new EnvScope(("ok", state));
        var session = NewSession(host);

        _ = Read(session);
        var runningBefore = session.IsWorkerRunning;
        session.Dispose();

        var result = Read(session);

        ChecksContext.Check("成就会话：释放前 worker 在跑", runningBefore);
        ChecksContext.Check("成就会话：释放后不留常驻进程", !session.IsWorkerRunning);
        ChecksContext.Check("成就会话：释放后仍回落一次性宿主", result.Ok && session.FallbackCount == 1);
    }

    private static SteamHostSession NewSession(string host, TimeSpan? requestTimeout = null) =>
        new(
            new SilentLogger(),
            new SteamHostClient(new SilentLogger(), host),
            host,
            TimeSpan.FromSeconds(10),
            requestTimeout ?? TimeSpan.FromSeconds(5));

    private static SteamHostAchievementsResult Read(SteamHostSession session) =>
        session.ReadAchievementsAsync(FakeApiPath, 123, CancellationToken.None).GetAwaiter().GetResult();

    private static string NewStateDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "pdxnext-session-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static string? FindFakeHostPath()
    {
        var candidate = Path.Combine(AppContext.BaseDirectory, "PdxLauncher.Checks.exe");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        return Environment.ProcessPath is { } path
            && !string.Equals(Path.GetFileName(path), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            ? path
            : null;
    }

    /// <summary>临时设置假宿主的环境变量（子进程继承），Dispose 时恢复。</summary>
    private sealed class EnvScope : IDisposable
    {
        private readonly (string Name, string? Previous)[] _saved;

        public EnvScope(params (string Mode, string StateDirectory)[] settings)
        {
            var mode = settings[0].Mode;
            var state = settings[0].StateDirectory;
            _saved =
            [
                (FakeSteamHost.ModeVariable, Environment.GetEnvironmentVariable(FakeSteamHost.ModeVariable)),
                (FakeSteamHost.StateVariable, Environment.GetEnvironmentVariable(FakeSteamHost.StateVariable)),
            ];
            Environment.SetEnvironmentVariable(FakeSteamHost.ModeVariable, mode);
            Environment.SetEnvironmentVariable(FakeSteamHost.StateVariable, state);
        }

        public void Dispose()
        {
            foreach (var (name, previous) in _saved)
            {
                Environment.SetEnvironmentVariable(name, previous);
            }
        }
    }

    private sealed class SilentLogger : IAppLogger
    {
        public void Info(string message)
        {
        }


        public void Warn(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }
    }
}
