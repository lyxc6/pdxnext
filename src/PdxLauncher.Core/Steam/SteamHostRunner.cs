namespace PdxLauncher.Core.Steam;

using System.Globalization;
using System.Text;
using System.Text.Json;

/// <summary>
/// <c>--steam-host</c> 子进程的入口：解析命令行、执行一次原生 Steamworks 操作、写一行结果。
/// <para>
/// 进程生命周期 = 一次操作：初始化 → 干活 → <c>Shutdown</c> → 退出。Steam 那边的登记随进程退出
/// 自动消失，所以主启动器永远不会挂在这条登记上被「退出游戏」带走。
/// </para>
/// </summary>
public static class SteamHostRunner
{
    /// <summary>订阅 / 取消订阅时最多等多久让 Steam 确认状态翻转。</summary>
    public static readonly TimeSpan WorkshopConfirmTimeout = TimeSpan.FromSeconds(25);

    /// <summary>发布上传最多等多久（Steam 上传大 mod 可能很久）。</summary>
    public static readonly TimeSpan PublishTimeout = TimeSpan.FromMinutes(20);

    /// <summary>拉订阅列表 / 批量动作最多等多久（订阅库大时拿状态要一条条读）。</summary>
    public static readonly TimeSpan WorkshopListTimeout = TimeSpan.FromSeconds(60);

    /// <summary>列出 / 读取自己发布的条目最多等多久（每页 50 条，纯内存往返，用不了这么久）。</summary>
    public static readonly TimeSpan OwnItemsTimeout = TimeSpan.FromSeconds(30);

    /// <summary>运行一次宿主操作，返回进程退出码。</summary>
    /// <param name="args">完整命令行参数（含 <c>--steam-host</c>）。</param>
    /// <param name="stdout">结果输出（真实调用传 <see cref="Console.Out"/>）。</param>
    /// <param name="stderr">错误输出。</param>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!SteamHostProtocol.TryGetVerb(args, out var verb))
        {
            stderr.WriteLine("用法：--steam-host probe|workshop-state|workshop-subscribe|workshop-unsubscribe|workshop-publish|workshop-subscribed|workshop-batch|workshop-mine|workshop-mine-detail --api <dll> --appid <int> [--fileid <ulong>] [--request <base64 json>]");
            return SteamHostProtocol.ExitFailure;
        }

        if (!TryGetOption(args, "--api", out var apiPath) || string.IsNullOrWhiteSpace(apiPath))
        {
            stderr.WriteLine("缺少 --api <steam_api64.dll 路径>");
            return SteamHostProtocol.ExitFailure;
        }

        if (!TryGetOption(args, "--appid", out var appIdText) ||
            !int.TryParse(appIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var appId) ||
            appId <= 0)
        {
            stderr.WriteLine("缺少或非法的 --appid <Steam appid>");
            return SteamHostProtocol.ExitFailure;
        }

        if (verb == SteamHostProtocol.ProbeVerb)
        {
            return RunProbe(apiPath!, appId, stdout);
        }

        if (verb == SteamHostProtocol.WorkshopPublishVerb)
        {
            return RunPublish(apiPath!, appId, args, stdout, stderr);
        }

        if (verb == SteamHostProtocol.WorkshopSubscribedVerb)
        {
            return RunSubscribed(apiPath!, appId, stdout);
        }

        if (verb == SteamHostProtocol.WorkshopBatchVerb)
        {
            return RunBatch(apiPath!, appId, args, stdout, stderr);
        }

        if (verb == SteamHostProtocol.WorkshopMineVerb)
        {
            return RunMine(apiPath!, appId, stdout);
        }

        if (verb == SteamHostProtocol.WorkshopMineDetailVerb)
        {
            return RunMineDetail(apiPath!, appId, args, stdout, stderr);
        }

