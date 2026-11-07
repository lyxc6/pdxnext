namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;

/// <summary>
/// Steam 工坊标签翻译自检：<see cref="WorkshopTagKeys"/> 的映射、两份 resw 的键，以及内置
/// 筛选目录 / Mod Studio 标签是否都有译文。
/// <para>
/// 标签值是发给 Steam 的机器名，不能翻译；这里只钉展示名的资源键，避免以后改标签时
/// 键名和 resw 对不上、界面上直接露出 <c>⟨WorkshopTagXxx⟩</c>。
/// </para>
/// </summary>
public static class WorkshopTagTextChecks
{
    /// <summary>Mod Studio 标签目录覆盖的游戏。</summary>
    private static readonly string[] GameIds =
    [
        "victoria3",
        "ck3",
        "stellaris",
        "eu4",
        "eu5",
        "hoi4",
        "imperator_rome",
    ];

    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（本组不用，参数保留以统一入口）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;
        var root = I18nResw.RepoRoot();
        var zh = I18nResw.Read(root, I18nResw.SimplifiedChinese);
        var en = I18nResw.Read(root, I18nResw.English);

        CheckKeysExist(zh, en);
        CheckCatalogCoverage();
        CheckLookup();
        CheckTranslations(zh, en);
        return ChecksContext.Failures - before;
    }

    private static void CheckKeysExist(
        Dictionary<string, string> zh,
        Dictionary<string, string> en)
    {
        ChecksContext.Equal("标签文案键数量", 34, WorkshopTagKeys.AllKeys.Count);

        foreach (var key in WorkshopTagKeys.AllKeys)
        {
            ChecksContext.Check("中文标签文案: " + key, I18nResw.Get(zh, key) is { Length: > 0 });
            ChecksContext.Check("英文标签文案: " + key, I18nResw.Get(en, key) is { Length: > 0 });
        }
    }

    private static void CheckCatalogCoverage()
    {
        // 内置兜底目录里的标签（V3 类别 24 个 + 更新兼容性里的 Deprecated）。
        foreach (var group in WorkshopFilterCatalog
                     .ForApp(WorkshopFilterCatalog.Victoria3AppId)
                     .Where(static group => !group.IsDlc))
        {
            foreach (var tag in group.Tags)
            {
                // 兼容性组里的 1.9 / 1.10 是版本号，不需要翻译。
                if (IsVersionTag(tag.Tag))
                {
                    continue;
                }

                ChecksContext.Check(
                    "工坊筛选标签有译文: " + tag.Tag,
                    WorkshopTagKeys.KeyFor(tag.Tag) is not null);
            }
        }

        // 其它游戏的上传标签（CK3 的 Characters、Stellaris 的 Megastructures 等）。
        foreach (var gameId in GameIds)
        {
            foreach (var tag in ModTagCatalog.For(gameId))
            {
                ChecksContext.Check(
                    "Mod Studio 标签有译文: " + gameId + "/" + tag,
                    WorkshopTagKeys.KeyFor(tag) is not null);
            }
        }
    }

    /// <summary>版本号标签（1.9 / 1.10 …）不需要翻译。</summary>
    private static bool IsVersionTag(string tag) =>
        tag.Length > 0 && tag.All(static ch => char.IsDigit(ch) || ch == '.');

    private static void CheckLookup()
    {
        // Steam 和上传目录对 and 的大小写写法不一致，查表必须大小写不敏感。
        ChecksContext.Equal(
            "标签大小写不敏感",
            WorkshopTagKeys.KeyFor("Cultures and Religions"),
            WorkshopTagKeys.KeyFor("Cultures And Religions"));

        ChecksContext.Equal("未收录标签 → null", null, WorkshopTagKeys.KeyFor("Not A Steam Tag"));
        ChecksContext.Equal("空标签 → null", null, WorkshopTagKeys.KeyFor("   "));
    }

    private static void CheckTranslations(
        Dictionary<string, string> zh,
        Dictionary<string, string> en)
    {
        ChecksContext.Equal("Gameplay 中文", "玩法", I18nResw.Get(zh, WorkshopTagKeys.KeyGameplay));
        ChecksContext.Equal("Gameplay 英文", "Gameplay", I18nResw.Get(en, WorkshopTagKeys.KeyGameplay));
        ChecksContext.Equal(
            "Economy and Buildings 中文",
            "经济与建筑",
            I18nResw.Get(zh, WorkshopTagKeys.KeyEconomyAndBuildings));
        ChecksContext.Equal(
            "Total Conversion 中文",
            "全面转换",
            I18nResw.Get(zh, WorkshopTagKeys.KeyTotalConversion));
        ChecksContext.Equal(
            "Deprecated 中文",
            "已弃用",
            I18nResw.Get(zh, WorkshopTagKeys.KeyDeprecated));
    }
}
