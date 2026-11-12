namespace PdxLauncher.Core.Playsets;

/// <summary>Mod 的来源：本地目录里的 mod，还是从工坊/Paradox 渠道订阅来的。</summary>
public enum ModOrigin
{
    /// <summary>用户自己放进 mod 目录（或 .mod 指向本地路径）的 mod。</summary>
    Local,

    /// <summary>从 Steam 创意工坊 / Paradox 渠道订阅来的 mod。</summary>
    Subscribed,
}

/// <summary>
/// 「本地 mod / 订阅 mod」的唯一判定点。
/// </summary>
/// <remarks>
/// 各游戏的存储里这个信息形态不同，容易各写一套：
/// <list type="bullet">
/// <item>sqlite（CK3/Vic3/Stellaris…）：<c>mods.source</c> 直接是 <c>local</c> / <c>steam</c>。</item>
/// <item>CS2 的 playset_config.json：<c>source</c> 是 <c>pdx_mods</c> / <c>local</c>。</item>
/// <item>纯磁盘扫描：只有「有没有创意工坊 id」可依据。</item>
/// </list>
/// 注意 <b>不能</b>用「steamId 是否为空」当唯一依据：真机 CK3 有一条 <c>source=local</c>
/// 却带着 <c>steamId=3613769385</c> 的本地 mod。所以要先看 source，source 缺了才看 steamId。
/// </remarks>
public static class ModOrigins
{
    /// <summary>官方库 <c>mods.source</c>：本地 mod。</summary>
    public const string LocalSource = "local";

    /// <summary>官方库 <c>mods.source</c>：Steam 创意工坊。</summary>
    public const string SteamSource = "steam";

    /// <summary>官方库 <c>mods.source</c>：Paradox 渠道。</summary>
    public const string ParadoxSource = "paradox";

    /// <summary>CS2 <c>playset_config.json</c> 的 <c>source</c>：PDX Mods 渠道。</summary>
    public const string PdxModsSource = "pdx_mods";

    /// <summary>徽标文案的 i18n 键（Core 不能引用生成的 <c>Keys</c>，返回键串由展示层翻译）。</summary>
    public const string LocalKey = "CommonSourceLocal";

    /// <summary>徽标文案的 i18n 键：订阅。</summary>
    public const string SubscribedKey = "CommonSourceSubscribed";

    /// <summary>把存储里的 source / steamId 归一成一个来源。</summary>
    public static ModOrigin Normalize(string? source, string? steamId)
    {
        if (string.Equals(source, LocalSource, StringComparison.OrdinalIgnoreCase))
        {
            return ModOrigin.Local;
        }

        if (string.Equals(source, SteamSource, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, ParadoxSource, StringComparison.OrdinalIgnoreCase)
            || string.Equals(source, PdxModsSource, StringComparison.OrdinalIgnoreCase))
        {
            return ModOrigin.Subscribed;
        }

        // source 缺失或是不认识的值：退回「有没有工坊 id」。磁盘扫描出来的 mod 只能这样判。
        return string.IsNullOrWhiteSpace(steamId) ? ModOrigin.Local : ModOrigin.Subscribed;
    }

    /// <summary>归一后是不是订阅来源。</summary>
    public static bool IsSubscribed(string? source, string? steamId) =>
        Normalize(source, steamId) == ModOrigin.Subscribed;

    /// <summary>徽标文案的 i18n 键。</summary>
    public static string DescribeKey(ModOrigin origin) =>
        origin == ModOrigin.Subscribed ? SubscribedKey : LocalKey;

    /// <summary>直接由原始字段取徽标文案的 i18n 键。</summary>
    public static string DescribeKey(string? source, string? steamId) =>
        DescribeKey(Normalize(source, steamId));
}
