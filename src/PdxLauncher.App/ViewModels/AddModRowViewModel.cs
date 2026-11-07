namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 「添加模组」子页 / 播放集页左栏里的一行：mod 库中的一条候选 mod。
/// </summary>
/// <remarks>
/// 行自己持有勾选状态（<see cref="IsSelected"/>），因此搜索过滤时只要复用同一批行对象，
/// 勾选就不会丢；父 ViewModel 通过回调拿到变化并刷新计数。
/// </remarks>
public sealed class AddModRowViewModel : LocalizedObservableObject
{
    /// <summary>「添加模组」子页的候选行 Id 前缀。</summary>
    public const string DefaultAutomationPrefix = "addModCandidate";

    /// <summary>mod 没有声明支持版本时的占位：一个短横，避免版本列在长名字右侧忽宽忽窄。</summary>
    public const string MissingVersionText = "—";

    private readonly Action<AddModRowViewModel, bool>? _onSelectionChanged;
    private readonly string _automationPrefix;
    private readonly string? _gameVersion;
    private bool _isSelected;

    /// <summary>创建一行候选 mod。</summary>
    /// <param name="entry">mod 库条目。</param>
    /// <param name="onSelectionChanged">勾选状态变化时的回调（可为 null）。</param>
    /// <param name="automationPrefix">
    /// 行与勾选框的 AutomationId 前缀。默认值等于「添加模组」子页用的 <c>addModCandidate</c>，
    /// 播放集页左栏传 <c>modCandidate</c>，两处的 Id 不会撞车（UI 自动化脚本靠 Id 定位）。
    /// </param>
    /// <param name="gameVersion">当前游戏版本，只用于判断「声明的支持版本是否偏低」；未知时传 null。</param>
    public AddModRowViewModel(
        ModLibraryEntry entry,
        Action<AddModRowViewModel, bool>? onSelectionChanged = null,
        string automationPrefix = DefaultAutomationPrefix,
        string? gameVersion = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationPrefix);

        Entry = entry;
        _onSelectionChanged = onSelectionChanged;
        _automationPrefix = automationPrefix;
        _gameVersion = gameVersion;
    }

    /// <summary>对应的 mod 库条目。</summary>
    public ModLibraryEntry Entry { get; }

    /// <summary>mod id（mods.id）。</summary>
    public Guid ModId => Entry.ModId;

    /// <summary>展示名。</summary>
    public string Name => Entry.Name;

    /// <summary>游戏注册 ID；可能为空串。</summary>
    public string RegistryId => Entry.GameRegistryId;

    /// <summary>大小文案：1010.6KB / 128.96MB；未知时为空串。</summary>
    public string SizeText => ModSizeText.Format(Entry.SizeBytes);

    /// <summary>来源徽标文案：本地 / 订阅。</summary>
    public string OriginText => T(ModOrigins.DescribeKey(Entry.Origin));

    /// <summary>是不是本地 mod（XAML 用不同底色区分）。</summary>
    public bool IsLocalOrigin => Entry.Origin == ModOrigin.Local;

    /// <summary>是不是订阅 mod。</summary>
    public bool IsSubscribedOrigin => Entry.Origin == ModOrigin.Subscribed;

    /// <summary>副标题：大小与注册 ID 用「 · 」拼接，任一段为空则自动省略。</summary>
    public string SubtitleText => string.Join(
        " · ",
        new[] { SizeText, RegistryId }.Where(static part => !string.IsNullOrWhiteSpace(part)));

    /// <summary>缩略图路径；没有图时为空串。</summary>
    public string ThumbnailPath => Entry.ThumbnailPath ?? string.Empty;

    /// <summary>有没有缩略图。</summary>
    public bool HasThumbnail => !string.IsNullOrWhiteSpace(Entry.ThumbnailPath);

    /// <summary>缩略图边长（像素）。</summary>
    public double ThumbnailSize => ModThumbnailSizes.MediumPixels;

    /// <summary>行本身的 AutomationId。</summary>
    public string RowAutomationId => _automationPrefix + "_" + ModId.ToString("N");

    /// <summary>勾选框的 AutomationId。</summary>
    public string CheckAutomationId => _automationPrefix + "Check_" + ModId.ToString("N");

    /// <summary>版本列的 AutomationId。</summary>
    public string VersionAutomationId => _automationPrefix + "Version_" + ModId.ToString("N");

    /// <summary>
    /// 版本列文案：有声明时是「支持 1.9.*」，没有声明时是一个短横（保持列宽稳定）。
    /// </summary>
    public string RequiredVersionText => string.IsNullOrWhiteSpace(Entry.RequiredVersion)
        ? MissingVersionText
        : T(Keys.ModsAddPageVersion, Entry.RequiredVersion);

    /// <summary>
    /// 声明的支持版本是否低于当前游戏版本：判定复用 <see cref="ModLibraryWarnings"/>，
    /// 这里不自己做版本比较（版本号解析规则只该有一份）。
    /// </summary>
    public bool HasVersionWarning
        => ModLibraryWarnings.ClassifyVersionSupport(Entry.RequiredVersion, _gameVersion)
            == ModVersionSupport.Below;

    /// <summary>版本列的提示文案：只在版本偏低时给一句解释，否则为空串。</summary>
    public string VersionWarningText => HasVersionWarning
        ? T(Keys.ModlibVersionSupportDeclared, Entry.RequiredVersion, _gameVersion)
            + " "
            + T(Keys.ModlibVersionSupportBelow)
        : string.Empty;

    /// <summary>是否被勾选（要加入播放集）。</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
            {
                return;
            }

            _isSelected = value;
            OnPropertyChanged();
            _onSelectionChanged?.Invoke(this, value);
        }
    }

    /// <summary>按名称 / 注册 ID 过滤（大小不参与匹配，避免输入 "1" 命中一堆行）。</summary>
    public bool Matches(string query)
    {
        return Entry.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || Entry.GameRegistryId.Contains(query, StringComparison.OrdinalIgnoreCase);
    }
}
