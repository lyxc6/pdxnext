namespace PdxLauncher.Core.Steam;

using System.Text.Json;

/// <summary>
/// 常驻宿主（<c>--steam-host serve</c>）的行协议。
/// <para>
/// 一次性宿主是「一个进程干一件事」，常驻宿主则把生命期拉长到整个启动器会话：进程启动后
/// <c>SteamAPI_Init</c> 只做一次，之后每来一行命令就回一行结果。渠道是 stdin/stdout，
/// 两边都是 UTF-8、一行一条 JSON，谁都不许在行中间换行。
/// </para>
/// <para>
/// 为什么不复用 <c>--request &lt;base64&gt;</c>：命令行走一次的参数有 32 KB 上限，而常驻宿主
/// 要跑整场会话，重复拼命令行既慢又容易被后续的「发进度」需求撑爆。行协议还带来一个好处：
/// 客户端可以只读结果行，worker 往 stderr 写诊断不会污染结果。
/// </para>
/// </summary>
public static class SteamHostWorkerProtocol
{
    /// <summary>
    /// 协议版本。客户端拿到 <see cref="SteamHostWorkerHello"/> 后必须比对；不一致就杀掉
    /// worker 回落一次性调用 —— 混版本的行格式会静默错位，比慢一点严重得多。
    /// </summary>
    public const int Version = 1;

    /// <summary>探活：不回读 Steam，只回 pong，用来判断常驻进程是否还活着。</summary>
    public const string PingVerb = "ping";

    /// <summary>让常驻进程回一行 ok 后退出；启动器退出时先礼后兵，Kill 是兜底。</summary>
    public const string ShutdownVerb = "shutdown";

    /// <summary>空闲多久没有命令就自行退出（父进程被强杀时不留孤儿进程）。</summary>
    public static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromMinutes(10);

    /// <summary>单条命令最多允许客户端要求等多久；超过这个数按超时上限处理。</summary>
    public static readonly TimeSpan MaxRequestTimeout = TimeSpan.FromMinutes(2);

    /// <summary>
    /// 解析一行命令。
    /// <para>
    /// 空行返回 <see cref="SteamHostWorkerParseStatus.Ignore"/>（不回复，避免心跳/回车键的噪声
    /// 被当成失败）；以 <c>{</c> 开头按 JSON 解，坏了返回 <see cref="SteamHostWorkerParseStatus.Malformed"/>，
    /// 其余按「裸动词 [毫秒超时]」解，方便手工 <c>echo achievements | PdxLauncher.App.exe --steam-host serve …</c> 排障。
    /// </para>
    /// </summary>
    public static SteamHostWorkerParseStatus TryParseCommand(string? line, out SteamHostWorkerCommand command)
    {
        command = new SteamHostWorkerCommand(string.Empty);

        if (line is null)
        {
            return SteamHostWorkerParseStatus.Ignore;
        }

        var text = line.Trim();
        if (text.Length == 0)
        {
            return SteamHostWorkerParseStatus.Ignore;
        }

        if (text[0] == '{')
        {
            try
            {
                var request = JsonSerializer.Deserialize<SteamHostWorkerRequest>(text, SteamHostProtocol.JsonOptions);
                if (request is null || string.IsNullOrWhiteSpace(request.Verb))
                {
                    return SteamHostWorkerParseStatus.Malformed;
                }

                command = new SteamHostWorkerCommand(request.Verb.Trim(), request.TimeoutMs);
                return SteamHostWorkerParseStatus.Command;
            }
            catch (JsonException)
            {
                return SteamHostWorkerParseStatus.Malformed;
            }
        }

        var parts = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var timeoutMs = 0;
        if (parts.Length > 1 && !int.TryParse(parts[1], out timeoutMs))
        {
            return SteamHostWorkerParseStatus.Malformed;
        }

        command = new SteamHostWorkerCommand(parts[0], timeoutMs);
        return SteamHostWorkerParseStatus.Command;
    }

    /// <summary>worker 是否认得这个动词。未知动词回 <c>unsupported</c>，不杀进程。</summary>
    public static bool IsKnownVerb(string? verb) =>
        string.Equals(verb, SteamHostProtocol.AchievementsVerb, StringComparison.Ordinal)
        || string.Equals(verb, PingVerb, StringComparison.Ordinal)
        || string.Equals(verb, ShutdownVerb, StringComparison.Ordinal);

