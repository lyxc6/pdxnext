namespace PdxLauncher.App.Services;

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 常驻 Steam 宿主会话：一条 <c>--steam-host serve</c> 子进程 + 一串请求/回复。
/// <para>
/// 为什么需要它：一次性宿主每次读成就都要重新 <c>SteamAPI_Init</c>（实测约 2.7 秒，耗在 Steam
/// 客户端侧），连点两次成就页就要付两次。常驻 worker 把 init 摊销到整场会话里，第二条命令只剩
/// <c>RequestUserStats</c> 的往返。
/// </para>
/// <para>
/// 生命周期口径：
/// <list type="bullet">
/// <item>请求**串行**排队：steamworks 的回调分发不是线程安全的，并发发命令只会拿到串号的结果。</item>
/// <item>身份是 (dll 路径, appid)：换游戏或换手填 DLL 就换一条 worker，旧进程先 Kill。</item>
/// <item>崩溃 / 无响应：Kill 后**重启一次**；再失败就回落一次性宿主调用，页面行为不退化。</item>
/// <item>退出：<see cref="Dispose"/> 先发 shutdown 再 Kill，绝不留孤儿进程。</item>
/// </list>
/// </para>
/// <para>
/// 本类只依赖 <see cref="SteamHostClient"/> 与 Core 类型，不碰 UI；自检把它链接进 net10.0 工程，
/// 用「自检 exe 充当假 worker」跑复用 / 崩溃 / 版本不匹配的用例。
/// </para>
/// </summary>
public sealed class SteamHostSession : IDisposable
{
    /// <summary>worker 启动后等握手行的上限；超过就按「起不来」处理并回落。</summary>
    public static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 单条命令的上限。worker 内部读成就最多等 10 秒（<see cref="SteamHostRunner.AchievementsTimeout"/>），
    /// 这里多留 2 秒给进程调度与管道往返。
    /// </summary>
    public static readonly TimeSpan DefaultRequestTimeout = TimeSpan.FromSeconds(12);

    private readonly IAppLogger _logger;
    private readonly SteamHostClient _fallback;
    private readonly string _hostPath;
    private readonly TimeSpan _startupTimeout;
    private readonly TimeSpan _requestTimeout;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private Worker? _worker;
    private bool _restartUsed;
    private bool _disposed;
    private int _workerStarts;
    private int _restartCount;
    private int _fallbackCount;

    /// <summary>构造。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="fallback">回落用的一次性宿主客户端；为空时按同一宿主路径新建一个。</param>
    /// <param name="hostPath">宿主可执行文件；为空时用 <paramref name="fallback"/> 的路径。</param>
    /// <param name="startupTimeout">握手超时（自检可缩短）。</param>
    /// <param name="requestTimeout">单条命令超时（自检可缩短）。</param>
    public SteamHostSession(
        IAppLogger logger,
        SteamHostClient? fallback = null,
        string? hostPath = null,
        TimeSpan? startupTimeout = null,
        TimeSpan? requestTimeout = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fallback = fallback ?? new SteamHostClient(logger, hostPath);
        _hostPath = hostPath ?? _fallback.HostPath;
        _startupTimeout = startupTimeout is { Ticks: > 0 } startup ? startup : DefaultStartupTimeout;
        _requestTimeout = requestTimeout is { Ticks: > 0 } request ? request : DefaultRequestTimeout;
    }

    /// <summary>宿主可执行文件路径。</summary>
    public string HostPath => _hostPath;

    /// <summary>当前是否有一条活着的 worker。</summary>
    public bool IsWorkerRunning => Volatile.Read(ref _worker) is { IsAlive: true };

    /// <summary>本会话启动过多少次 worker（自检断言「复用」用）。</summary>
    public int WorkerStarts => Volatile.Read(ref _workerStarts);

    /// <summary>崩溃 / 无响应触发过多少次重启（自检断言「只重启一次」用）。</summary>
    public int RestartCount => Volatile.Read(ref _restartCount);

    /// <summary>回落到一次性调用的次数。</summary>
    public int FallbackCount => Volatile.Read(ref _fallbackCount);

    /// <summary>单条命令超时。</summary>
    public TimeSpan RequestTimeout => _requestTimeout;

