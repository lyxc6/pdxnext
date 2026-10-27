namespace PdxLauncher.Core.Configuration;

/// <summary>
/// Mod 缩略图的显示尺寸（settings.json 的 modThumbnailSize 字段）。
/// 与背景显示方式一样用字符串常量：未知枚举值会让整份设置反序列化失败。
/// </summary>
public static class ModThumbnailSizes
{
    /// <summary>小：32 像素。</summary>
    public const string Small = "small";

    /// <summary>中：48 像素（默认）。</summary>
    public const string Medium = "medium";

    /// <summary>大：72 像素。</summary>
    public const string Large = "large";

    /// <summary>小档边长（逻辑像素）。</summary>
    public const int SmallPixels = 32;

    /// <summary>中档边长（逻辑像素）。</summary>
    public const int MediumPixels = 48;

    /// <summary>大档边长（逻辑像素）。</summary>
    public const int LargePixels = 72;

    /// <summary>把任意设置值归一化成合法取值；null / 空白 / 未知值一律回退 <see cref="Medium"/>。</summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Medium;
        }

        var trimmed = value.Trim();
        if (string.Equals(trimmed, Small, StringComparison.OrdinalIgnoreCase))
        {
            return Small;
        }

        if (string.Equals(trimmed, Large, StringComparison.OrdinalIgnoreCase))
        {
            return Large;
        }

        return Medium;
    }

    /// <summary>取值对应的边长（逻辑像素）；未知值按中档 48 处理。</summary>
    public static int Pixels(string? value) => Normalize(value) switch
    {
        Small => SmallPixels,
        Large => LargePixels,
        _ => MediumPixels,
    };
}
