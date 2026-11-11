namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// <see cref="SteamClientProcess"/> 的进程名匹配口径自检。
/// <para>真正的进程枚举是环境相关的，这里只钉住从成就宿主沿用下来的匹配规则。</para>
/// </summary>
public static class SteamClientProcessChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        ChecksContext.Check("steam.exe 算客户端", SteamClientProcess.IsClientProcessName("steam"));
        ChecksContext.Check("steamwebhelper 算客户端", SteamClientProcess.IsClientProcessName("steamwebhelper"));
        ChecksContext.Check("进程名大小写不敏感", SteamClientProcess.IsClientProcessName("Steam"));
        ChecksContext.Check("SteamService 不算客户端", !SteamClientProcess.IsClientProcessName("steamservice"));
        ChecksContext.Check("SteamService 大小写不敏感", !SteamClientProcess.IsClientProcessName("SteamService"));
        ChecksContext.Check("非 steam 进程不算客户端", !SteamClientProcess.IsClientProcessName("notsteam"));
        ChecksContext.Check("空进程名不算客户端", !SteamClientProcess.IsClientProcessName(null));
        ChecksContext.Check("空白进程名不算客户端", !SteamClientProcess.IsClientProcessName("  "));
        return ChecksContext.Failures - before;
    }
}
