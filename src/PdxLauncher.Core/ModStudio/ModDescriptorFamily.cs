namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Games;

/// <summary>Paradox 游戏的 mod 描述符家族。</summary>
public enum ModDescriptorFamily
{
    /// <summary>这款游戏不使用本启动器支持的本地 mod 描述符。</summary>
    None,

    /// <summary>旧式 <c>&lt;name&gt;.mod</c> + <c>descriptor.mod</c>（CK3 / Stellaris / EU4 / HOI4 / Imperator）。</summary>
    Legacy,

    /// <summary><c>.metadata/metadata.json</c>（Victoria 3 / EU5）。</summary>
    Metadata,
}

/// <summary>
/// 「某款游戏该生成哪种 mod 描述符」的唯一判定点。
/// <para>这里只做纯映射，不读磁盘；创建器、页面和自检都用同一张表，避免页面允许创建而游戏不认。</para>
/// </summary>
public static class ModDescriptorRules
{
    /// <summary>取游戏的描述符家族；未登记 / 不支持的游戏返回 <see cref="ModDescriptorFamily.None"/>。</summary>
    public static ModDescriptorFamily FamilyFor(string? gameId)
    {
        return KnownGames.CanonicalizeGameId(gameId) switch
        {
            "ck3" or "stellaris" or "eu4" or "hoi4" or "imperator_rome" => ModDescriptorFamily.Legacy,
            "victoria3" or "eu5" => ModDescriptorFamily.Metadata,
            _ => ModDescriptorFamily.None,
        };
    }

    /// <summary>这款游戏能否在本地创建 mod。</summary>
    public static bool CanCreate(string? gameId) => FamilyFor(gameId) != ModDescriptorFamily.None;

    /// <summary>这款游戏能否上传到 Steam 创意工坊。</summary>
    public static bool CanPublish(string? gameId) =>
        CanCreate(gameId) && Online.SteamWorkshopGames.TryGet(gameId) == true;

    /// <summary>描述符家族对应的本地化说明键。</summary>
    public static string DescribeKey(ModDescriptorFamily family) => family switch
    {
        ModDescriptorFamily.Legacy => "modstudio.descriptorLegacy",
        ModDescriptorFamily.Metadata => "modstudio.descriptorMetadata",
        _ => "modstudio.descriptorUnsupported",
    };
}
