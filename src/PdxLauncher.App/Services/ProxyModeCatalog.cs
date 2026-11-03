namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 设置页「网络代理」下拉的一项。文案按 i18n 键取，语言切换后由基类重新发 PropertyChanged。
/// </summary>
public sealed class ProxyModeOption : LocalizedObservableObject
{
    private readonly string _titleKey;
    private readonly string _descriptionKey;

    internal ProxyModeOption(string key, string titleKey, string descriptionKey)
    {
        Key = key;
        _titleKey = titleKey;
        _descriptionKey = descriptionKey;
    }

    /// <summary>写进 settings.json 的值，见 <see cref="ProxyModes"/>。</summary>
    public string Key { get; }

    /// <summary>下拉里显示的名字。</summary>
    public string Title => T(_titleKey);

    /// <summary>下拉项的 UI 自动化名称取自 ToString，返回本地化标题。</summary>
    public override string ToString() => Title;

    /// <summary>选中后显示在下方的一行说明。</summary>
    public string Description => T(_descriptionKey);
}

/// <summary>
/// 网络代理模式的目录。顺序即下拉顺序：默认「直接连接」排第一。
/// 与 <see cref="PlaySetPageDisplayModeCatalog"/> 一样返回同一批实例，
/// 这样下拉控件的 SelectedItem 能靠引用相等匹配上（否则每次重建选项都会丢选中项）。
/// </summary>
public static class ProxyModeCatalog
{
    /// <summary>全部可选模式。</summary>
    public static readonly IReadOnlyList<ProxyModeOption> All = new[]
    {
        new ProxyModeOption(
            ProxyModes.Direct,
            Keys.SettingsProxyModeDirectTitle,
            Keys.SettingsProxyModeDirectDesc),
        new ProxyModeOption(
            ProxyModes.System,
            Keys.SettingsProxyModeSystemTitle,
            Keys.SettingsProxyModeSystemDesc),
        new ProxyModeOption(
            ProxyModes.Manual,
            Keys.SettingsProxyModeManualTitle,
            Keys.SettingsProxyModeManualDesc),
    };

    /// <summary>按 settings.json 里的两个字段解析出对应项（旧配置只填了地址时按手动配置处理）。</summary>
    public static ProxyModeOption Resolve(string? mode, string? proxyUrl)
    {
        var normalized = ProxyModes.Normalize(mode, proxyUrl);
        foreach (var option in All)
        {
            if (string.Equals(option.Key, normalized, StringComparison.Ordinal))
            {
                return option;
            }
        }

        return All[0];
    }
}
