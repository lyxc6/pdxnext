namespace PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 按文件改动足迹给 mod 定性的分层标签（互斥，判定优先级见 <see cref="FootprintClassifier.Classify"/>）。
/// </summary>
public enum ModKind
{
    /// <summary>大修：原版玩法覆盖 ≥200，或总文件 ≥1000 且玩法覆盖 ≥50。排最前铺底。</summary>
    Overhaul,

    /// <summary>大量改动：玩法覆盖 51–200。</summary>
    LargeChange,

    /// <summary>中量改动：玩法覆盖 11–50。</summary>
    MediumChange,

    /// <summary>微调：玩法覆盖 1–10。靠后加载，冲突时小改获胜。</summary>
    SmallTweak,

    /// <summary>纯新增：0 处原版玩法覆盖。位置基本无关，保持相对稳定。</summary>
    PureAdditive,

    /// <summary>美化：gfx 占绝对主导且不改玩法。位于内容 mod 之后。</summary>
    GfxOnly,

    /// <summary>翻译/本地化：localization 占绝对主导。排最末，让译文最终生效。</summary>
    Localization,
}
