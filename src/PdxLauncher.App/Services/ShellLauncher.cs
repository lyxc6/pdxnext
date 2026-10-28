namespace PdxLauncher.App.Services;

using System.Diagnostics;

/// <summary>
/// 交给 Windows shell 打开目标（默认浏览器 / Steam 客户端协议地址）。
/// <para>
/// 打开外部程序属于 shell 动作，不是文件内容读写，故不经 <c>IFileSystem</c>；
/// 目标也不是本进程发起的 HTTP 请求，因此不受 <c>OnlineHosts</c> 白名单约束。
/// </para>
/// <para>
/// 这里只负责「尝试打开」，不负责界面反馈：调用方拿返回值自己决定怎么提示。
/// </para>
/// </summary>
public static class ShellLauncher
{
    /// <summary>打开 http/https 地址（默认浏览器）。</summary>
    /// <param name="url">目标地址。</param>
    /// <param name="caller">日志里用来定位调用方。</param>
    public static bool TryOpenUrl(string? url, string? caller = null)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        return TryStart(url, string.Empty, caller);
    }

    /// <summary>按 <c>steam://</c> 协议地址打开 Steam 客户端。</summary>
    /// <param name="address">协议地址，如 <c>steam://url/CommunityFilePage/123</c>。</param>
    /// <param name="caller">日志里用来定位调用方。</param>
    public static bool TryOpenSteam(string? address, string? caller = null)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return false;
        }

        return TryStart(address, string.Empty, caller);
    }

    /// <summary>按文件名 + 参数启动外部程序。</summary>
    /// <param name="fileName">可执行文件或协议地址。</param>
    /// <param name="arguments">命令行参数，可为空。</param>
    /// <param name="caller">日志里用来定位调用方。</param>
    public static bool TryStart(string fileName, string? arguments = null, string? caller = null)
    {
        var who = string.IsNullOrWhiteSpace(caller) ? "ShellLauncher" : caller;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? string.Empty,
                UseShellExecute = true,
            };

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                Warn(who, "无法启动外部程序：" + fileName);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Error(who, "启动外部程序失败：" + fileName, ex);
            return false;
        }
    }

    private static void Warn(string caller, string message)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Warn(caller + " " + message);
        }
    }

    private static void Error(string caller, string message, Exception ex)
    {
        if (AppServices.IsInitialized)
        {
            AppServices.Logger.Error(caller + " " + message, ex);
        }
    }
}
