namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml.Media;

/// <summary>
/// XAML 侧的图片缩放方式辅助函数。用 x:Bind 静态函数代替 IValueConverter，
/// 这样 ViewModel 里可以只保留 bool，不出现任何 UI 类型。
/// </summary>
public static class XamlStretch
{
    /// <summary>
    /// 本地横图（280×160 ≈ 16:9）填满图片框；Steam 头图（460×215 = 2.14:1）和
    /// EU5 的 500×500 方形本地缩略图用 Uniform 完整显示。
    /// <para>
    /// 方图若用 UniformToFill 填进 208×117 的 16:9 框，上下会被各裁约 22%，
    /// DLC 盾徽/建筑只剩中间一条，看起来就是「歪」。
    /// </para>
    /// </summary>
    public static Stretch ForDlcImage(bool fill) =>
        fill ? Stretch.UniformToFill : Stretch.Uniform;
}
