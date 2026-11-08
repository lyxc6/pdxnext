namespace PdxLauncher.App.Services;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

/// <summary>
/// XAML 侧的颜色辅助函数：把「结论类别 / 拥有类别」的字符串换成主题画笔。
/// <para>
/// 画笔定义在 <c>App.xaml</c> 的三套 ThemeDictionary 里（<c>PdxOwnedForegroundBrush</c> 等），
/// 所以换主题时颜色自动跟着走；ViewModel 里仍然只有字符串，不出现任何 UI 类型。
/// </para>
/// </summary>
public static class XamlBrush
{
    private const string OwnedKey = "PdxOwnedForegroundBrush";
    private const string UnownedKey = "PdxUnownedForegroundBrush";
    private const string UnknownKey = "PdxUnknownForegroundBrush";

    private static Brush? _fallback;

    /// <summary>授权判定条的配色：ok / warn / 其它（中性）。</summary>
    public static Brush FromVerdict(string? kind) => kind switch
    {
        "ok" => Lookup(OwnedKey),
        "warn" => Lookup(UnownedKey),
        _ => Lookup(UnknownKey),
    };

    /// <summary>单行徽标的配色：owned / unowned / 其它（未知）。</summary>
    public static Brush FromOwnership(string? kind) => kind switch
    {
        "owned" => Lookup(OwnedKey),
        "unowned" => Lookup(UnownedKey),
        _ => Lookup(UnknownKey),
    };

    /// <summary>
    /// 补丁覆盖徽标的配色：applied（绿）/ overwritten（提醒色，补丁被 Steam 冲掉了）/ covered（中性）。
    /// <para>「可覆盖」只是说明，不等于已生效，所以不用绿色。</para>
    /// </summary>
    public static Brush FromCoverage(string? kind) => kind switch
    {
        "applied" => Lookup(OwnedKey),
        "overwritten" => Lookup(UnownedKey),
        _ => Lookup(UnknownKey),
    };

    /// <summary>
    /// 内容来源徽标的配色：steam（绿，Steam 自己装的）/ missing（提醒色，授权通了但内容没落盘）/
    /// 其它（中性：仅本地文件、或判不了）。
    /// </summary>
    public static Brush FromContent(string? kind) => kind switch
    {
        "steam" => Lookup(OwnedKey),
        "missing" => Lookup(UnownedKey),
        _ => Lookup(UnknownKey),
    };

    private static Brush Lookup(string key)
    {
        if (Application.Current is { } app
            && app.Resources.TryGetValue(key, out var value)
            && value is Brush brush)
        {
            return brush;
        }

        // 资源被换掉时也别把前景色设成 null（那会让文字看不见），退回中性灰。
        return _fallback ??= new SolidColorBrush(Microsoft.UI.Colors.Gray);
    }
}
