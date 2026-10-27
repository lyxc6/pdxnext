namespace PdxLauncher.Imaging.Dds;

/// <summary>按 DDS 头里的像素格式派发到具体解码器，并把结果整理成 RGBA32。</summary>
public static class DdsDecoder
{
    private const int PixelBytes = 4;

    /// <summary>解码 DDS 的 mip0（可能顺带缩小）。</summary>
    /// <param name="data">完整文件字节。</param>
    /// <param name="maxDimension">大于 0 时把结果按最近邻缩到长边不超过该值；0 表示原尺寸。</param>
    /// <returns>成功时 <see cref="DdsDecodeResult.IsSuccess"/> 为 true。</returns>
    public static DdsDecodeResult Decode(ReadOnlySpan<byte> data, int maxDimension = 0)
    {
        if (!DdsHeader.TryParse(data, out DdsImageInfo info, out string error))
        {
            return DdsDecodeResult.Failure(error);
        }

        int width = info.Width;
        int height = info.Height;
        byte[] rgba = new byte[width * height * PixelBytes];
        ReadOnlySpan<byte> payload = data.Slice(info.DataOffset, info.DataLength);

        switch (info.Format)
        {
            case DdsFormat.Bc1:
                BlockDecoder.DecodeBc1(payload, width, height, rgba);
                break;
            case DdsFormat.Bc2:
                BlockDecoder.DecodeBc2(payload, width, height, rgba);
                break;
            case DdsFormat.Bc3:
                BlockDecoder.DecodeBc3(payload, width, height, rgba);
                break;
            case DdsFormat.Bc4:
                BlockDecoder.DecodeBc4(payload, width, height, rgba);
                break;
            case DdsFormat.Bc5:
                BlockDecoder.DecodeBc5(payload, width, height, rgba);
                break;
            case DdsFormat.Bc7:
                if (!DecodeBc7(payload, width, height, rgba))
                {
                    return DdsDecodeResult.Failure("BC7 数据块非法");
                }

                break;
            case DdsFormat.Rgba8:
            case DdsFormat.Bgra8:
            case DdsFormat.Bgrx8:
            case DdsFormat.Bgr8:
                DecodeUncompressed(payload, info.Format, width, height, rgba);
                break;
            default:
                return DdsDecodeResult.Failure($"不支持的 DDS 格式：{info.Format}");
        }

        PixelBuffer buffer = new(width, height, rgba);
        if (maxDimension > 0 && (width > maxDimension || height > maxDimension))
        {
            buffer = Downscale(buffer, maxDimension);
        }

        return DdsDecodeResult.Success(buffer);
    }

    private static bool DecodeBc7(ReadOnlySpan<byte> payload, int width, int height, Span<byte> rgba)
    {
        Span<byte> block = stackalloc byte[16 * PixelBytes];
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;
        for (int by = 0; by < blocksY; by++)
        {
            for (int bx = 0; bx < blocksX; bx++)
            {
                int sourceOffset = (by * blocksX + bx) * 16;
                if (!Bc7Decoder.DecodeBlock(payload.Slice(sourceOffset, 16), block))
                {
                    return false;
                }

                int x = bx * 4;
                int y = by * 4;
                for (int row = 0; row < 4 && y + row < height; row++)
                {
                    for (int col = 0; col < 4 && x + col < width; col++)
                    {
                        int source = (row * 4 + col) * PixelBytes;
                        int target = ((y + row) * width + (x + col)) * PixelBytes;
                        rgba[target] = block[source];
                        rgba[target + 1] = block[source + 1];
                        rgba[target + 2] = block[source + 2];
                        rgba[target + 3] = block[source + 3];
                    }
                }
            }
        }

        return true;
    }

    private static void DecodeUncompressed(ReadOnlySpan<byte> payload, DdsFormat format, int width, int height, Span<byte> rgba)
    {
        int pixels = width * height;
        switch (format)
        {
            case DdsFormat.Rgba8:
                payload[..(pixels * 4)].CopyTo(rgba);
                break;
            case DdsFormat.Bgra8:
                for (int i = 0; i < pixels; i++)
                {
                    int source = i * 4;
                    int target = i * 4;
                    rgba[target] = payload[source + 2];
                    rgba[target + 1] = payload[source + 1];
                    rgba[target + 2] = payload[source];
                    rgba[target + 3] = payload[source + 3];
                }

                break;
            case DdsFormat.Bgrx8:
                for (int i = 0; i < pixels; i++)
                {
                    int source = i * 4;
                    int target = i * 4;
                    rgba[target] = payload[source + 2];
                    rgba[target + 1] = payload[source + 1];
                    rgba[target + 2] = payload[source];
                    rgba[target + 3] = 255;
                }

                break;
            case DdsFormat.Bgr8:
                for (int i = 0; i < pixels; i++)
                {
                    int source = i * 3;
                    int target = i * 4;
                    rgba[target] = payload[source + 2];
                    rgba[target + 1] = payload[source + 1];
                    rgba[target + 2] = payload[source];
                    rgba[target + 3] = 255;
                }

                break;
        }
    }

    private static PixelBuffer Downscale(PixelBuffer source, int maxDimension)
    {
        double scale = Math.Min(
            (double)maxDimension / source.Width,
            (double)maxDimension / source.Height);
        int width = Math.Max(1, (int)Math.Round(source.Width * scale));
        int height = Math.Max(1, (int)Math.Round(source.Height * scale));
        byte[] target = new byte[width * height * PixelBytes];

        for (int y = 0; y < height; y++)
        {
            int sourceY = Math.Min(source.Height - 1, (int)((long)y * source.Height / height));
            for (int x = 0; x < width; x++)
            {
                int sourceX = Math.Min(source.Width - 1, (int)((long)x * source.Width / width));
                int from = (sourceY * source.Width + sourceX) * PixelBytes;
                int to = (y * width + x) * PixelBytes;
                target[to] = source.Rgba32[from];
                target[to + 1] = source.Rgba32[from + 1];
                target[to + 2] = source.Rgba32[from + 2];
                target[to + 3] = source.Rgba32[from + 3];
            }
        }

        return new PixelBuffer(width, height, target);
    }
}
