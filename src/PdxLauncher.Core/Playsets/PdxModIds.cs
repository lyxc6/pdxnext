namespace PdxLauncher.Core.Playsets;

/// <summary>
/// PDX Mods 数字 id 的取值规则。
/// <para>
/// 官方库把 PDX 渠道 mod 的平台 id 存在 <c>mods.pdxId</c> 列里，但自有库
/// （<c>pdxnext.sqlite</c>）没有这一列，只把「来源 + id」拼进 <c>gameRegistryId</c>。
/// 官方「分享播放集」JSON 需要用 <c>pdxId</c> 标识这类 mod，所以这里统一从
/// <c>gameRegistryId</c> 解析出可用的 id，避免每个仓储各写一套格式判断。
/// </para>
/// </summary>
public static class PdxModIds
{
    private static readonly char[] Separators = [':', '/'];

    /// <summary>去掉首尾空白；空白返回 null。</summary>
    public static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>是否是纯数字 id（分享 JSON 的 <c>steamId</c> / <c>pdxId</c> 都只认这种）。</summary>
    public static bool LooksNumeric(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.All(char.IsAsciiDigit);

    /// <summary>
    /// 从 <c>gameRegistryId</c> 解析 PDX Mods id。支持 <c>pdx:123</c>、<c>pdx/123</c>、
    /// <c>pdx_mods:123</c>、<c>paradox:123</c>；其余形式（如 <c>mod/ugc_123.mod</c>）返回 null。
    /// </summary>
    public static string? FromRegistryId(string? gameRegistryId)
    {
        if (string.IsNullOrWhiteSpace(gameRegistryId))
        {
            return null;
        }

        var value = gameRegistryId.Trim();
        var separator = value.LastIndexOfAny(Separators);
        if (separator <= 0 || separator >= value.Length - 1)
        {
            return null;
        }

        var prefix = value[..separator].Trim().Replace("_", string.Empty, StringComparison.Ordinal);
        if (!string.Equals(prefix, "pdx", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "pdxmods", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(prefix, "paradox", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var id = value[(separator + 1)..].Trim();
        return LooksNumeric(id) ? id : null;
    }
}
