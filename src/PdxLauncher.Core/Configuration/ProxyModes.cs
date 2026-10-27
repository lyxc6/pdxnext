namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 网络代理模式的取值（settings.json 的 proxyMode 字段）。
/// 用字符串常量而不是枚举：AppSettingsStore 的 JSON 反序列化遇到未知枚举值会抛异常，
/// 进而让整份设置回退默认值；字符串可以安全兜底。
/// </summary>
public static class ProxyModes
{
    /// <summary>直接连接：完全不使用代理，忽略 proxyUrl / 系统代理 / 环境变量（默认）。</summary>
    public const string Direct = "direct";

    /// <summary>跟随系统：优先 Windows 系统代理，其次 http_proxy/https_proxy 环境变量。</summary>
    public const string System = "system";

    /// <summary>手动配置：只使用 AppSettings.ProxyUrl。</summary>
    public const string Manual = "manual";

    /// <summary>
    /// 把设置里的两个字段归一化成一个合法模式。
    /// 旧版本只有 proxyUrl（没有 proxyMode）：填了地址就按手动配置迁移，否则直连。
    /// </summary>
    /// <param name="mode">settings.json 里的 proxyMode。</param>
    /// <param name="proxyUrl">settings.json 里的 proxyUrl（只在手动配置下有意义）。</param>
    public static string Normalize(string? mode, string? proxyUrl)
    {
        var trimmed = mode?.Trim();
        if (string.Equals(trimmed, Direct, StringComparison.OrdinalIgnoreCase))
        {
            return Direct;
        }

        if (string.Equals(trimmed, System, StringComparison.OrdinalIgnoreCase))
        {
            return System;
        }

        if (string.Equals(trimmed, Manual, StringComparison.OrdinalIgnoreCase))
        {
            return Manual;
        }

        // null / 空白 / 未知值：旧配置迁移——只写了代理地址的用户保持原来的「手动配置」行为。
        return string.IsNullOrWhiteSpace(proxyUrl) ? Direct : Manual;
    }

    /// <summary>归一化后是否为手动配置（设置页靠它决定是否显示地址输入区）。</summary>
    public static bool IsManual(string? mode, string? proxyUrl) =>
        string.Equals(Normalize(mode, proxyUrl), Manual, StringComparison.Ordinal);
}
