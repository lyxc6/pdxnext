namespace PdxLauncher.App.ViewModels;

using System.Globalization;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 成就列表里的一行。只做「契约 → 文案」的映射，不引用任何 Microsoft.UI.Xaml 类型。
/// <para>
/// 隐藏成就（<see cref="AchievementEntry.IsHidden"/>）在未解锁前不展示说明，避免剧透；
/// 解锁时间按本地时区显示，数据契约里始终是 UTC。
/// </para>
/// </summary>
public sealed class AchievementRowViewModel : LocalizedObservableObject
{
    private readonly AchievementEntry _entry;
    private readonly string _iconPath;

    /// <summary>
    /// 构造。<paramref name="iconPath"/> 是游戏自带图标的本地路径（见
    /// <c>AchievementIconResolver</c>）；为空时退回 <see cref="AchievementEntry.IconUrl"/>，
    /// 再没有就显示占位方块。
    /// </summary>
    public AchievementRowViewModel(AchievementEntry entry, string? iconPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        _iconPath = string.IsNullOrWhiteSpace(iconPath)
            ? entry.IconUrl ?? string.Empty
            : iconPath;
    }

    public string Id => _entry.Id;

    public string Name => _entry.Name;

    public bool IsUnlocked => _entry.IsUnlocked;

    public bool IsHidden => _entry.IsHidden;

    public string IconUrl => _iconPath;

    /// <summary>有图标地址时为 true；否则界面显示占位方块。</summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(_iconPath);

    /// <summary>平台给了稀有度才显示百分比；没有数据时整行隐藏，不显示 0%。</summary>
    public bool HasRarity => _entry.GlobalPercent.HasValue;

    public string RarityText => _entry.GlobalPercent is { } percent
        ? T(Keys.AchievementsRarity, percent.ToString("0.0", CultureInfo.CurrentCulture))
        : string.Empty;

    /// <summary>隐藏成就且未解锁时用说明占位，避免把剧透文案直接摊在列表里。</summary>
    public string DescriptionText => IsHidden && !IsUnlocked
        ? T(Keys.AchievementsHiddenDescription)
        : _entry.Description;

    /// <summary>行右侧的状态：已解锁（带本地时间）/ 隐藏成就 / 未解锁。</summary>
    public string StatusText
    {
        get
        {
            if (!IsUnlocked)
            {
                return IsHidden ? T(Keys.AchievementsStatusHidden) : T(Keys.AchievementsStatusLocked);
            }

            return _entry.UnlockTimeUtc is { } unlockTimeUtc
                ? T(Keys.AchievementsStatusUnlockedAt, unlockTimeUtc.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : T(Keys.AchievementsStatusUnlocked);
        }
    }

    /// <summary>列表行的自动化 id：UI 走查按成就 id 定位。</summary>
    public string AutomationId => "achievement_" + Id;
}