    /// <summary>把客户端给的超时毫秒钳到 [0, <see cref="MaxRequestTimeout"/>]；0 表示用默认值。</summary>
    public static TimeSpan ResolveTimeout(int timeoutMs, TimeSpan fallback)
    {
        if (timeoutMs <= 0)
        {
            return fallback;
        }

        var requested = TimeSpan.FromMilliseconds(timeoutMs);
        return requested <= MaxRequestTimeout ? requested : MaxRequestTimeout;
    }
}

/// <summary>一行命令的解析结果。</summary>
public enum SteamHostWorkerParseStatus
{
    /// <summary>空行 / 纯空白：忽略，不回复。</summary>
    Ignore,

    /// <summary>解析成一条命令。</summary>
    Command,

    /// <summary>长得像 JSON 但解不出来：回一行 <c>requestFailed</c>，继续等下一条。</summary>
    Malformed,
}

/// <summary>客户端发来的一条命令。</summary>
/// <param name="Verb">动词，见 <see cref="SteamHostWorkerProtocol"/>。</param>
/// <param name="TimeoutMs">原生调用最多等多久；0 表示用宿主默认值。</param>
public sealed record SteamHostWorkerCommand(string Verb, int TimeoutMs = 0);

/// <summary>命令行的 JSON 形状（只用于反序列化，和 <see cref="SteamHostWorkerCommand"/> 同构）。</summary>
public sealed record SteamHostWorkerRequest
{
    /// <summary>动词。</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>超时毫秒；0 表示默认。</summary>
    public int TimeoutMs { get; init; }
}

/// <summary>
/// worker 启动后写的第一行：宣告协议版本与它服务的 (dll, appid)。
/// <para>
/// 客户端必须等到这一行才算「worker 可用」；等不到就按启动失败处理并回落一次性调用。
/// 这一行也让「旧版本 App + 新版本 worker」在握手阶段就被拒掉，而不是跑到一半才错位。
/// </para>
/// </summary>
public sealed record SteamHostWorkerHello
{
    /// <summary>协议版本。</summary>
    public int Version { get; init; } = SteamHostWorkerProtocol.Version;

    /// <summary>本次常驻会话服务的 Steam appid。</summary>
    public int AppId { get; init; }

    /// <summary>本次常驻会话加载的 <c>steam_api64.dll</c> 路径（便于日志定位）。</summary>
    public string ApiPath { get; init; } = string.Empty;
}

/// <summary>
/// worker 对一条命令的回复。成功读成就时 <see cref="Achievements"/> 带完整结果，
/// 其余动词只填 <see cref="Ok"/> / <see cref="Code"/>。
/// </summary>
public sealed record SteamHostWorkerReply
{
    /// <summary>协议版本；客户端必须与自己的一致。</summary>
    public int Version { get; init; } = SteamHostWorkerProtocol.Version;

    /// <summary>对应命令的动词（回显，便于对上号）。</summary>
    public string Verb { get; init; } = string.Empty;

    /// <summary>结果码，取值见 <see cref="SteamAchievementCodes"/>。</summary>
    public string Code { get; init; } = SteamAchievementCodes.HostFailed;

    /// <summary>这条命令是否成功。</summary>
    public bool Ok { get; init; }

    /// <summary>原生库是否已初始化（失败时用来区分「没起来」与「起来了但读不到」）。</summary>
    public bool Initialized { get; init; }

    /// <summary>失败说明（中文，给人看）。</summary>
    public string? Error { get; init; }

    /// <summary>更细的诊断信息。</summary>
    public string? Detail { get; init; }

    /// <summary>成就结果；仅 achievements 成功时非空。</summary>
    public SteamHostAchievementsResult? Achievements { get; init; }

    /// <summary>构造 pong。</summary>
    public static SteamHostWorkerReply Pong() => new()
    {
        Verb = SteamHostWorkerProtocol.PingVerb,
        Code = SteamAchievementCodes.Ok,
        Ok = true,
    };

    /// <summary>把一次成就读取的完整结果包成回复。</summary>
    public static SteamHostWorkerReply FromAchievements(string verb, SteamHostAchievementsResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        return new SteamHostWorkerReply
        {
            Verb = verb,
            Code = result.Code,
            Ok = result.Ok,
            Initialized = result.Initialized,
            Error = result.Error,
            Detail = result.Detail,
            Achievements = result,
        };
    }

    /// <summary>构造失败回复。</summary>
    public static SteamHostWorkerReply Fail(string verb, string code, string? error) => new()
    {
        Verb = verb,
        Code = code,
        Ok = false,
        Error = error,
    };
}
