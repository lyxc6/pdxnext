namespace PdxLauncher.Core.Games;

/// <summary>
/// 「这个游戏的加载器对路径有什么硬性要求」——与具体游戏的安装结构无关，只按 gameId 查。
/// </summary>
/// <remarks>
/// 这类限制游戏自己不会报错，只会静默忽略掉加载文件里那一行，表现就是「mod 明明启用了，
/// 进游戏却没有」。所以必须在我们写加载文件之前就判出来并提示。
/// </remarks>
public static class GamePathRules
{
    /// <summary>
    /// 该游戏是否只能加载纯 ASCII 路径下的 mod。
    /// </summary>
    /// <remarks>
    /// Victoria 3 的 wiki（Mod structure 一节）明确写着：
    /// "Victoria 3 cannot load any mods whose file path includes non-ASCII characters"，
    /// 官方论坛也有同一条限制的讨论（非 ASCII 路径的本地 mod 在游戏里直接不生效）。
    /// 换本地 mod 根目录的办法是改 <c>Steam/steamapps/common/Victoria 3/launcher/launcher-settings.json</c>
    /// 里的 <c>gameDataPath</c>。
    /// 其余游戏（CK3 / Stellaris / EU4 / EU5 / CS2…）没有这条限制，真机 CK3 的中文目录名本地 mod 正常加载。
    /// </remarks>
    public static bool RequiresAsciiPaths(string? gameId) =>
        string.Equals(gameId, "victoria3", StringComparison.OrdinalIgnoreCase);

    /// <summary>需要 ASCII 限制时返回对应的提示文案键，否则 null。</summary>
    public static string? DescribeAsciiRuleKey(string? gameId) =>
        RequiresAsciiPaths(gameId) ? "mods.warnNonAsciiPath" : null;
}
