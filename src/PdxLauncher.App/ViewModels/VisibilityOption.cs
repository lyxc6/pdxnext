namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Steam;

/// <summary>
/// 可见性下拉的一项。<see cref="Text"/> 是文案键，语言切换时由 ViewModel 重建列表刷新，
/// 所以这里不缓存翻译结果。
/// </summary>
public sealed class VisibilityOption
{
    /// <summary>构造。</summary>
    public VisibilityOption(SteamWorkshopVisibility value, string textKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(textKey);
        Value = value;
        TextKey = textKey;
    }

    /// <summary>背后对应的 Steam 可见性。</summary>
    public SteamWorkshopVisibility Value { get; }

    /// <summary>文案键。</summary>
    public string TextKey { get; }

    /// <summary>当前语言下的显示文本。</summary>
    public string Text => Localizer.Instance.T(TextKey);
}
