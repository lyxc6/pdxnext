namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 从 <see cref="ModFootprint"/> 推导分层标签与排序层位。纯函数、不碰 IO。
/// </summary>
/// <remarks>
/// 分层口径来自对本机 CK3 382 个订阅 mod 的实测分布（纯新增 223 / 微调 128 / 中量 17 / 大量 11 / 超大 3），
/// 与社区惯例一致：大 mod 在上、兼容补丁与小修改在下（后加载者在同文件冲突中获胜）。
/// </remarks>
public static class FootprintClassifier
{
    /// <summary>玩法目录：其中覆盖原版文件的规模决定「改动量」分层。</summary>
    private static readonly string[] GameplayDirs =
        ["common/", "events/", "history/", "gui/", "map_data/", "data_binding/"];

    /// <summary>
    /// 拉丁字母的补丁/衍生名称标记：按「整词」匹配，避免 Suffix / Prefix 里的 fix 这类子串误伤。
    /// </summary>
    private static readonly string[] DerivedLatinMarkers =
        ["compatch", "compatibility", "compat", "patch", "fix", "translation", "localization"];

    /// <summary>中日文的补丁/衍生名称标记：中文没有词边界，按子串匹配。</summary>
    private static readonly string[] DerivedCjkMarkers =
        ["补丁", "兼容", "汉化", "翻译", "中文化"];

    /// <summary>把足迹判成分层标签（互斥，按声明顺序优先匹配）。</summary>
    public static ModKind Classify(ModFootprint footprint)
    {
        ArgumentNullException.ThrowIfNull(footprint);

        if (footprint.LocFiles * 5 >= footprint.TotalFiles * 4 && footprint.TotalFiles > 0)
        {
            return ModKind.Localization;
        }

        if (footprint.GfxFiles * 5 >= footprint.TotalFiles * 4 && footprint.TotalFiles > 0 && footprint.GameplayOverrides == 0)
        {
            return ModKind.GfxOnly;
        }

        if (footprint.GameplayOverrides >= 200 || (footprint.TotalFiles >= 1000 && footprint.GameplayOverrides >= 50))
        {
            return ModKind.Overhaul;
        }

        if (footprint.GameplayOverrides >= 51)
        {
            return ModKind.LargeChange;
        }

        if (footprint.GameplayOverrides >= 11)
        {
            return ModKind.MediumChange;
        }

        if (footprint.GameplayOverrides >= 1)
        {
            return ModKind.SmallTweak;
        }

        return ModKind.PureAdditive;
    }

    /// <summary>
    /// 名字里带补丁/兼容/翻译变体标记（Compatch、Compatibility、补丁、汉化……）。
    /// 拉丁标记整词匹配（Suffix / Prefix 不算），中日文标记子串匹配。
    /// </summary>
    public static bool IsDerivedPatchName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        foreach (var marker in DerivedCjkMarkers)
        {
            if (name.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var marker in DerivedLatinMarkers)
        {
            if (ContainsBoundedToken(name, marker))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>目标词以前后都不是字母/数字为边界时才算命中（Fix / Fix Pack / (Fix) 命中，Suffix / Fixpack 不命中）。</summary>
    private static bool ContainsBoundedToken(string text, string token)
    {
        for (var i = text.IndexOf(token, StringComparison.OrdinalIgnoreCase);
             i >= 0;
             i = text.IndexOf(token, i + 1, StringComparison.OrdinalIgnoreCase))
        {
            var startsWord = i == 0 || !char.IsLetterOrDigit(text[i - 1]);
            var end = i + token.Length;
            var endsWord = end >= text.Length || !char.IsLetterOrDigit(text[end]);
            if (startsWord && endsWord)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 排序层位：越小越靠前。大修铺底 → 中量 → 美化 → 纯新增 → 微调 → 补丁/翻译变体 → 翻译。
    /// 翻译类即使名字带 patch 也固定在最末；超大 mod 本身就是被补丁的对象，不因名字带补丁字样被压到后段。
    /// </summary>
    public static int TierRank(ModKind kind, bool isDerivedPatch)
    {
        if (kind == ModKind.Localization)
        {
            return 7;
        }

        if (isDerivedPatch && kind != ModKind.Overhaul)
        {
            return 6;
        }

        return kind switch
        {
            ModKind.Overhaul => 0,
            ModKind.LargeChange => 1,
            ModKind.MediumChange => 2,
            ModKind.GfxOnly => 3,
            ModKind.PureAdditive => 4,
            ModKind.SmallTweak => 5,
            _ => 4,
        };
    }

    /// <summary>路径是否属于玩法目录（决定 GameplayOverrides 口径）。</summary>
    public static bool IsGameplayPath(string normalizedPath)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);
        foreach (var dir in GameplayDirs)
        {
            if (normalizedPath.StartsWith(dir, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>路径是否属于本地化目录。</summary>
    public static bool IsLocPath(string normalizedPath)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);
        return normalizedPath.StartsWith("localization/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>路径是否属于美术目录。</summary>
    public static bool IsGfxPath(string normalizedPath)
    {
        ArgumentNullException.ThrowIfNull(normalizedPath);
        return normalizedPath.StartsWith("gfx/", StringComparison.OrdinalIgnoreCase);
    }
}
