namespace PdxLauncher.Core.Online;

/// <summary>
/// 判断一张主题背景图是不是「纱层」：整幅图用同一个很低的不透明度导出，
/// 本意是压在官方启动器的画面底纹上，单看几乎没有内容。
/// <para>
/// 实测 EU4 的 assets/app-background.png 是 1920×1080 RGBA，alpha 全幅恒为 51/255（20%），
/// RGB 却是一张完整的明亮油画（均值约 98，最大值 255）。把它按 alpha 直接合成到
/// 卡片底色 #27272C 上，结果就是一片没有层次的灰 —— 用户看到的「发灰」正是这个。
/// 这类图应当被摊平（丢弃那层均匀 alpha）后再当封面/整窗背景用。
/// </para>
/// </summary>
public static class WashLayerPolicy
{
    /// <summary>alpha 直方图桶数（0–255）。</summary>
    public const int AlphaBucketCount = 256;

    /// <summary>峰值不透明度上限：低于 96/255（约 38%）说明整幅图都盖不住底色。</summary>
    public const int MaximumPeakAlpha = 96;

    /// <summary>不透明像素（alpha ≥ 240）占比上限：超过 1% 就是真正的半透明美术，不能摊平。</summary>
    public const double MaximumOpaqueFraction = 0.01;

    /// <summary>判定为纱层时是否把 alpha 直接置满（摊平）。规则唯一，留个名字便于日志解释。</summary>
    public const int FlattenedAlpha = 255;

    /// <summary>
    /// 判断给定 alpha 直方图是否代表纱层。
    /// </summary>
    /// <param name="alphaHistogram">长度 256 的直方图，下标即 alpha 值。</param>
    /// <param name="pixelCount">参与统计的像素数。</param>
    public static bool IsWashLayer(ReadOnlySpan<int> alphaHistogram, long pixelCount)
    {
        if (pixelCount <= 0 || alphaHistogram.Length != AlphaBucketCount)
        {
            return false;
        }

        long opaque = 0;
        for (var alpha = 240; alpha < AlphaBucketCount; alpha++)
        {
            opaque += alphaHistogram[alpha];
        }

        if ((double)opaque / pixelCount > MaximumOpaqueFraction)
        {
            return false;
        }

        var peakAlpha = 0;
        for (var alpha = AlphaBucketCount - 1; alpha > 0; alpha--)
        {
            if (alphaHistogram[alpha] > 0)
            {
                peakAlpha = alpha;
                break;
            }
        }

        // 全透明图没有任何可见内容，摊平也还是空，交给调用方按原逻辑处理。
        return peakAlpha > 0 && peakAlpha <= MaximumPeakAlpha;
    }

    /// <summary>统计 alpha 直方图；<paramref name="stride"/> 是像素步长（采样用，≥1）。</summary>
    public static int[] BuildAlphaHistogram(ReadOnlySpan<byte> bgraPixels, int stride = 1)
    {
        if (bgraPixels.Length % 4 != 0)
        {
            throw new ArgumentException("BGRA 像素缓冲区长度必须是 4 的倍数。", nameof(bgraPixels));
        }

        var step = Math.Max(1, stride);
        var histogram = new int[AlphaBucketCount];
        var pixelCount = bgraPixels.Length / 4;
        for (var i = 0; i < pixelCount; i += step)
        {
            histogram[bgraPixels[(i * 4) + 3]]++;
        }

        return histogram;
    }
}
