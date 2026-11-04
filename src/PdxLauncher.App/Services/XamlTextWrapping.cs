namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml;

/// <summary>
/// XAML 侧的文本换行辅助函数。用 x:Bind 静态函数代替 IValueConverter，
/// 这样 ViewModel 里可以只保留 bool，不出现任何 UI 类型。
/// </summary>
public static class XamlTextWrapping
{
    /// <summary>true → 自动换行，false → 不换行（长行横向滚动）。</summary>
    public static TextWrapping FromBool(bool wrap) =>
        wrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
}
