namespace PdxLauncher.App.ViewModels;

using CommunityToolkit.Mvvm.ComponentModel;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

/// <summary>
/// 筛选栏里的一个选项（类别标签、更新兼容性，或「所需的 DLC」里的一个 DLC）。
/// <para>
/// 界面用三态胶囊（<see cref="Controls.PdxTagChip"/>）显示：未选 → 必须包含 → 必须排除 → 未选，
/// 由 <see cref="Cycle"/> 轮换。两个布尔量仍然互斥，选项本身不触发查询，
/// 由 <see cref="WorkshopFilterGroupViewModel"/> 聚合成一次 <c>Changed</c>。
/// </para>
/// </summary>
public sealed partial class WorkshopTagOption : LocalizedObservableObject
{
    private readonly string _displayName;
    private readonly bool _localize;

    /// <summary>创建选项。</summary>
    /// <param name="tag">发给服务端的值：标签名，或 DLC 的 appid 字符串。</param>
    /// <param name="displayName">Steam 给的展示名。</param>
    /// <param name="localize">是否把展示名换成界面语言；DLC 商品名传 false。</param>
    public WorkshopTagOption(string tag, string displayName, bool localize = true)
    {
        Tag = tag;
        _displayName = displayName;
        _localize = localize;
    }

    /// <summary>发给服务端的值（标签名 / DLC appid）。</summary>
    public string Tag { get; }

    /// <summary>展示名（标签随界面语言切换；DLC 商品名保持原文）。</summary>
    public string DisplayName => _localize ? WorkshopTagText.Localize(Tag, _displayName) : _displayName;

    /// <summary>是否必须包含（胶囊的「+」态）。</summary>
    [ObservableProperty]
    private bool _isRequired;

    /// <summary>是否必须排除（胶囊的「−」态）。</summary>
    [ObservableProperty]
    private bool _isExcluded;

    /// <summary>是否处于任一选中状态（用于高亮行）。</summary>
    public bool IsActive => IsRequired || IsExcluded;

    partial void OnIsRequiredChanged(bool value)
    {
        if (value && IsExcluded)
        {
            IsExcluded = false;
        }

        OnPropertyChanged(nameof(IsActive));
    }

    partial void OnIsExcludedChanged(bool value)
    {
        if (value && IsRequired)
        {
            IsRequired = false;
        }

        OnPropertyChanged(nameof(IsActive));
    }

    /// <summary>清空状态（不负责通知聚合层，由分组统一处理）。</summary>
    public void Reset()
    {
        IsRequired = false;
        IsExcluded = false;
    }

    /// <summary>
    /// 按胶囊点击轮换状态：未选 → 必须包含 → 必须排除 → 未选。
    /// <para>
    /// 两个布尔量互斥的规则仍由 <c>OnIsRequiredChanged</c> / <c>OnIsExcludedChanged</c> 保证，
    /// 这里只决定下一步该置哪个，避免两处各写一份互斥逻辑。
    /// </para>
    /// </summary>
    public void Cycle()
    {
        if (IsRequired)
        {
            // 先置排除再清包含：中间态始终只有一个为真。
            IsExcluded = true;
            IsRequired = false;
        }
        else if (IsExcluded)
        {
            IsExcluded = false;
        }
        else
        {
            IsRequired = true;
        }
    }
}
