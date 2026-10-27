namespace PdxLauncher.Core.Configuration;

/// <summary>
/// 整窗背景图的显示方式（settings.json 的 backgroundDisplayMode 字段）。
/// 用字符串常量而不是枚举：AppSettingsStore 的 JSON 反序列化遇到未知枚举值会抛异常，
/// 进而让整份设置回退默认值；字符串可以安全兜底。
/// </summary>
public static class BackgroundDisplayModes
{
    /// <summary>覆盖：等比放大铺满整窗，放不下的部分裁掉（默认，与原版启动器一致）。</summary>
    public const string Cover = "cover";

    /// <summary>填充：直接拉伸铺满整窗，不裁切但比例不符时会变形。</summary>
    public const string Fill = "fill";

    /// <summary>完整（模糊底）：整图完整显示，四周用同一张图的模糊放大版填满。</summary>
    public const string FullBlur = "fullBlur";

    /// <summary>完整（纯色边）：整图完整显示，四周露出窗口底色。</summary>
    public const string FullPlain = "fullPlain";

    /// <summary>居中：按图片原始像素居中，超出窗口的部分裁掉。</summary>
    public const string Center = "center";

    /// <summary>把任意设置值归一化成合法取值；null / 空白 / 未知值一律回退 <see cref="Cover"/>。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Cover;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, Fill, StringComparison.OrdinalIgnoreCase))
        {
            return Fill;
        }

        if (string.Equals(trimmed, FullBlur, StringComparison.OrdinalIgnoreCase))
        {
            return FullBlur;
        }

        if (string.Equals(trimmed, FullPlain, StringComparison.OrdinalIgnoreCase))
        {
            return FullPlain;
        }

        if (string.Equals(trimmed, Center, StringComparison.OrdinalIgnoreCase))
        {
            return Center;
        }

        return Cover;
    }

    /// <summary>该取值是否需要额外渲染一层同图模糊铺底（只有 <see cref="FullBlur"/> 需要）。</summary>
    public static bool UsesBlurredBackdrop(string? value) =>
        string.Equals(Normalize(value), FullBlur, StringComparison.Ordinal);
}
