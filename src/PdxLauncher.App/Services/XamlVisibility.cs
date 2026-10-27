namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml;

/// <summary>
/// XAML 侧的可见性/文本辅助函数。用 x:Bind 静态函数代替 IValueConverter，
/// 这样 ViewModel 里可以只保留 bool / string，不出现任何 UI 类型。
/// </summary>
public static class XamlVisibility
{
    /// <summary>true → Visible，false → Collapsed。</summary>
    public static Visibility FromBool(bool value) =>
        value ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>true → Collapsed，false → Visible。</summary>
    public static Visibility FromBoolInverse(bool value) =>
        value ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>非空字符串 → Visible。</summary>
    public static Visibility FromString(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>空字符串 → Visible；有内容时 → Collapsed（用于「字标存在时不再重复显示纯文字标题」）。</summary>
    public static Visibility FromStringInverse(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>空字符串 → Visible（用于占位提示）。</summary>
    public static Visibility FromStringEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>非空取原值，否则取兜底文案。</summary>
    public static string OrFallback(string? value, string fallback) =>
        string.IsNullOrWhiteSpace(value) ? fallback : value;
}
