namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 「添加模组」子页里的一行：mod 库中的一条候选 mod。
/// </summary>
/// <remarks>
/// 行自己持有勾选状态（<see cref="IsSelected"/>），因此搜索过滤时只要复用同一批行对象，
/// 勾选就不会丢；父 ViewModel 通过回调拿到变化并刷新计数。
/// </remarks>
public sealed class AddModRowViewModel : LocalizedObservableObject
{
    private readonly Action<AddModRowViewModel, bool>? _onSelectionChanged;
    private bool _isSelected;

    /// <summary>创建一行候选 mod。</summary>
    /// <param name="entry">mod 库条目。</param>
    /// <param name="onSelectionChanged">勾选状态变化时的回调（可为 null）。</param>
    public AddModRowViewModel(ModLibraryEntry entry, Action<AddModRowViewModel, bool>? onSelectionChanged = null)
    {
        ArgumentNullException.ThrowIfNull(entry);

        Entry = entry;
        _onSelectionChanged = onSelectionChanged;
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
    public string RowAutomationId => "addModCandidate_" + ModId.ToString("N");

    /// <summary>勾选框的 AutomationId。</summary>
    public string CheckAutomationId => "addModCandidateCheck_" + ModId.ToString("N");

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
