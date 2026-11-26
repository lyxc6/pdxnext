namespace PdxLauncher.Core.Mcp;

/// <summary>
/// MCP 密钥可勾选的业务能力目录。能力是用户能理解的授权单位，
/// 每个能力映射到一组工具；存储仍写在 <c>mcp_access_keys.allowed_tools</c>
/// 这一列（逗号分隔的能力 id），不需要改表结构。
/// </summary>
public static class McpCapabilityCatalog
{
    public const string GamesRead = "games.read";
    public const string UploadRead = McpPermissionScopes.UploadRead;
    public const string UploadWrite = McpPermissionScopes.UploadWrite;
    public const string PlaySetsRead = McpPermissionScopes.PlaySetsRead;
    public const string PlaySetsPlan = "playsets.plan";
    public const string SettingsRead = McpPermissionScopes.SettingsRead;
    public const string SettingsPlan = "settings.plan";

    /// <summary>已登记的能力；顺序即设置页的显示顺序。</summary>
    public static readonly IReadOnlyList<string> All =
    [
        GamesRead,
        UploadRead,
        UploadWrite,
        PlaySetsRead,
        PlaySetsPlan,
        SettingsRead,
        SettingsPlan,
    ];

    /// <summary>没有密钥时的兼容模式只给读取能力；写入必须由密钥显式授权。</summary>
    public static readonly IReadOnlyList<string> ReadOnly =
    [
        GamesRead,
        UploadRead,
        PlaySetsRead,
        SettingsRead,
    ];

    private static readonly Dictionary<string, string[]> ToolsByCapability =
        new(StringComparer.Ordinal)
        {
            [GamesRead] = ["list_games", "list_mods", "list_tags"],
            [UploadRead] = ["get_upload_draft"],
            [UploadWrite] = ["save_upload_draft", "clear_upload_draft"],
            [PlaySetsRead] = ["get_playsets_page"],
            [PlaySetsPlan] = ["create_change_plan"],
            [SettingsRead] = ["get_settings_page"],
            [SettingsPlan] = ["create_change_plan"],
        };

    /// <summary>老版本写进库里的作用域/工具名 → 新能力，用于读旧密钥时归一化。</summary>
    private static readonly Dictionary<string, string[]> LegacyAliases =
        new(StringComparer.Ordinal)
        {
            [McpPermissionScopes.PlaySetsWriteSafe] = [PlaySetsPlan],
            [McpPermissionScopes.SettingsWriteSafe] = [SettingsPlan],
            ["get_playsets_page"] = [PlaySetsRead],
            ["get_settings_page"] = [SettingsRead],
            ["get_upload_draft"] = [UploadRead],
            ["save_upload_draft"] = [UploadWrite],
            ["clear_upload_draft"] = [UploadWrite],
            ["create_change_plan"] = [PlaySetsPlan, SettingsPlan],
            ["list_games"] = [GamesRead],
            ["list_mods"] = [GamesRead],
            ["list_tags"] = [GamesRead],
        };

    public static bool IsValid(string? capability) =>
        capability is not null && All.Contains(capability, StringComparer.Ordinal);

    /// <summary>能力是否能调用该工具；<paramref name="domain"/> 只影响变更计划。</summary>
    public static bool AllowsTool(string capability, string toolName, string? domain = null)
    {
        if (!ToolsByCapability.TryGetValue(capability, out var tools)
            || !tools.Contains(toolName, StringComparer.Ordinal))
        {
            return false;
        }

        return toolName != "create_change_plan"
            || string.IsNullOrWhiteSpace(domain)
            || AllowsPlanDomain(capability, domain);
    }

    /// <summary>变更计划按业务域分别授权，避免「能改播放集」被顺带解释成「能改设置」。</summary>
    public static bool AllowsPlanDomain(string capability, string domain) => capability switch
    {
        PlaySetsPlan => string.Equals(domain, "playsets", StringComparison.Ordinal),
        SettingsPlan => string.Equals(domain, "settings", StringComparison.Ordinal),
        _ => false,
    };

    /// <summary>
    /// 把库里的旧值（作用域、工具名、<c>*</c>）归一到能力列表。
    /// 旧版对任意密钥都隐式放行只读工具，所以只要旧值里出现过任何作用域，
    /// 就补上 <see cref="GamesRead"/>，避免升级后 AI 突然看不到游戏列表。
    /// </summary>
    public static IReadOnlyList<string> Normalize(IReadOnlyList<string> stored)
    {
        if (stored.Count == 0 || stored.Contains("*", StringComparer.Ordinal))
        {
            return All;
        }

        var result = new List<string>();
        var sawLegacy = false;
        foreach (var item in stored)
        {
            if (IsValid(item))
            {
                AddDistinct(result, item);
                continue;
            }

            sawLegacy = true;
            if (LegacyAliases.TryGetValue(item, out var aliases))
            {
                foreach (var alias in aliases)
                {
                    AddDistinct(result, alias);
                }
            }
            else if (McpPermissionScopes.IsValid(item))
            {
                AddDistinct(result, MapLegacyScope(item));
            }
        }

        if (sawLegacy)
        {
            AddDistinct(result, GamesRead);
        }

        return result.Count == 0 ? ReadOnly : result;
    }

    /// <summary>新密钥按能力排序落库，保证列表稳定。</summary>
    public static string ToStorage(IReadOnlyList<string> capabilities)
    {
        var ordered = All
            .Where(capability => capabilities.Contains(capability, StringComparer.Ordinal))
            .ToArray();
        return ordered.Length == 0 ? string.Empty : string.Join(',', ordered);
    }

    private static string? MapLegacyScope(string scope) => scope switch
    {
        McpPermissionScopes.UploadRead => UploadRead,
        McpPermissionScopes.UploadWrite => UploadWrite,
        McpPermissionScopes.PlaySetsRead => PlaySetsRead,
        McpPermissionScopes.SettingsRead => SettingsRead,
        _ => null,
    };

    private static void AddDistinct(List<string> list, string? capability)
    {
        if (capability is not null && !list.Contains(capability, StringComparer.Ordinal))
        {
            list.Add(capability);
        }
    }
}
