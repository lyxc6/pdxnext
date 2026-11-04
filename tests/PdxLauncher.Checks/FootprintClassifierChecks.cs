namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 足迹分层自检：<see cref="FootprintClassifier.Classify"/> / <see cref="FootprintClassifier.TierRank"/> /
/// <see cref="FootprintClassifier.IsDerivedPatchName"/> 的口径与边界。
/// <para>纯内存用例，不读写磁盘。分层口径来自本机 CK3 382 个订阅 mod 的实测分布。</para>
/// </summary>
public static class FootprintClassifierChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckKinds();
        CheckLocAndGfxDominance();
        CheckDerivedNames();
        CheckTierRankOrder();
        CheckPathPredicates();
        return ChecksContext.Failures - before;
    }

    private static void CheckKinds()
    {
        ChecksContext.Equal("纯新增(0 覆盖) → PureAdditive", ModKind.PureAdditive, FootprintClassifier.Classify(Foot(50, 0)));
        ChecksContext.Equal("微调(1-10) → SmallTweak", ModKind.SmallTweak, FootprintClassifier.Classify(Foot(50, 5)));

        // 体量门槛：2339 个文件只覆盖 2 处玩法的内容包（V3 的 Tech & Res）不是「小调整」，
        // 它是以新增为主的内容包，层位上应排在真正的微调之前。
        ChecksContext.Equal("微调边界(200 文件) → SmallTweak", ModKind.SmallTweak, FootprintClassifier.Classify(Foot(200, 3)));
        ChecksContext.Equal("超过微调体量(201 文件) → PureAdditive", ModKind.PureAdditive, FootprintClassifier.Classify(Foot(201, 3)));
        ChecksContext.Equal("大体量少覆盖(300 文件/3 处) → PureAdditive", ModKind.PureAdditive, FootprintClassifier.Classify(Foot(300, 3)));
        ChecksContext.Equal("中量(11-50) → MediumChange", ModKind.MediumChange, FootprintClassifier.Classify(Foot(200, 12)));
        ChecksContext.Equal("大量(51-200) → LargeChange", ModKind.LargeChange, FootprintClassifier.Classify(Foot(400, 51)));
        ChecksContext.Equal("超大(≥200) → Overhaul", ModKind.Overhaul, FootprintClassifier.Classify(Foot(4000, 200)));
        ChecksContext.Equal("体量门槛(总文件≥1000 且覆盖≥50) → Overhaul", ModKind.Overhaul, FootprintClassifier.Classify(Foot(1200, 60)));
        ChecksContext.Equal("总文件≥1000 但覆盖 49 → MediumChange（未达 51）", ModKind.MediumChange, FootprintClassifier.Classify(Foot(1200, 49)));
    }

    private static void CheckLocAndGfxDominance()
    {
        var locDominant = new ModFootprint(Guid.NewGuid(), "A", 100, 0, 80, 0, 0, Empty(), Empty());
        ChecksContext.Equal("localization 占 80% → Localization", ModKind.Localization, FootprintClassifier.Classify(locDominant));

        var locDominantWithTweaks = new ModFootprint(Guid.NewGuid(), "A", 100, 5, 80, 0, 0, Empty(), Empty());
        ChecksContext.Equal("翻译主导且带少量改动 → 仍 Localization（层位最末）", ModKind.Localization, FootprintClassifier.Classify(locDominantWithTweaks));

        // 有意语义：localization 占比 ≥80% 即按翻译包处理（排最末），不再看 gameplay 覆盖数；
        // 翻译包必须最后加载才能套住所有玩法文本，本机 382 mod 里只有 2 个落在这条边界上。
        var locDominantWithMedium = new ModFootprint(Guid.NewGuid(), "A", 100, 30, 80, 0, 0, Empty(), Empty());
        ChecksContext.Equal("翻译主导且改了 30 处玩法 → 仍 Localization（有意为之）", ModKind.Localization, FootprintClassifier.Classify(locDominantWithMedium));

        var locBelowThreshold = new ModFootprint(Guid.NewGuid(), "A", 100, 0, 79, 0, 0, Empty(), Empty());
        ChecksContext.Equal("localization 79% 未达 80% → 不是 Localization", ModKind.PureAdditive, FootprintClassifier.Classify(locBelowThreshold));

        var gfxDominant = new ModFootprint(Guid.NewGuid(), "A", 100, 0, 0, 85, 0, Empty(), Empty());
        ChecksContext.Equal("gfx 占 85% 且不改玩法 → GfxOnly", ModKind.GfxOnly, FootprintClassifier.Classify(gfxDominant));

        var gfxDominantWithTweaks = new ModFootprint(Guid.NewGuid(), "A", 100, 3, 0, 85, 0, Empty(), Empty());
        ChecksContext.Equal("gfx 主导但改了玩法 → 按改动量 SmallTweak", ModKind.SmallTweak, FootprintClassifier.Classify(gfxDominantWithTweaks));
    }

    private static void CheckDerivedNames()
    {
        ChecksContext.Equal("「OE + CFP Compatch」是衍生补丁名", true, FootprintClassifier.IsDerivedPatchName("OE + CFP Compatch"));
        ChecksContext.Equal("「Unofficial Patch」是衍生补丁名", true, FootprintClassifier.IsDerivedPatchName("Unofficial Patch"));
        ChecksContext.Equal("「Smaller World Map RICE Compatibility」是衍生补丁名", true, FootprintClassifier.IsDerivedPatchName("Smaller World Map RICE Compatibility"));
        ChecksContext.Equal("「XX 汉化」是衍生补丁名", true, FootprintClassifier.IsDerivedPatchName("XX 汉化"));
        ChecksContext.Equal("「Fix Pack」整词命中补丁名", true, FootprintClassifier.IsDerivedPatchName("Fix Pack"));
        ChecksContext.Equal("「Ethnicities and Portraits Expanded」不是衍生补丁名", false, FootprintClassifier.IsDerivedPatchName("Ethnicities and Portraits Expanded"));
        ChecksContext.Equal("「1.20 Local」不是衍生补丁名", false, FootprintClassifier.IsDerivedPatchName("1.20 Local"));
        ChecksContext.Equal("「Suffix」里的 fix 不算补丁名", false, FootprintClassifier.IsDerivedPatchName("Suffix"));
        ChecksContext.Equal("「Prefix」里的 fix 不算补丁名", false, FootprintClassifier.IsDerivedPatchName("Prefix"));
        ChecksContext.Equal("「Fixpack」连写不算整词", false, FootprintClassifier.IsDerivedPatchName("Fixpack"));
        ChecksContext.Equal("「XX 重制版」不再算补丁名（整包重做不是补丁）", false, FootprintClassifier.IsDerivedPatchName("XX 重制版"));
        ChecksContext.Equal("空名不是衍生补丁名", false, FootprintClassifier.IsDerivedPatchName(null));
    }

    private static void CheckTierRankOrder()
    {
        var rank = (ModKind kind, bool patch) => FootprintClassifier.TierRank(kind, patch);

        ChecksContext.Check(
            "层位序：大修 < 大量 < 中量 < 美化 < 纯新增 < 微调 < 补丁 < 翻译",
            rank(ModKind.Overhaul, false) < rank(ModKind.LargeChange, false)
                && rank(ModKind.LargeChange, false) < rank(ModKind.MediumChange, false)
                && rank(ModKind.MediumChange, false) < rank(ModKind.GfxOnly, false)
                && rank(ModKind.GfxOnly, false) < rank(ModKind.PureAdditive, false)
                && rank(ModKind.PureAdditive, false) < rank(ModKind.SmallTweak, false)
                && rank(ModKind.SmallTweak, false) < rank(ModKind.PureAdditive, true)
                && rank(ModKind.PureAdditive, true) < rank(ModKind.Localization, false));
        ChecksContext.Equal("超大 mod 名字带 patch 仍是基底（不被压到补丁层）", 0, rank(ModKind.Overhaul, true));
        ChecksContext.Equal("纯新增带补丁名 → 补丁层位", 6, rank(ModKind.PureAdditive, true));
        ChecksContext.Equal("翻译类即使名字带 patch 也层位最末", 7, rank(ModKind.Localization, true));
    }

    private static void CheckPathPredicates()
    {
        ChecksContext.Equal("common/ 属玩法目录", true, FootprintClassifier.IsGameplayPath("common/on_action/a.txt"));
        ChecksContext.Equal("events/ 属玩法目录", true, FootprintClassifier.IsGameplayPath("events/x.txt"));
        ChecksContext.Equal("gfx/ 不属玩法目录", false, FootprintClassifier.IsGameplayPath("gfx/portraits/x.png"));
        ChecksContext.Equal("localization/ 判定", true, FootprintClassifier.IsLocPath("localization/simp_chinese/x.yml"));
        ChecksContext.Equal("gfx/ 判定", true, FootprintClassifier.IsGfxPath("gfx/x.dds"));
    }

    private static ModFootprint Foot(int totalFiles, int gameplayOverrides)
    {
        return new ModFootprint(Guid.NewGuid(), "A", totalFiles, gameplayOverrides, 0, 0, 0, Empty(), Empty());
    }

    private static HashSet<string> Empty() => new(StringComparer.OrdinalIgnoreCase);
}
