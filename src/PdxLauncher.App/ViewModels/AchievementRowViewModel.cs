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
/// <para>
/// 图标解析可以是惰性的：CK3 的图标是 DDS，第一次显示某一行时才解码落成 PNG。
/// 198 行在进页面时全部解码会卡住首屏，所以 <see cref="IconUrl"/> / <see cref="HasIcon"/>
/// 第一次被读取时才调用工厂，之后缓存结果。
/// </para>
/// </summary>
public sealed class AchievementRowViewModel : LocalizedObservableObject
{
    private readonly AchievementEntry _entry;
    private readonly Func<string?>? _iconFactory;

    private string? _iconPath;
    private bool _iconSettled;

    /// <summary>
    /// 构造。<paramref name="iconPath"/> 是游戏自带图标的本地路径（见
    /// <c>AchievementIconResolver</c>）；为空时退回 <see cref="AchievementEntry.IconUrl"/>，
    /// 再没有就显示占位方块。
    /// </summary>
    public AchievementRowViewModel(AchievementEntry entry, string? iconPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        _iconPath = string.IsNullOrWhiteSpace(iconPath) ? null : iconPath;
        _iconSettled = true;
    }

    /// <summary>
    /// 构造。图标在第一次读取 <see cref="IconUrl"/> / <see cref="HasIcon"/> 时才解析，
    /// 这样列表容器只实化前几行时不会为其余几百行解码 DDS。
    /// </summary>
    public AchievementRowViewModel(AchievementEntry entry, Func<string?> iconFactory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(iconFactory);
        _entry = entry;
        _iconFactory = iconFactory;
    }

    public string Id => _entry.Id;

    public string Name => _entry.Name;

    public bool IsUnlocked => _entry.IsUnlocked;

    public bool IsHidden => _entry.IsHidden;

    public string IconUrl => ResolveIconPath();

    /// <summary>有图标地址时为 true；否则界面显示占位方块。</summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

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

    private string ResolveIconPath()
    {
        if (!_iconSettled)
        {
            _iconSettled = true;
            try
            {
                var resolved = _iconFactory?.Invoke();
                _iconPath = string.IsNullOrWhiteSpace(resolved) ? null : resolved;
            }
            catch
            {
                // 图标只是装饰：解析/解码炸了就回占位方块，不能把整行拖崩。
                _iconPath = null;
            }
        }

        return _iconPath ?? _entry.IconUrl ?? string.Empty;
    }
}
