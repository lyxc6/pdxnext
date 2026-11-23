namespace PdxLauncher.Achievements;

/// <summary>
/// 成就脚本中的完成条件。展示层只消费这份不可变契约：官方提示词优先，
/// 没有提示词时仍可展示清洗后的 happened 原文。
/// </summary>
/// <param name="Summary">官方提示词（已剥掉本地化标记），没有时为空。</param>
/// <param name="UnlockScript">happened 块原文（已清洗并限量）。</param>
/// <param name="PossibleScript">possible 块原文（已清洗并限量）。</param>
/// <param name="SourceFile">命中的成就脚本文件；没命中时为空。</param>
/// <param name="IsOfficialHint">Summary 是否来自官方本地化提示词。</param>
public sealed record AchievementConditionInfo(
    string Summary,
    string UnlockScript,
    string PossibleScript,
    string SourceFile,
    bool IsOfficialHint)
{
    /// <summary>是否存在可展示的条件数据。</summary>
    public bool HasData => IsOfficialHint
        || UnlockScript.Length > 0
        || PossibleScript.Length > 0
        || SourceFile.Length > 0;

    /// <summary>没有任何条件数据时的共享空实例。</summary>
    public static AchievementConditionInfo Empty { get; } = new(
        string.Empty,
        string.Empty,
        string.Empty,
        string.Empty,
        false);
}
