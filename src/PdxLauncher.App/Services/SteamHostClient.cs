namespace PdxLauncher.App.Services;

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Steam;

/// <summary>
/// 拉起「Steam 宿主子进程」（<c>PdxLauncher.App.exe --steam-host …</c>）并收回它写的那行结果。
/// <para>
/// 所有原生 Steamworks 调用都经过这里，主启动器进程**从不**调 <c>SteamAPI_Init</c>：
/// Steam 只会把子进程登记成「游戏正在运行」，子进程做完事就退出，登记随之消失，
/// 于是 Steam 的「退出游戏」再也打不到启动器（历史事故：启动器被当成 vic3 的运行进程）。
/// </para>
/// </summary>
public sealed class SteamHostClient
{
    /// <summary>授权探测的超时（正常 1 秒内回来；Steam 卡住时不要拖死界面）。</summary>
    public static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(30);

    /// <summary>订阅 / 取消订阅的超时；宿主自己最多等 25 秒，这里再留余量。</summary>
    public static readonly TimeSpan WorkshopTimeout = TimeSpan.FromSeconds(45);

    /// <summary>发布上传的超时；宿主自己最多等 20 分钟。</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(20);

    /// <summary>拉订阅列表 / 批量动作的超时；宿主自己最多等 60 秒，这里再留余量。</summary>
    public static readonly TimeSpan WorkshopListTimeout = TimeSpan.FromSeconds(90);

    /// <summary>列 / 读「我自己发布的条目」的超时；宿主自己最多等 30 秒，这里再留余量。</summary>
    public static readonly TimeSpan OwnItemsTimeout = TimeSpan.FromSeconds(45);

    private readonly IAppLogger _logger;
    private readonly string _hostPath;

    /// <summary>构造。</summary>
    /// <param name="logger">日志。</param>
    /// <param name="hostPath">宿主可执行文件；默认就是当前进程自己的 exe。</param>
    public SteamHostClient(IAppLogger logger, string? hostPath = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hostPath = string.IsNullOrWhiteSpace(hostPath)
            ? ResolveDefaultHostPath()
            : hostPath!;
    }

    /// <summary>宿主可执行文件全路径。</summary>
    public string HostPath => _hostPath;

