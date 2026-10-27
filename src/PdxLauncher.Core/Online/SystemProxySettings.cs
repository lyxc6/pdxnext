namespace PdxLauncher.Core.Online;

using Microsoft.Win32;

/// <summary>
/// Windows 系统代理（「设置 → 网络和 Internet → 代理」写的那份 WinINET 配置）。
/// 注册表位置：HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings。
/// 只读、不写；读不到或平台不支持时返回 null，由调用方按「系统未配置代理」处理。
/// </summary>
/// <param name="Enabled">ProxyEnable 是否为 1。</param>
/// <param name="Server">ProxyServer，可能是 127.0.0.1:7890，也可能是 http=a;https=b。</param>
/// <param name="BypassList">ProxyOverride（如 localhost;127.*;&lt;local&gt;）。</param>
/// <param name="AutoConfigUrl">AutoConfigURL（PAC 脚本地址；本启动器不执行 PAC）。</param>
public sealed record SystemProxySettings(
    bool Enabled,
    string? Server,
    string? BypassList,
    string? AutoConfigUrl)
{
    private const string InternetSettingsKey =
        @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";

    /// <summary>读取当前用户的 WinINET 代理配置；失败（非 Windows、无权限、值损坏）返回 null。</summary>
    public static SystemProxySettings? Read()
    {
        // 注册表 API 在 net10.0 里带 [SupportedOSPlatform("windows")]，显式守卫避免 CA1416。
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(InternetSettingsKey, writable: false);
            if (key is null)
            {
                return null;
            }

            // ProxyEnable 是 DWORD（int）；个别工具写成字符串，这里一并兼容。
            var enabled = key.GetValue("ProxyEnable") switch
            {
                int flag => flag != 0,
                string text => string.Equals(text, "1", StringComparison.Ordinal),
                _ => false,
            };

            return new SystemProxySettings(
                enabled,
                key.GetValue("ProxyServer") as string,
                key.GetValue("ProxyOverride") as string,
                key.GetValue("AutoConfigURL") as string);
        }
        catch (Exception)
        {
            // 权限不足、注册表项被策略锁定或值类型异常都当作「系统未配置代理」，
            // 不打断联网：上层会继续看环境变量，最后直连。
            return null;
        }
    }
}
