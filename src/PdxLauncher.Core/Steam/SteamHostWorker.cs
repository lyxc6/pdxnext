namespace PdxLauncher.Core.Steam;

using System.Collections.Concurrent;

/// <summary>
/// 常驻宿主（<c>--steam-host serve</c>）的会话循环：init 一次，之后每条命令复用同一个
/// <see cref="SteamAchievementsHostOperation"/>。
/// <para>
/// 一次性宿主每读一次成就都要重新 <c>SteamAPI_Init</c>（实测约 2.7 秒，全在 Steam 客户端侧），
/// 而 <c>ISteamUserStats</c> 的对象、回调和身份在同一个进程里可以一直用。所以这里把「读成就」
/// 拆出来复用：第一次命令付 init，后面每条命令只剩 <c>RequestUserStats</c> 的往返（几十到几百毫秒）。
/// </para>
/// <para>
/// 退出条件有三条：<see cref="SteamHostWorkerProtocol.ShutdownVerb"/>（正常退出）、stdin 关闭
/// （父进程没了）、空闲 <see cref="SteamHostWorkerProtocol.DefaultIdleTimeout"/>（兜底不留孤儿）。
/// 原生库的 <c>Shutdown</c> 在 <c>Dispose</c> 里做，且只做一次。
/// </para>
/// </summary>
public static class SteamHostWorker
{
    /// <summary>真实宿主入口：控制台 stdin/stdout，默认空闲超时。</summary>
    /// <param name="apiPath"><c>steam_api64.dll</c> 路径。</param>
    /// <param name="appId">要读成就的 Steam appid。</param>
    /// <param name="input">命令行输入（真实调用传 <see cref="Console.In"/>）。</param>
    /// <param name="output">结果行输出（真实调用传 <see cref="Console.Out"/>）。</param>
    /// <param name="error">诊断输出（真实调用传 <see cref="Console.Error"/>）。</param>
    public static int Run(string apiPath, int appId, TextReader input, TextWriter output, TextWriter error) =>
        Run(
            apiPath,
            appId,
            input,
            output,
            error,
            SteamHostWorkerProtocol.DefaultIdleTimeout,
            readerFactory: null);

    /// <summary>
    /// 会话循环本体。自检直接注入 <see cref="StringReader"/> 和假 reader，不必碰原生库。
    /// </summary>
    internal static int Run(
        string apiPath,
        int appId,
        TextReader input,
        TextWriter output,
        TextWriter error,
        TimeSpan idleTimeout,
        Func<string, int, ISteamHostWorkerAchievements>? readerFactory)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        var idle = idleTimeout > TimeSpan.Zero ? idleTimeout : SteamHostWorkerProtocol.DefaultIdleTimeout;

        // stdin 可能一直没人写：单独一条后台线程收行，主循环用带超时的 TryTake 等。
        // 这样「空闲 N 分钟」与「父进程关管道」都能从阻塞读里出来。
        using var lines = new BlockingCollection<string>(new ConcurrentQueue<string>());
        StartLinePump(input, lines);

        // 握手行：客户端必须先在启动超时内读到它，才把这条通道当成可用。
        output.WriteLine(SteamHostProtocol.Encode(new SteamHostWorkerHello { AppId = appId, ApiPath = apiPath }));
        output.Flush();

        ISteamHostWorkerAchievements achievements = readerFactory is null
            ? new SteamHostAchievementsSessionReader(apiPath, appId)
            : readerFactory(apiPath, appId);
        using var achievementsScope = achievements;

