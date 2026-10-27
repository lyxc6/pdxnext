namespace PdxLauncher.Core.Playsets;

/// <summary>
/// Steam 创意工坊地址拼装：把 mod 的创意工坊 id 翻译成「在 Steam 中打开」与「在浏览器中打开」的地址。
/// <para>
/// 只做字符串处理：真正拉起 Steam / 浏览器属于宿主进程的 shell 动作，不在 Core 里执行。
/// </para>
/// </summary>
public static class SteamWorkshop
{
    /// <summary>创意工坊页面在浏览器里的地址前缀。</summary>
    public const string BrowserAddressPrefix = "https://steamcommunity.com/sharedfiles/filedetails/?id=";

    /// <summary>Steam 客户端内打开创意工坊页面的协议地址前缀。</summary>
    public const string SteamAddressPrefix = "steam://url/CommunityFilePage/";

    /// <summary>
    /// 注册 ID 里内嵌创意工坊 id 的写法：Steam 下载的 mod 注册成 mod/ugc_&lt;id&gt;.mod。
    /// </summary>
    private const string RegistryPrefix = "ugc_";

    /// <summary>
    /// 取 mod 的创意工坊 id。
    /// 优先用 mods.steamId；为空时回退从 gameRegistryId（mod/ugc_&lt;id&gt;）.mod 里解析。
    /// 两者都拿不到、或不是纯数字时返回 null。
    /// </summary>
    /// <param name="steamId">mods.steamId，可能为 null。</param>
    /// <param name="gameRegistryId">游戏注册 ID，可能为 null / 空串。</param>
    public static string? GetWorkshopId(string? steamId, string? gameRegistryId)
    {
        // 数据库里的值可能带空白（手工编辑过 mods 表、或跨版本导入）：先裁再判，别把能用的 id 当缺失。
        var trimmed = steamId?.Trim();
        if (IsAllDigits(trimmed))
        {
            return trimmed;
        }

        return ExtractFromRegistryId(gameRegistryId);
    }

    /// <summary>浏览器打开用的地址；id 为空时返回 null。</summary>
    public static string? GetBrowserAddress(string? workshopId)
        => string.IsNullOrWhiteSpace(workshopId) ? null : BrowserAddressPrefix + workshopId;

    /// <summary>Steam 客户端打开用的地址；id 为空时返回 null。</summary>
    public static string? GetSteamAddress(string? workshopId)
        => string.IsNullOrWhiteSpace(workshopId) ? null : SteamAddressPrefix + workshopId;

    /// <summary>
    /// 从注册 ID 里解析创意工坊 id：找 "ugc_" 之后连续的 ASCII 数字。
    /// 只认 0-9，不用 char.IsDigit：后者会把全角数字等也当数字，拼出来的地址打不开。
    /// </summary>
    private static string? ExtractFromRegistryId(string? gameRegistryId)
    {
        if (string.IsNullOrWhiteSpace(gameRegistryId))
        {
            return null;
        }

        var marker = gameRegistryId.IndexOf(RegistryPrefix, StringComparison.OrdinalIgnoreCase);
        if (marker < 0)
        {
            return null;
        }

        var start = marker + RegistryPrefix.Length;
        var end = start;
        while (end < gameRegistryId.Length && IsAsciiDigit(gameRegistryId[end]))
        {
            end++;
        }

        return end > start ? gameRegistryId[start..end] : null;
    }

    private static bool IsAllDigits(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!IsAsciiDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsAsciiDigit(char character) => character is >= '0' and <= '9';
}
