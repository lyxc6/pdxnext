namespace PdxLauncher.Core.Steam;

/// <summary>
/// 把 Steam 宿主的失败文本归成「Steam 客户端不可用」。
/// <para>
/// 覆盖两种来源：宿主返回的结构化 <c>SteamAPI_Init 失败</c>，以及原生库 fatal assert
/// 后没有结果行、只能从 stderr 捞到的断言文本。只认几个明确的 IPC / 未运行特征，
/// 不把任意 <c>SteamAPI_Init 失败</c>或单独的 <c>Fatal assert</c> 误判成客户端没运行。
/// </para>
/// </summary>
public static class SteamHostFailureClassifier
{
    /// <summary>出现任意一条就说明 Steam 客户端的跨进程管道不可用。</summary>
    private static readonly string[] SteamUnavailableMarkers =
    {
        "Cannot create IPC pipe to Steam client process",
        "Steam is probably not running",
        "SteamAPI_IsSteamRunning() failed",
        "Bad Event handle",
        "CCrossProcessPipe::BWrite",
        "BWriteAndReadResult: BWrite failed",
        "Steam 客户端没有运行",
        "Steam 未运行或未登录",
    };

    /// <summary>失败文本是否说明 Steam 客户端没运行 / IPC 管道连不上。</summary>
    /// <param name="error">宿主返回的 <c>Error</c>，或 <c>Describe</c> 拼出的 stderr 文本。</param>
    public static bool IsSteamClientUnavailable(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
        {
            return false;
        }

        foreach (var marker in SteamUnavailableMarkers)
        {
            if (error.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 这次失败值不值得再拉起一次宿主：只有「Steam 不可用」且进程表明确显示客户端仍在时，
    /// 才按「Steam 正在重启 / 切换账号」的瞬时故障处理。
    /// </summary>
    /// <param name="error">首次失败的文本。</param>
    /// <param name="steamClientProcessRunning"><see cref="SteamClientProcess.IsRunning"/> 的结果。</param>
    public static bool ShouldRetry(string? error, bool? steamClientProcessRunning) =>
        steamClientProcessRunning == true && IsSteamClientUnavailable(error);
}
