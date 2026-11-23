namespace PdxLauncher.App.ViewModels;

using PdxLauncher.Achievements;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就条件的展示文案映射。放在纯 C# 类里，自检可以直接钉住「官方提示 / 原始判定 /
/// 前置限制 / 来源」四块文案与隐藏成就的可见性，不需要 WinUI 运行时。
/// </summary>
public sealed class AchievementConditionPresentation
{
    private readonly AchievementEntry _entry;

    public AchievementConditionPresentation(AchievementEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
    }

    public bool HasData => _entry.Condition is { HasData: true };

    public bool ShowOfficialHint => _entry.Condition is { IsOfficialHint: true, Summary.Length: > 0 };

    public string SummaryText => _entry.Condition?.Summary ?? string.Empty;

    public string UnlockText => _entry.Condition?.UnlockScript ?? string.Empty;

    public string PossibleText => _entry.Condition?.PossibleScript ?? string.Empty;

    public string SourceText => _entry.Condition?.SourceFile ?? string.Empty;

    public string ToggleAutomationId => "btnAchievementCondition_" + _entry.Id;

    public string DetailsAutomationId => "pnlAchievementCondition_" + _entry.Id;

    public string SummaryAutomationId => "txtAchievementConditionHint_" + _entry.Id;

    public string UnlockAutomationId => "txtAchievementConditionUnlock_" + _entry.Id;

    public string PossibleAutomationId => "txtAchievementConditionPossible_" + _entry.Id;

    public string SourceAutomationId => "txtAchievementConditionSource_" + _entry.Id;

    public string NoDataAutomationId => "txtAchievementConditionEmpty_" + _entry.Id;
}