        while (true)
        {
            if (!lines.TryTake(out var line, idle))
            {
                // 管道关了（父进程退出）或真空闲到点了。两种情况都正常退出，不写结果行。
                try
                {
                    error.WriteLine("Steam 宿主 worker：空闲超时，退出");
                }
                catch
                {
                    // 没有 stderr 可写时忽略。
                }

                return SteamHostProtocol.ExitOk;
            }

            var status = SteamHostWorkerProtocol.TryParseCommand(line, out var command);
            if (status == SteamHostWorkerParseStatus.Ignore)
            {
                continue;
            }

            if (status == SteamHostWorkerParseStatus.Malformed)
            {
                WriteReply(output, SteamHostWorkerReply.Fail(
                    string.Empty,
                    SteamAchievementCodes.RequestFailed,
                    "无法解析的命令行（应为一行 JSON 或「动词 [超时毫秒]」）"));
                continue;
            }

            if (string.Equals(command.Verb, SteamHostWorkerProtocol.ShutdownVerb, StringComparison.Ordinal))
            {
                WriteReply(output, new SteamHostWorkerReply
                {
                    Verb = command.Verb,
                    Code = SteamAchievementCodes.Ok,
                    Ok = true,
                });
                return SteamHostProtocol.ExitOk;
            }

            if (string.Equals(command.Verb, SteamHostWorkerProtocol.PingVerb, StringComparison.Ordinal))
            {
                WriteReply(output, SteamHostWorkerReply.Pong());
                continue;
            }

            if (string.Equals(command.Verb, SteamHostProtocol.AchievementsVerb, StringComparison.Ordinal))
            {
                var timeout = SteamHostWorkerProtocol.ResolveTimeout(
                    command.TimeoutMs,
                    SteamHostRunner.AchievementsTimeout);
                SteamHostAchievementsResult result;
                try
                {
                    result = achievements.ReadAchievements(timeout);
                }
                catch (Exception ex)
                {
                    result = SteamHostAchievementsResult.Fail(
                        SteamAchievementCodes.HostFailed,
                        "读取成就时异常：" + ex.GetType().Name + "：" + ex.Message);
                }

                WriteReply(output, SteamHostWorkerReply.FromAchievements(command.Verb, result));
                continue;
            }

            WriteReply(output, SteamHostWorkerReply.Fail(
                command.Verb,
                SteamAchievementCodes.Unsupported,
                "常驻宿主不支持的动词：" + command.Verb));
        }
    }

    /// <summary>写一行结果。客户端只认前缀 + JSON，行内不能有换行。</summary>
    private static void WriteReply(TextWriter output, SteamHostWorkerReply reply)
    {
        output.WriteLine(SteamHostProtocol.Encode(reply));
        output.Flush();
    }

    /// <summary>把 stdin 的行搬进队列；EOF / 读异常都按「输入结束」处理。</summary>
    private static void StartLinePump(TextReader input, BlockingCollection<string> lines)
    {
        var thread = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = input.ReadLine()) is not null)
                {
                    lines.Add(line);
                }
            }
            catch
            {
                // 管道被父进程关掉（IOException / ObjectDisposedException）：按 EOF 处理。
            }
            finally
            {
                try
                {
                    lines.CompleteAdding();
                }
                catch (ObjectDisposedException)
                {
                    // 主循环已返回并释放了队列，泵线程此时才收尾：按「输入结束」处理即可。
                }
            }
        })
        {
            IsBackground = true,
            Name = "steam-host-stdin",
        };

        thread.Start();
    }

    /// <summary>
    /// 真实成就读取器：同一个 <see cref="SteamAchievementsHostOperation"/> 复用整场会话。
    /// </summary>
    internal sealed class SteamHostAchievementsSessionReader : ISteamHostWorkerAchievements
    {
        private readonly string _apiPath;
        private readonly int _appId;
        private SteamAchievementsHostOperation? _operation;

        public SteamHostAchievementsSessionReader(string apiPath, int appId)
        {
            _apiPath = apiPath;
            _appId = appId;
        }

        public SteamHostAchievementsResult ReadAchievements(TimeSpan timeout)
        {
            if (!File.Exists(_apiPath))
            {
                return SteamHostAchievementsResult.Fail(
                    SteamAchievementCodes.NoDll,
                    "找不到 steam_api64.dll：" + _apiPath);
            }

            try
            {
                // 首次命令才创建工作对象：worker 起来了但没人读成就时不必付 init。
                _operation ??= new SteamAchievementsHostOperation(_apiPath, _appId);
                return _operation.ReadAchievements(timeout);
            }
            catch (Exception ex)
            {
                return SteamHostAchievementsResult.Fail(
                    SteamAchievementCodes.HostFailed,
                    "读取成就时异常：" + ex.GetType().Name + "：" + ex.Message);
            }
        }

        public void Dispose()
        {
            _operation?.Dispose();
            _operation = null;
        }
    }
}

/// <summary>
/// 一次常驻会话里的成就读取器。抽成接口只为让自检能注入「不碰原生库」的假实现，
/// 顺便把「init 一次」的语义暴露成可断言的调用次数。
/// </summary>
public interface ISteamHostWorkerAchievements : IDisposable
{
    /// <summary>读当前账号的成就；超时返回 <see cref="SteamAchievementCodes.Timeout"/> 结果。</summary>
    SteamHostAchievementsResult ReadAchievements(TimeSpan timeout);
}
