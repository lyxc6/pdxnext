namespace PdxLauncher.Core.Steam;

/// <summary>
/// 成就宿主动词的结果码。
/// <para>
/// 为什么不用「Ok + 中文错误串」：App 侧要把「Steam 没运行」「这款游戏没有成就」「用户没登录」
/// 映射成**不同的页面空状态**，靠匹配错误文案迟早会漂。这里每个码都是稳定字符串，
/// 只增不改；未知码一律按「Steam 不可用」处理，保证老 App 遇到新码时不会误报「没有成就」。
/// </para>
/// </summary>
public static class SteamAchievementCodes
{
    /// <summary>成功读到成就列表（可能为空数组，此时用 <see cref="NoAchievements"/>）。</summary>
    public const string Ok = "ok";

    /// <summary>找不到 <c>steam_api64.dll</c>（用户手填路径不对 / 游戏目录里没有）。</summary>
    public const string NoDll = "noDll";

    /// <summary>加载原生库失败（位数不符、文件损坏、缺依赖）。</summary>
    public const string Native = "native";

    /// <summary>Steam 客户端没在运行（<c>SteamAPI_IsSteamRunning</c> 返回 0）。</summary>
    public const string SteamNotRunning = "steamNotRunning";

    /// <summary><c>SteamAPI_Init</c> 失败（未登录、被拒、初始化参数不对等）。</summary>
    public const string InitFailed = "initFailed";

    /// <summary>Init 成功但拿不到当前 SteamID（账号态异常）。</summary>
    public const string NoSteamUser = "noSteamUser";

    /// <summary>原生库缺本功能需要的导出（老版本 DLL 没带 <c>ISteamUserStats</c> / ManualDispatch）。</summary>
    public const string Unsupported = "unsupported";

    /// <summary><c>RequestUserStats</c> 回调回来了但 <c>EResult</c> 不是成功。</summary>
    public const string RequestFailed = "requestFailed";

    /// <summary>等 <c>UserStatsReceived_t</c> 超时（Steam 无响应）。</summary>
    public const string Timeout = "timeout";

    /// <summary>这款游戏一个成就都没有（不是失败，也不等于 Steam 不可用）。</summary>
    public const string NoAchievements = "noAchievements";

    /// <summary>App 侧：宿主子进程没起来 / 输出无法解码。</summary>
    public const string HostFailed = "hostFailed";

    /// <summary>
    /// 这个失败码是否说明「这份 DLL 不行」：典型的是 DLC 解锁 loader 在 <c>SteamAPI_Init</c> 里失败
    /// （<see cref="InitFailed"/>），或者根本加载不起来（<see cref="Native"/>）。
    /// 这种时候换下一份候选是有意义的；但只换 DLL，别再等更多轮 Init。
    /// </summary>
    /// <param name="code">宿主返回的结果码。</param>
    public static bool IsCandidateFault(string? code) => code is InitFailed or Native;

    /// <summary>
    /// 这个失败码是否属于环境问题（Steam 没启动、没登录、等回调超时）。
    /// 换哪份 <c>steam_api64.dll</c> 结果都一样，调用方应当直接停下，而不是把候选全试一遍。
    /// </summary>
    /// <param name="code">宿主返回的结果码。</param>
    public static bool IsEnvironmentFault(string? code) => code is SteamNotRunning or NoSteamUser or Timeout;

    /// <summary>
    /// 这个码是否意味着「Steam 侧确实不可用」（而不是「没有成就」或成功）。
    /// </summary>
    public static bool IsUnavailable(string? code) =>
        code is not null && code != Ok && code != NoAchievements;
}