        return RunWorkshop(verb, apiPath!, appId, args, stdout, stderr);
    }

    private static int RunProbe(string apiPath, int appId, TextWriter stdout)
    {
        var payload = RunProbeCore(apiPath, appId);
        WriteResult(stdout, payload);
        return SteamHostProtocol.ExitOk;
    }

    /// <summary>真正做一次授权探测；单独抽出来便于自检直接调用。</summary>
    internal static SteamHostProbeResult RunProbeCore(string apiPath, int appId)
    {
        if (!File.Exists(apiPath))
        {
            return SteamHostProbeResult.Fail(SteamProbeError.NoDll, apiPath);
        }

        try
        {
            var result = new SteamDllProbe().Probe(apiPath, appId);
            return new SteamHostProbeResult(
                result.Initialized,
                result.SteamRunning,
                result.SteamId64,
                result.Dlcs,
                result.Error,
                result.ErrorDetail);
        }
        catch (Exception ex)
        {
            return SteamHostProbeResult.Fail(SteamProbeError.HostFailed, ex.GetType().Name + "：" + ex.Message);
        }
    }

    private static int RunPublish(string apiPath, int appId, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryGetOption(args, "--request", out var encoded) || string.IsNullOrWhiteSpace(encoded))
        {
            stderr.WriteLine("发布缺少 --request <base64 json>");
            return SteamHostProtocol.ExitFailure;
        }

        ModPublishRequest? request;
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded!));
            request = JsonSerializer.Deserialize<ModPublishRequest>(json, SteamHostProtocol.JsonOptions);
        }
        catch (Exception ex)
        {
            stderr.WriteLine("无法解析 --request：" + ex.Message);
            return SteamHostProtocol.ExitFailure;
        }

        if (request is null || string.IsNullOrWhiteSpace(request.ContentPath))
        {
            stderr.WriteLine("--request 缺少内容目录");
            return SteamHostProtocol.ExitFailure;
        }

        if (!Enum.IsDefined(request.Visibility))
        {
            stderr.WriteLine("--request 里的可见性值非法：" + (int)request.Visibility);
            return SteamHostProtocol.ExitFailure;
        }

        // 每种附加语言都是一次独立的 Steam 提交，超时按语言数线性放大。
        var extraLanguages = Math.Min(request.Localizations?.Count ?? 0, ModPublishLimits.MaxLocalizations);
        var timeout = PublishTimeout * (1 + extraLanguages);

        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, ModPublishResult.Fail("找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            var result = operation.Publish(
                request,
                progress =>
                {
                    stdout.WriteLine(SteamHostProtocol.EncodeProgress(progress));
                    stdout.Flush();
                },
                timeout);
            WriteResult(stdout, result);
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, ModPublishResult.Fail(ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    /// <summary>拉取当前账号在本游戏下的全部订阅条目。</summary>
    private static int RunSubscribed(string apiPath, int appId, TextWriter stdout)
    {
        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, SteamHostSubscribedResult.Fail("找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            WriteResult(stdout, operation.ReadSubscribed(WorkshopListTimeout));
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, SteamHostSubscribedResult.Fail(ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    /// <summary>对一批条目做同一个动作（下载 / 本地禁用 / 解除禁用 / 只读状态）。</summary>
    private static int RunBatch(string apiPath, int appId, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryDecodeRequest<WorkshopBatchRequest>(args, out var request) || request is null)
        {
            stderr.WriteLine("批量动作缺少 --request <base64 json>");
            return SteamHostProtocol.ExitFailure;
        }

        var op = request.Op ?? string.Empty;
        if (!WorkshopBatchOps.All.Contains(op))
        {
            stderr.WriteLine("未知的 --op：" + op);
            return SteamHostProtocol.ExitFailure;
        }

        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, WorkshopBatchResult.Fail(op, "找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            WriteResult(stdout, operation.RunBatch(request, WorkshopListTimeout));
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, WorkshopBatchResult.Fail(op, ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    /// <summary>列出当前账号在本游戏下自己发布的工坊条目（含未公开条目）。</summary>
    private static int RunMine(string apiPath, int appId, TextWriter stdout)
    {
        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, SteamHostMineResult.Fail("找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            WriteResult(stdout, operation.ReadOwnPublishedItems(OwnItemsTimeout));
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, SteamHostMineResult.Fail(ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    /// <summary>按 id 读自己某个工坊条目的详情（未公开条目也读得到）。</summary>
    private static int RunMineDetail(string apiPath, int appId, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryGetOption(args, "--fileid", out var fileIdText) ||
            !ulong.TryParse(fileIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId) ||
            fileId == 0)
        {
            stderr.WriteLine("缺少或非法的 --fileid <工坊条目 id>");
            return SteamHostProtocol.ExitFailure;
        }

        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, SteamHostMineItemResult.Fail(fileId, "找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            WriteResult(stdout, operation.ReadOwnPublishedItem(fileId, OwnItemsTimeout));
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, SteamHostMineItemResult.Fail(fileId, ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    /// <summary>把 <c>--request</c> 的 base64 JSON 解成请求对象；格式不对返回 false。</summary>
    private static bool TryDecodeRequest<T>(IReadOnlyList<string> args, out T? request)
        where T : class
    {
        request = null;
        if (!TryGetOption(args, "--request", out var encoded) || string.IsNullOrWhiteSpace(encoded))
        {
            return false;
        }

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(encoded!));
            request = JsonSerializer.Deserialize<T>(json, SteamHostProtocol.JsonOptions);
            return request is not null;
        }
        catch
        {
            return false;
        }
    }

    private static int RunWorkshop(string verb, string apiPath, int appId, IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        if (!TryGetOption(args, "--fileid", out var fileIdText) ||
            !ulong.TryParse(fileIdText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var fileId) ||
            fileId == 0)
        {
            stderr.WriteLine("缺少或非法的 --fileid <工坊条目 id>");
            return SteamHostProtocol.ExitFailure;
        }

        if (!File.Exists(apiPath))
        {
            WriteResult(stdout, SteamHostWorkshopResult.Fail("找不到 steam_api64.dll：" + apiPath));
            return SteamHostProtocol.ExitOk;
        }

        try
        {
            using var operation = new SteamWorkshopHostOperation(apiPath, appId);
            var payload = verb switch
            {
                SteamHostProtocol.WorkshopStateVerb => operation.ReadState(fileId),
                SteamHostProtocol.WorkshopSubscribeVerb => operation.SetSubscribed(fileId, subscribe: true, WorkshopConfirmTimeout),
                _ => operation.SetSubscribed(fileId, subscribe: false, WorkshopConfirmTimeout),
            };

            WriteResult(stdout, payload);
            return SteamHostProtocol.ExitOk;
        }
        catch (Exception ex)
        {
            WriteResult(stdout, SteamHostWorkshopResult.Fail(ex.GetType().Name + "：" + ex.Message));
            return SteamHostProtocol.ExitOk;
        }
    }

    private static void WriteResult<T>(TextWriter stdout, T payload)
    {
        stdout.WriteLine(SteamHostProtocol.Encode(payload));
        stdout.Flush();
    }

    /// <summary>取 <c>--name value</c> 形式的选择项。</summary>
    private static bool TryGetOption(IReadOnlyList<string> args, string name, out string? value)
    {
        value = null;
        for (var i = 0; i < args.Count - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            {
                value = args[i + 1];
                return true;
            }
        }

        return false;
    }
}