    /// <summary>
    /// 读成就。会话不可用（起不来 / 版本不符 / 重启后仍失败）时自动回落一次性宿主，
    /// 返回值语义与 <see cref="SteamHostClient.AchievementsAsync"/> 完全一致。
    /// </summary>
    public async Task<SteamHostAchievementsResult> ReadAchievementsAsync(
        string apiFilePath,
        int appId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiFilePath);
        if (appId <= 0)
        {
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.RequestFailed,
                "非法的 Steam appid：" + appId);
        }

        // 回调分发天然串行：并发发两条命令会拿串号的结果，所以在会话层排队。
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // 「重启一次」的预算按单条命令算：同一调用里最多重启一次，之后回落一次性宿主。
            // 不能放进下面的 worker 为空的启动分支——否则每次重启成功都会把预算清零，
            // 遇到持续坏回包就会无限重启 worker（自检里曾长时间挂住）。
            _restartUsed = false;

            if (_disposed)
            {
                _logger.Warn("成就会话已释放，改用一次性宿主（appid " + appId + "）");
                return await FallbackAsync(apiFilePath, appId, cancellationToken).ConfigureAwait(false);
            }

            while (true)
            {
                var worker = Volatile.Read(ref _worker);
                if (worker is not null && !worker.Matches(apiFilePath, appId))
                {
                    _logger.Info("Steam 宿主会话身份变化（appid " + worker.AppId + " → " + appId + "），重启 worker");
                    KillWorker("身份变化");
                    worker = null;
                }

                if (worker is null)
                {
                    worker = await Task.Run(() => StartWorker(apiFilePath, appId), cancellationToken)
                        .ConfigureAwait(false);
                    if (worker is null)
                    {
                        return await FallbackAsync(apiFilePath, appId, cancellationToken).ConfigureAwait(false);
                    }
                }

                var reply = await worker.RequestAsync(SteamHostProtocol.AchievementsVerb, cancellationToken)
                    .ConfigureAwait(false);
                if (reply is not null)
                {
                    return reply.Achievements
                        ?? SteamHostAchievementsResult.Fail(
                            SteamAchievementCodes.HostFailed,
                            "Steam 宿主 worker 回包缺少成就数据");
                }

                KillWorker("请求失败");
                if (_restartUsed)
                {
                    _logger.Warn("Steam 宿主 worker 重启一次后仍失败，回落一次性调用（appid " + appId + "）");
                    return await FallbackAsync(apiFilePath, appId, cancellationToken).ConfigureAwait(false);
                }

                _restartUsed = true;
                Interlocked.Increment(ref _restartCount);
                _logger.Warn("Steam 宿主 worker 崩溃或无响应，重启一次（appid " + appId + "）");
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>提前结束 worker（App 退出 / 设置变化时调用）；之后仍可回落一次性调用。</summary>
    public void Dispose()
    {
        _disposed = true;
        KillWorker("会话释放");
    }

    private async Task<SteamHostAchievementsResult> FallbackAsync(
        string apiFilePath,
        int appId,
        CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _fallbackCount);

        try
        {
            return await _fallback.AchievementsAsync(apiFilePath, appId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("回落一次性宿主仍失败（appid " + appId + "）：" + ex.GetType().Name + "：" + ex.Message);
            return SteamHostAchievementsResult.Fail(
                SteamAchievementCodes.HostFailed,
                "回落一次性宿主失败：" + ex.Message);
        }
    }

    private Worker? StartWorker(string apiFilePath, int appId)
    {
        if (!File.Exists(_hostPath))
        {
            _logger.Warn("找不到 Steam 宿主程序，无法启动常驻 worker：" + _hostPath);
            return null;
        }

        Process? process = null;
        StreamWriter? stdin = null;
        try
        {
            var startInfo = new ProcessStartInfo(_hostPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardOutputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                StandardErrorEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                WorkingDirectory = AppContext.BaseDirectory,
            };
            startInfo.ArgumentList.Add(SteamHostProtocol.VerbArgument);
            startInfo.ArgumentList.Add(SteamHostProtocol.ServeVerb);
            startInfo.ArgumentList.Add("--api");
            startInfo.ArgumentList.Add(apiFilePath);
            startInfo.ArgumentList.Add("--appid");
            startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));

            process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            var stderr = new StringBuilder();
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (stderr)
                {
                    if (stderr.Length < 4096)
                    {
                        stderr.AppendLine(e.Data);
                    }
                }
            };

            if (!process.Start())
            {
                _logger.Warn("Steam 宿主 worker 进程未能启动：" + _hostPath);
                return null;
            }

            process.BeginErrorReadLine();
            stdin = process.StandardInput;
            stdin.AutoFlush = true;

            var lines = new BlockingCollection<string>(new ConcurrentQueue<string>());
            var worker = new Worker(this, process, stdin, lines, stderr, apiFilePath, appId);
            stdin = null; // 所有权移交给 worker，失败时由 worker 释放。
            Volatile.Write(ref _worker, worker);
            Interlocked.Increment(ref _workerStarts);

            if (!worker.TryReadHello(_startupTimeout))
            {
                _logger.Warn("Steam 宿主 worker 握手失败（" + _hostPath + "），回落一次性调用");
                KillWorker("握手失败");
                return null;
            }

            _logger.Info("Steam 宿主 worker 已就绪（appid " + appId + "，PID " + worker.ProcessId + "）");
            return worker;
        }
        catch (Exception ex)
        {
            _logger.Warn("启动 Steam 宿主 worker 失败：" + ex.GetType().Name + "：" + ex.Message);
            KillWorker("启动异常");
            try
            {
                stdin?.Dispose();
            }
            catch
            {
                // 忽略。
            }

            process?.Dispose();
            return null;
        }
    }

    /// <summary>结束当前 worker；重复调用安全。</summary>
    private void KillWorker(string reason)
    {
        var worker = Interlocked.Exchange(ref _worker, null);
        if (worker is null)
        {
            return;
        }

        try
        {
            worker.Dispose();
        }
        catch (Exception ex)
        {
            _logger.Warn("结束 Steam 宿主 worker 时出错（" + reason + "）：" + ex.GetType().Name + "：" + ex.Message);
        }
    }

    /// <summary>预读 stdout 的线程：BlockingCollection 让请求方带超时等一行，EOF 用 CompleteAdding 表达。</summary>
    private static void PumpLines(StreamReader reader, BlockingCollection<string> lines)
    {
        var thread = new Thread(() =>
        {
            try
            {
                string? line;
                while ((line = reader.ReadLine()) is not null)
                {
                    lines.Add(line);
                }
            }
            catch
            {
                // 进程被杀 / 管道关闭：按 EOF 处理。
            }
            finally
            {
                lines.CompleteAdding();
            }
        })
        {
            IsBackground = true,
            Name = "steam-host-worker-stdout",
        };

        thread.Start();
    }

    /// <summary>一条常驻宿主进程。</summary>
    private sealed class Worker : IDisposable
    {
        private readonly SteamHostSession _owner;
        private readonly Process _process;
        private readonly StreamWriter _stdin;
        private readonly BlockingCollection<string> _lines;
        private readonly StringBuilder _stderr;
        private readonly string _apiFilePath;
        private int _disposed;

        public Worker(
            SteamHostSession owner,
            Process process,
            StreamWriter stdin,
            BlockingCollection<string> lines,
            StringBuilder stderr,
            string apiFilePath,
            int appId)
        {
            _owner = owner;
            _process = process;
            _stdin = stdin;
            _lines = lines;
            _stderr = stderr;
            _apiFilePath = apiFilePath;
            AppId = appId;
            PumpLines(process.StandardOutput, lines);
        }

        public int AppId { get; }

        public int ProcessId => _process.Id;

        public bool IsAlive
        {
            get
            {
                try
                {
                    return !_process.HasExited;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>身份是否仍是同一条会话要服务的 (dll, appid)。</summary>
        public bool Matches(string apiFilePath, int appId) =>
            appId == AppId && string.Equals(apiFilePath, _apiFilePath, StringComparison.OrdinalIgnoreCase);

        /// <summary>读握手行并校验协议版本；超时 / 版本不符都返回 false。</summary>
        public bool TryReadHello(TimeSpan timeout)
        {
            var line = Task.Run(() => _lines.TryTake(out var first, timeout) ? first : null)
                .GetAwaiter()
                .GetResult();
            if (line is null)
            {
                _owner._logger.Warn("Steam 宿主 worker 未在 " + (int)timeout.TotalSeconds + " 秒内送来握手行");
                return false;
            }

            if (!SteamHostProtocol.TryDecode<SteamHostWorkerHello>(line, out var hello) || hello is null)
            {
                _owner._logger.Warn("Steam 宿主 worker 握手行无法解析：" + Truncate(line));
                return false;
            }

            if (hello.Version != SteamHostWorkerProtocol.Version)
            {
                _owner._logger.Warn(
                    "Steam 宿主 worker 协议版本不匹配（App " + SteamHostWorkerProtocol.Version + "，worker " + hello.Version + "），回落一次性调用");
                return false;
            }

            return true;
        }

        /// <summary>发一条命令并等一行回复；超时 / EOF / 坏 JSON / 版本不符都返回 null（调用方据此重启或回落）。</summary>
        public async Task<SteamHostWorkerReply?> RequestAsync(string verb, CancellationToken cancellationToken)
        {
            if (!IsAlive)
            {
                return null;
            }

            var request = JsonSerializer.Serialize(
                new SteamHostWorkerRequest
                {
                    Verb = verb,
                    TimeoutMs = (int)_owner._requestTimeout.TotalMilliseconds,
                },
                SteamHostProtocol.JsonOptions);

            try
            {
                await _stdin.WriteLineAsync(request.AsMemory(), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Dispose();
                throw;
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                _owner._logger.Warn("向 Steam 宿主 worker 写命令失败：" + ex.GetType().Name + "：" + ex.Message);
                return null;
            }

            string? replyLine;
            try
            {
                replyLine = await Task.Run(
                        () => _lines.TryTake(
                                out var line,
                                (int)Math.Max(0, _owner._requestTimeout.TotalMilliseconds),
                                cancellationToken)
                            ? line
                            : null,
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // 调用方取消（切游戏 / 离开页面）：这条 worker 里还压着未回的命令，杀掉重来最干净。
                Dispose();
                throw;
            }

            if (replyLine is null)
            {
                _owner._logger.Warn("Steam 宿主 worker 未在 " + (int)_owner._requestTimeout.TotalSeconds + " 秒内回复（appid " + AppId + "）" + StderrSuffix());
                return null;
            }

            if (!SteamHostProtocol.TryDecode<SteamHostWorkerReply>(replyLine, out var reply) || reply is null)
            {
                _owner._logger.Warn("Steam 宿主 worker 回复无法解析：" + Truncate(replyLine) + StderrSuffix());
                return null;
            }

            if (reply.Version != SteamHostWorkerProtocol.Version)
            {
                _owner._logger.Warn("Steam 宿主 worker 回复协议版本不一致（" + reply.Version + "），按失败处理");
                return null;
            }

            if (!string.Equals(reply.Verb, verb, StringComparison.Ordinal))
            {
                _owner._logger.Warn("Steam 宿主 worker 回复动词串号：期望 " + verb + "，实际 " + reply.Verb);
                return null;
            }

            return reply;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (!_process.HasExited)
                {
                    // 先礼后兵：shutdown 让 Steam 走正常退出路径，Kill 兜底。
                    try
                    {
                        _stdin.WriteLine(JsonSerializer.Serialize(
                            new SteamHostWorkerRequest { Verb = SteamHostWorkerProtocol.ShutdownVerb },
                            SteamHostProtocol.JsonOptions));
                        _stdin.Flush();
                    }
                    catch
                    {
                        // 进程已经死了：直接 Kill。
                    }

                    if (!_process.WaitForExit(500))
                    {
                        _process.Kill(entireProcessTree: true);
                    }
                }
            }
            catch
            {
                // 退出路径上的异常不再上报。
            }

            try
            {
                _stdin.Dispose();
            }
            catch
            {
                // 忽略。
            }

            try
            {
                _process.WaitForExit(2000);
            }
            catch
            {
                // 忽略。
            }

            _process.Dispose();
            _lines.Dispose();
        }

        private string StderrSuffix()
        {
            lock (_stderr)
            {
                return _stderr.Length == 0 ? string.Empty : "，stderr=" + Truncate(_stderr.ToString());
            }
        }

        private static string Truncate(string text)
        {
            var flat = text.Replace("\r", " ").Replace("\n", " ").Trim();
            return flat.Length <= 300 ? flat : flat[..300] + "…";
        }
    }
}