    /// <summary>宿主是否存在（不存在时所有 Steam 功能都应降级而不是报错）。</summary>
    public bool IsHostAvailable
    {
        get
        {
            try
            {
                return File.Exists(_hostPath);
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>跑一次 DLC 授权探测（阻塞调用方线程，最长 <see cref="ProbeTimeout"/>）。</summary>
    public SteamHostProbeResult Probe(string apiFilePath, int appId) =>
        ProbeAsync(apiFilePath, appId, CancellationToken.None).GetAwaiter().GetResult();

    /// <summary>跑一次 DLC 授权探测。</summary>
    public async Task<SteamHostProbeResult> ProbeAsync(string apiFilePath, int appId, CancellationToken cancellationToken)
    {
        var run = await ExecuteAsync(
            SteamHostProtocol.ProbeVerb,
            apiFilePath,
            appId,
            publishedFileId: null,
            ProbeTimeout,
            cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<SteamHostProbeResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return SteamHostProbeResult.Fail(SteamProbeError.HostFailed, Describe(run));
    }

    /// <summary>跑一次工坊操作（读状态 / 订阅 / 取消订阅）。</summary>
    public async Task<SteamHostWorkshopResult> WorkshopAsync(
        string verb,
        string apiFilePath,
        int appId,
        ulong publishedFileId,
        CancellationToken cancellationToken)
    {
        var run = await ExecuteAsync(verb, apiFilePath, appId, publishedFileId, WorkshopTimeout, cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<SteamHostWorkshopResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return SteamHostWorkshopResult.Fail(Describe(run));
    }

    /// <summary>
    /// 一次拉取当前账号在本游戏下订阅的全部工坊条目。
    /// <para>这是「每行起一个宿主进程」的替代：整张库一次拿回来。</para>
    /// </summary>
    /// <param name="apiFilePath">要加载的 <c>steam_api64.dll</c>。</param>
    /// <param name="appId">游戏的 Steam appid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>订阅列表结果；失败时带原因。</returns>
    public async Task<SteamHostSubscribedResult> ReadSubscribedAsync(
        string apiFilePath,
        int appId,
        CancellationToken cancellationToken)
    {
        var run = await ExecuteAsync(
            SteamHostProtocol.WorkshopSubscribedVerb,
            apiFilePath,
            appId,
            publishedFileId: null,
            WorkshopListTimeout,
            cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<SteamHostSubscribedResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return SteamHostSubscribedResult.Fail(Describe(run));
    }

    /// <summary>
    /// 列出当前账号在本游戏下**自己发布**的工坊条目。
    /// <para>与 <see cref="ReadSubscribedAsync"/> 是两回事：这边是作者视角，未公开条目也在。</para>
    /// </summary>
    /// <param name="apiFilePath">要加载的 <c>steam_api64.dll</c>。</param>
    /// <param name="appId">游戏的 Steam appid。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目 id 列表；失败时带中文原因。</returns>
    public async Task<SteamHostMineResult> ReadMineAsync(
        string apiFilePath,
        int appId,
        CancellationToken cancellationToken)
    {
        var run = await ExecuteAsync(
            SteamHostProtocol.WorkshopMineVerb,
            apiFilePath,
            appId,
            publishedFileId: null,
            OwnItemsTimeout,
            cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<SteamHostMineResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return SteamHostMineResult.Fail(Describe(run));
    }

    /// <summary>按 id 读自己某个工坊条目的详情（未公开条目也读得到）。</summary>
    /// <param name="apiFilePath">要加载的 <c>steam_api64.dll</c>。</param>
    /// <param name="appId">游戏的 Steam appid。</param>
    /// <param name="publishedFileId">工坊条目 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>详情；失败时带中文原因。</returns>
    public async Task<SteamHostMineItemResult> ReadMineItemAsync(
        string apiFilePath,
        int appId,
        ulong publishedFileId,
        CancellationToken cancellationToken)
    {
        var run = await ExecuteAsync(
            SteamHostProtocol.WorkshopMineDetailVerb,
            apiFilePath,
            appId,
            publishedFileId,
            OwnItemsTimeout,
            cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<SteamHostMineItemResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return SteamHostMineItemResult.Fail(publishedFileId, Describe(run));
    }

    /// <summary>对一批工坊条目执行同一个动作（下载 / 本地禁用 / 解除禁用 / 只读状态）。</summary>
    /// <param name="apiFilePath">要加载的 <c>steam_api64.dll</c>。</param>
    /// <param name="appId">游戏的 Steam appid。</param>
    /// <param name="request">动作与条目 id。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>逐条结果；失败时带原因。</returns>
    public async Task<WorkshopBatchResult> BatchAsync(
        string apiFilePath,
        int appId,
        WorkshopBatchRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var run = await ExecuteWithRequestAsync(
            SteamHostProtocol.WorkshopBatchVerb,
            apiFilePath,
            appId,
            request,
            WorkshopListTimeout,
            cancellationToken).ConfigureAwait(false);

        if (SteamHostProtocol.TryDecode<WorkshopBatchResult>(run.Stdout, out var payload) && payload is not null)
        {
            return payload;
        }

        return WorkshopBatchResult.Fail(request.Op, Describe(run));
    }

    /// <summary>创建一个工坊条目并上传内容；stdout 里的进度行会实时回调。</summary>
    public async Task<ModPublishResult> PublishAsync(
        string apiFilePath,
        int appId,
        ModPublishRequest request,
        Action<ModPublishProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var startInfo = new ProcessStartInfo(_hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request, SteamHostProtocol.JsonOptions);
        startInfo.ArgumentList.Add(SteamHostProtocol.VerbArgument);
        startInfo.ArgumentList.Add(SteamHostProtocol.WorkshopPublishVerb);
        startInfo.ArgumentList.Add("--api");
        startInfo.ArgumentList.Add(apiFilePath);
        startInfo.ArgumentList.Add("--appid");
        startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(PublishTimeout);

        Process? process = null;
        var lines = new List<string>();
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return ModPublishResult.Fail("宿主进程未能启动");
            }

            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);
            var stdoutTask = ReadPublishLinesAsync(process, lines, progress);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                if (cancellationToken.IsCancellationRequested)
                {
                    TryKill(process);
                    await SafeAwaitTask(stdoutTask).ConfigureAwait(false);
                    return ModPublishResult.Fail("已取消上传");
                }

                TryKill(process);
            }

            await SafeAwaitTask(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);
            stopwatch.Stop();
            _logger.Info(string.Concat(
                "Steam 宿主 ", SteamHostProtocol.WorkshopPublishVerb, "：exit=",
                timedOut ? "timeout" : process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "，耗时 ", stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), " ms",
                stderr.Length == 0 ? string.Empty : "，stderr=" + Trim(stderr)));

            var stdout = string.Join('\n', lines);
            if (SteamHostProtocol.TryDecode<ModPublishResult>(stdout, out var payload) && payload is not null)
            {
                return payload;
            }

            return ModPublishResult.Fail(Describe(new HostRun(
                timedOut ? -1 : process.ExitCode,
                stdout,
                stderr,
                timedOut,
                true)));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Warn("拉起 Steam 宿主失败（" + SteamHostProtocol.WorkshopPublishVerb + "）：" + ex.Message);
            return ModPublishResult.Fail("宿主进程异常：" + ex.Message);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static async Task ReadPublishLinesAsync(
        Process process,
        List<string> lines,
        Action<ModPublishProgress>? progress)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync().ConfigureAwait(false) is { } line)
            {
                if (SteamHostProtocol.TryDecodeProgress<ModPublishProgress>(line, out var payload) && payload is not null)
                {
                    try
                    {
                        progress?.Invoke(payload);
                    }
                    catch (Exception)
                    {
                        // 进度回调失败不能打断上传。
                    }

                    continue;
                }

                lines.Add(line);
            }
        }
        catch (Exception)
        {
            // 进程被杀时读端会抛，结果由最终行或 Describe 兜底。
        }
    }

    private static async Task SafeAwaitTask(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // 忽略读端异常。
        }
    }

    private async Task<HostRun> ExecuteAsync(
        string verb,
        string apiFilePath,
        int appId,
        ulong? publishedFileId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(_hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        startInfo.ArgumentList.Add(SteamHostProtocol.VerbArgument);
        startInfo.ArgumentList.Add(verb);
        startInfo.ArgumentList.Add("--api");
        startInfo.ArgumentList.Add(apiFilePath);
        startInfo.ArgumentList.Add("--appid");
        startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (publishedFileId is { } fileId)
        {
            startInfo.ArgumentList.Add("--fileid");
            startInfo.ArgumentList.Add(fileId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new HostRun(SteamHostProtocol.ExitFailure, string.Empty, "进程未能启动", false, false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                TryKill(process);
            }

            var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.Info(string.Concat(
                "Steam 宿主 ", verb, "：exit=", timedOut ? "timeout" : process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "，耗时 ", stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), " ms",
                stderr.Length == 0 ? string.Empty : "，stderr=" + Trim(stderr)));

            return new HostRun(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut, true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Warn("拉起 Steam 宿主失败（" + verb + "）：" + ex.Message);
            return new HostRun(SteamHostProtocol.ExitFailure, string.Empty, ex.Message, false, false);
        }
        finally
        {
            process?.Dispose();
        }
    }

    /// <summary>
    /// 跑一个「请求体走 <c>--request</c>」的宿主动作，不读进度行。
    /// <para>与 <see cref="ExecuteAsync"/> 同一套起进程 / 超时 / 日志口径，只是多一个 base64 请求参数。</para>
    /// </summary>
    private async Task<HostRun> ExecuteWithRequestAsync<TRequest>(
        string verb,
        string apiFilePath,
        int appId,
        TRequest request,
        TimeSpan timeout,
        CancellationToken cancellationToken)
        where TRequest : class
    {
        var startInfo = new ProcessStartInfo(_hostPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(request, SteamHostProtocol.JsonOptions);
        startInfo.ArgumentList.Add(SteamHostProtocol.VerbArgument);
        startInfo.ArgumentList.Add(verb);
        startInfo.ArgumentList.Add("--api");
        startInfo.ArgumentList.Add(apiFilePath);
        startInfo.ArgumentList.Add("--appid");
        startInfo.ArgumentList.Add(appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("--request");
        startInfo.ArgumentList.Add(Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));

        var stopwatch = Stopwatch.StartNew();
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        Process? process = null;
        try
        {
            process = new Process { StartInfo = startInfo };
            if (!process.Start())
            {
                return new HostRun(SteamHostProtocol.ExitFailure, string.Empty, "进程未能启动", false, false);
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
            var stderrTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

            var timedOut = false;
            try
            {
                await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                timedOut = !cancellationToken.IsCancellationRequested;
                TryKill(process);
            }

            var stdout = await SafeAwait(stdoutTask).ConfigureAwait(false);
            var stderr = await SafeAwait(stderrTask).ConfigureAwait(false);
            stopwatch.Stop();

            _logger.Info(string.Concat(
                "Steam 宿主 ", verb, "：exit=", timedOut ? "timeout" : process.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "，耗时 ", stopwatch.ElapsedMilliseconds.ToString(System.Globalization.CultureInfo.InvariantCulture), " ms",
                stderr.Length == 0 ? string.Empty : "，stderr=" + Trim(stderr)));

            return new HostRun(timedOut ? -1 : process.ExitCode, stdout, stderr, timedOut, true);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.Warn("拉起 Steam 宿主失败（" + verb + "）：" + ex.Message);
            return new HostRun(SteamHostProtocol.ExitFailure, string.Empty, ex.Message, false, false);
        }
        finally
        {
            process?.Dispose();
        }
    }

    private static string Describe(HostRun run)
    {
        if (!run.Started)
        {
            return "宿主进程未能启动：" + run.Stdout;
        }

        if (run.TimedOut)
        {
            return "宿主进程超时未返回结果";
        }

        var stderr = run.Stderr.Trim();
        var text = stderr.Length > 0 ? stderr : (run.Stdout.Trim().Length > 0 ? run.Stdout.Trim() : "无输出");
        return "宿主进程返回 " + run.ExitCode.ToString(System.Globalization.CultureInfo.InvariantCulture) + "：" + Trim(text);
    }

    private static string Trim(string text)
    {
        var single = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return single.Length <= 300 ? single : single.Substring(0, 300) + "…";
    }

    private static async Task<string> SafeAwait(Task<string> task)
    {
        try
        {
            return await task.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception)
        {
            // 进程可能刚好自己退出了。
        }
    }

    private static string ResolveDefaultHostPath()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath))
        {
            return processPath!;
        }

        return Path.Combine(AppContext.BaseDirectory, "PdxLauncher.App.exe");
    }

    private readonly record struct HostRun(int ExitCode, string Stdout, string Stderr, bool TimedOut, bool Started);
}
