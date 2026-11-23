namespace PdxLauncher.App.ViewModels;

using System.Globalization;
using System.Windows.Input;

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
/// 198 行在进页面时全部解码会卡住首屏，所以 <see cref="IconUrl"/> / <see cref="HasIcon"/> /
/// <see cref="IconOpacity"/> 第一次被读取时才调用工厂，之后缓存结果。
/// </para>
/// </summary>
public sealed class AchievementRowViewModel : LocalizedObservableObject
{
    private readonly AchievementEntry _entry;
    private readonly Func<AchievementIconPresentation?>? _iconFactory;

    private AchievementIconPresentation? _icon;
    private bool _iconSettled;
    private bool _isExpanded;

    /// <summary>
    /// 构造。<paramref name="iconPath"/> 是游戏自带图标的本地路径（见
    /// <c>AchievementIconResolver</c>）；为空时退回 <see cref="AchievementEntry.IconUrl"/>，
    /// 再没有就显示占位方块。
    /// </summary>
    public AchievementRowViewModel(AchievementEntry entry, string? iconPath = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        _entry = entry;
        _icon = string.IsNullOrWhiteSpace(iconPath) ? null : new AchievementIconPresentation(iconPath!, 1.0);
        _iconSettled = true;
        Condition = new AchievementConditionPresentation(entry);
        ToggleConditionCommand = new AchievementToggleCommand(ToggleCondition);
    }

    /// <summary>
    /// 构造。图标在第一次读取 <see cref="IconUrl"/> / <see cref="HasIcon"/> / <see cref="IconOpacity"/>
    /// 时才解析，这样列表容器只实化前几行时不会为其余几百行解码 DDS。
    /// </summary>
    public AchievementRowViewModel(AchievementEntry entry, Func<AchievementIconPresentation?> iconFactory)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(iconFactory);
        _entry = entry;
        _iconFactory = iconFactory;
        Condition = new AchievementConditionPresentation(entry);
        ToggleConditionCommand = new AchievementToggleCommand(ToggleCondition);
    }

    public string Id => _entry.Id;

    public string Name => _entry.Name;

    public bool IsUnlocked => _entry.IsUnlocked;

    public bool IsHidden => _entry.IsHidden;

    public string IconUrl => ResolveIcon().Url;

    /// <summary>有图标地址时为 true；否则界面显示占位方块。</summary>
    public bool HasIcon => !string.IsNullOrWhiteSpace(IconUrl);

    /// <summary>
    /// 图标不透明度：未解锁且游戏没有专门灰图时为 0.55，其余为 1.0。
    /// 没有图标时也返回 1.0（占位方块不该被压暗）。
    /// </summary>
    public double IconOpacity => HasIcon ? ResolveIcon().Opacity : 1.0;

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

    /// <summary>条件详情是否展开；每行新建时都是收起状态。</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        private set => SetProperty(ref _isExpanded, value);
    }

    /// <summary>命中的成就脚本才有可展开的详情；隐藏成就同样可展开。</summary>
    public bool HasConditionData => Condition.HasData;

    /// <summary>行内折叠按钮入口：可点击区域跨越整行，不依赖 ListView 选中。</summary>
    public ICommand ToggleConditionCommand { get; }

    public AchievementConditionPresentation Condition { get; }

    public bool ShowConditionDetails => IsExpanded && HasConditionData;

    public string ConditionSummary => Condition.SummaryText;

    public string ConditionUnlockText => Condition.UnlockText;

    public string ConditionPossibleText => Condition.PossibleText;

    public string ConditionSourceText => Condition.SourceText;

    public bool ShowOfficialHint => Condition.ShowOfficialHint;

    public bool ShowNoConditionData => IsExpanded && !HasConditionData;

    public string ConditionToggleText => IsExpanded
        ? T(Keys.AchievementsConditionCollapse)
        : T(Keys.AchievementsConditionExpand);

    /// <summary>来源行的可读文案（本地化键 + 路径），不把键暴露给用户。</summary>
    public string ConditionSourceDisplay => Condition.SourceText.Length == 0
        ? string.Empty
        : T(Keys.AchievementsConditionSource, Condition.SourceText);

    public string ConditionToggleAutomationId => Condition.ToggleAutomationId;

    public string ConditionDetailsAutomationId => Condition.DetailsAutomationId;

    public string ConditionSummaryAutomationId => Condition.SummaryAutomationId;

    public string ConditionUnlockAutomationId => Condition.UnlockAutomationId;

    public string ConditionPossibleAutomationId => Condition.PossibleAutomationId;

    public string ConditionSourceAutomationId => Condition.SourceAutomationId;

    public string ConditionNoDataAutomationId => Condition.NoDataAutomationId;

    private void ToggleCondition()
    {
        IsExpanded = !IsExpanded;
        OnPropertyChanged(nameof(ShowConditionDetails));
        OnPropertyChanged(nameof(ShowNoConditionData));
        OnPropertyChanged(nameof(ConditionToggleText));
    }

    private AchievementIconPresentation ResolveIcon()
    {
        if (!_iconSettled)
        {
            _iconSettled = true;
            try
            {
                _icon = _iconFactory?.Invoke();
            }
            catch
            {
                // 图标只是装饰：解析/解码炸了就回占位方块，不能把整行拖崩。
                _icon = null;
            }
        }

        if (_icon is { Url.Length: > 0 } icon)
        {
            return icon;
        }

        // 本地没有图时退回数据源自带的图标地址（Steam 目前不给，留作兜底）。
        return string.IsNullOrWhiteSpace(_entry.IconUrl)
            ? new AchievementIconPresentation(string.Empty, 1.0)
            : new AchievementIconPresentation(_entry.IconUrl!, 1.0);
    }
}
