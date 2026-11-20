namespace PdxLauncher.App.Services;

/// <summary>
/// 整窗背景的解码宽度策略。
/// <para>
/// 旧实现固定按 2560 解码：1200×800 的默认窗口在 100% 缩放下只需要 1200 像素宽，
/// 2560 相当于多解约 4 倍像素（约 14.7 MB vs 3.7 MB），既拖慢解码，也在赋给
/// <c>ImageBrush</c> 时多花一次大位图上传 —— 实测「解码完成 → 背景上屏」这一段就能到 100 ms 以上。
/// </para>
/// <para>
/// 这里按窗口的物理像素定档：取窗口两条边里较长的那条（横图铺满横窗时由宽度决定，
/// 竖窗由高度决定），向上对齐到 128 px，再夹在 <see cref="Min"/>~<see cref="Max"/> 之间。
/// 窗口事后被拉大时由 <see cref="NeedsUpgrade"/> 判断是否需要按新宽度重解一次；
/// 只放大不缩小，避免来回缩放时反复解码。
/// </para>
/// </summary>
public static class BackdropDecodeWidth
{
    /// <summary>下限：再小的窗口也按 1280 解码，缩到小窗/高 DPI 时不至于发糊。</summary>
    public const int Min = 1280;

    /// <summary>上限：与旧实现一致，4K 窗口不会超过 2560。</summary>
    public const int Max = 2560;

    /// <summary>对齐粒度：8 的倍数且 128 对齐，解码器与纹理上传都按这个量级取整最划算。</summary>
    private const int Alignment = 128;

    /// <summary>按窗口物理像素算解码宽度；尺寸非法（0 / NaN / 负数）时退回 <see cref="Min"/>。</summary>
    public static int Resolve(double windowWidthPx, double windowHeightPx)
    {
        var longest = Math.Max(windowWidthPx, windowHeightPx);
        if (!double.IsFinite(longest) || longest <= 0)
        {
            return Min;
        }

        var aligned = (int)Math.Ceiling(longest / Alignment) * Alignment;
        return Math.Clamp(aligned, Min, Max);
    }

    /// <summary>
    /// 窗口变大后是否需要按更大的宽度重解：新宽度至少比当前宽 15% 才值得再来一次，
    /// 否则轻微缩放就会反复解码。
    /// </summary>
    public static bool NeedsUpgrade(int currentDecodeWidth, double windowWidthPx, double windowHeightPx)
    {
        if (currentDecodeWidth <= 0)
        {
            return true;
        }

        var wanted = Resolve(windowWidthPx, windowHeightPx);
        return wanted >= currentDecodeWidth * 1.15;
    }
}
