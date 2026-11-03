namespace PdxLauncher.App.Services;

/// <summary>
/// 把「控件上占多少 DIP」换算成 <c>BitmapImage.DecodePixelWidth</c>（物理像素）。
/// <para>
/// 列表里的图以前直接 <c>new BitmapImage { UriSource = ... }</c>，按原图解码：
/// 一张 1900×1900 的工坊封面解出来约 14MB，却只画在 48px 的格子里。
/// 这里按「显示尺寸 × 屏幕缩放 × 1.25 余量」给一个上限，并夹到源图真实宽度——
/// 放大的解码只会白白多占内存，所以源图比目标小时按源图来。
/// </para>
/// </summary>
public static class ImageDecodeSize
{
    /// <summary>超采样系数：留一点余量给轻微放大和亚像素对齐，又不至于为 1.25 倍多占 56% 内存。</summary>
    public const double Oversample = 1.25;

    /// <summary>解码宽度下限；再小也按这个解码，避免糊成色块。</summary>
    public const int MinWidth = 16;

    /// <summary>解码宽度上限；详情大图允许到 2048，再大对肉眼没有收益。</summary>
    public const int MaxWidth = 2048;

    /// <summary>
    /// 计算解码宽度。<paramref name="displayDip"/> 是控件实际占用的逻辑像素；
    /// <paramref name="rasterizationScale"/> 是屏幕缩放（1.0 / 1.25 / 1.5 / 2.0）；
    /// <paramref name="sourcePixelWidth"/> 是源图真实宽度，未知传 0。
    /// 返回 0 表示「不设 DecodePixelWidth，按原图解码」。
    /// </summary>
    public static int Compute(double displayDip, double rasterizationScale, int sourcePixelWidth)
    {
        if (displayDip <= 0 || double.IsNaN(displayDip) || double.IsInfinity(displayDip))
        {
            return 0;
        }

        double scale = rasterizationScale > 0 && !double.IsNaN(rasterizationScale) ? rasterizationScale : 1.0;
        int width = (int)Math.Ceiling(displayDip * scale * Oversample);
        width = Math.Clamp(width, MinWidth, MaxWidth);

        if (sourcePixelWidth > 0 && width > sourcePixelWidth)
        {
            width = sourcePixelWidth;
        }

        return width;
    }
}
