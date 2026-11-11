namespace PdxLauncher.Core.Steam;

using System.Diagnostics;

/// <summary>
/// Steam 客户端进程的旁证：只用来在明显没运行时快速失败，不能替代 <c>SteamAPI_Init</c> 的结果。
/// </summary>
public static class SteamClientProcess
{
    /// <summary>
    /// 进程表里是否有 Steam 客户端进程。
    /// <para>
    /// 返回 <c>null</c> 表示进程枚举本身失败（受限会话等），调用方不要据此判定「没运行」；
    /// 只有枚举成功且确实没有匹配进程时才返回 <c>false</c>。
    /// </para>
    /// <para>
    /// 匹配口径沿用成就宿主：名字以 <c>steam</c> 开头（忽略大小写），排除常驻的
    /// <c>SteamService</c>。会话隔离看不到 <c>steam.exe</c> 时，<c>steamwebhelper</c>
    /// 通常仍在进程表里，所以它也算旁证。
    /// </para>
    /// </summary>
    public static bool? IsRunning()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcesses();
        }
        catch
        {
            return null;
        }

        try
        {
            foreach (var process in processes)
            {
                string name;
                try
                {
                    name = process.ProcessName;
                }
                catch
                {
                    continue;
                }

                if (IsClientProcessName(name))
                {
                    return true;
                }
            }

            return false;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }

    /// <summary>单个进程名是否算 Steam 客户端旁证（不含 <c>SteamService</c>）。</summary>
    internal static bool IsClientProcessName(string? name) =>
        !string.IsNullOrEmpty(name)
        && name.StartsWith("steam", StringComparison.OrdinalIgnoreCase)
        && !name.Equals("steamservice", StringComparison.OrdinalIgnoreCase);
}
