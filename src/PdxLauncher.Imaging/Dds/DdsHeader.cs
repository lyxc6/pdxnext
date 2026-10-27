using System.Buffers.Binary;

namespace PdxLauncher.Imaging.Dds;

/// <summary>DDS 文件解析结果（只描述 mip0，不含像素内容）。</summary>
/// <param name="Width">像素宽度。</param>
/// <param name="Height">像素高度。</param>
/// <param name="MipCount">文件声明的 mip 级数（未知时为 1）。</param>
/// <param name="Format">像素格式。</param>
/// <param name="DataOffset">mip0 像素数据在文件中的起始偏移。</param>
/// <param name="DataLength">mip0 像素数据应有的字节数。</param>
public readonly record struct DdsImageInfo(
    int Width,
    int Height,
    int MipCount,
    DdsFormat Format,
    int DataOffset,
    int DataLength);

/// <summary>解析 DDS 文件头（0x124 字节），校验尺寸与像素数据长度。</summary>
public static class DdsHeader
{
    private const uint Magic = 0x20534444;
    private const int HeaderBytes = 128;
    private const int Dx10HeaderBytes = 20;

    private const uint DdsdCaps = 0x1;
    private const uint DdsdHeight = 0x2;
    private const uint DdsdWidth = 0x4;
    private const uint DdsdPixelFormat = 0x1000;
    private const uint DdsdMipMapCount = 0x20000;
    private const uint DdsdDepth = 0x800000;

    private const uint DdpfAlphaPixels = 0x1;
    private const uint DdpfFourCc = 0x4;
    private const uint DdpfRgb = 0x40;

    private const uint D3d10ResourceDimensionTexture2D = 3;
    private const uint D3d10ResourceMiscTextureCube = 0x4;

    /// <summary>尝试解析文件头；成功时给出 mip0 的尺寸、格式与数据区间。</summary>
    /// <param name="data">文件字节（至少 128 字节）。</param>
    /// <param name="info">解析结果。</param>
    /// <param name="error">失败原因（中文，可直接写日志）。</param>
    /// <returns>解析成功返回 true。</returns>
    public static bool TryParse(ReadOnlySpan<byte> data, out DdsImageInfo info, out string error)
    {
        info = default;
        error = string.Empty;

        if (data.Length < HeaderBytes)
        {
            error = "DDS 文件不足 128 字节";
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data) != Magic)
        {
            error = "DDS 魔数不匹配";
            return false;
        }

        if (BinaryPrimitives.ReadUInt32LittleEndian(data[4..]) != 124)
        {
            error = "DDS 头长度不是 124 字节";
            return false;
        }

        uint flags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        int height = BinaryPrimitives.ReadInt32LittleEndian(data[12..]);
        int width = BinaryPrimitives.ReadInt32LittleEndian(data[16..]);
        int depth = BinaryPrimitives.ReadInt32LittleEndian(data[24..]);
        int mipCount = BinaryPrimitives.ReadInt32LittleEndian(data[28..]);

        if ((flags & DdsdPixelFormat) == 0)
        {
            error = "DDS 头缺少 pixelFormat 标志";
            return false;
        }

        if ((flags & (DdsdWidth | DdsdHeight)) != (DdsdWidth | DdsdHeight))
        {
            error = "DDS 头缺少 width/height 标志";
            return false;
        }

        if (width <= 0 || height <= 0)
        {
            error = "DDS 尺寸非法";
            return false;
        }

        if (width > 65536 || height > 65536)
        {
            error = "DDS 尺寸过大";
            return false;
        }

        if ((flags & DdsdDepth) != 0 && depth > 1)
        {
            error = "不支持 3D 纹理";
            return false;
        }

        if ((flags & DdsdMipMapCount) == 0 || mipCount <= 0)
        {
            mipCount = 1;
        }

        int pfSize = BinaryPrimitives.ReadInt32LittleEndian(data[76..]);
        uint pfFlags = BinaryPrimitives.ReadUInt32LittleEndian(data[80..]);
        uint fourCc = BinaryPrimitives.ReadUInt32LittleEndian(data[84..]);
        int rgbBits = BinaryPrimitives.ReadInt32LittleEndian(data[88..]);
        uint rMask = BinaryPrimitives.ReadUInt32LittleEndian(data[92..]);
        uint gMask = BinaryPrimitives.ReadUInt32LittleEndian(data[96..]);
        uint bMask = BinaryPrimitives.ReadUInt32LittleEndian(data[100..]);
        uint aMask = BinaryPrimitives.ReadUInt32LittleEndian(data[104..]);

        if (pfSize != 32)
        {
            error = "DDS pixelFormat 长度不是 32 字节";
            return false;
        }

        int dataOffset = HeaderBytes;
        DdsFormat format;

        if ((pfFlags & DdpfFourCc) != 0)
        {
            if (!TryParseFourCc(fourCc, out format))
            {
                if (fourCc != FourCcDx10)
                {
                    error = $"不支持的 DDS fourCC：0x{fourCc:X8}";
                    return false;
                }

                if (!TryParseDx10Header(data, out format, out error))
                {
                    return false;
                }

                dataOffset = HeaderBytes + Dx10HeaderBytes;
            }
        }
        else if ((pfFlags & DdpfRgb) != 0)
        {
            if (!TryParseUncompressed(rgbBits, rMask, gMask, bMask, aMask, out format, out error))
            {
                return false;
            }
        }
        else
        {
            error = $"不支持的 DDS 像素格式标志：0x{pfFlags:X8}";
            return false;
        }

        int dataLength = ComputeTopMipLength(width, height, format);
        if (dataLength <= 0)
        {
            error = "DDS 像素数据长度计算失败";
            return false;
        }

        if (dataOffset + dataLength > data.Length)
        {
            error = $"DDS 像素数据不足：需要 {dataLength} 字节，实际只有 {data.Length - dataOffset} 字节";
            return false;
        }

        info = new DdsImageInfo(width, height, mipCount, format, dataOffset, dataLength);
        return true;
    }

    /// <summary>计算 mip0 的字节数（不含 mip 链）。</summary>
    /// <param name="width">宽度。</param>
    /// <param name="height">高度。</param>
    /// <param name="format">像素格式。</param>
    /// <returns>字节数；格式未知时返回 0。</returns>
    public static int ComputeTopMipLength(int width, int height, DdsFormat format)
    {
        int blocksX = (width + 3) / 4;
        int blocksY = (height + 3) / 4;

        return format switch
        {
            DdsFormat.Bc1 or DdsFormat.Bc4 => blocksX * blocksY * 8,
            DdsFormat.Bc2 or DdsFormat.Bc3 or DdsFormat.Bc5 or DdsFormat.Bc7 => blocksX * blocksY * 16,
            DdsFormat.Rgba8 or DdsFormat.Bgra8 or DdsFormat.Bgrx8 => width * height * 4,
            DdsFormat.Bgr8 => width * height * 3,
            _ => 0
        };
    }

    private const uint FourCcDx10 = 0x30315844; // 'DX10'

    private static bool TryParseFourCc(uint fourCc, out DdsFormat format)
    {
        format = fourCc switch
        {
            0x31545844 => DdsFormat.Bc1, // DXT1
            0x32545844 => DdsFormat.Bc2, // DXT2
            0x33545844 => DdsFormat.Bc2, // DXT3
            0x34545844 => DdsFormat.Bc3, // DXT4
            0x35545844 => DdsFormat.Bc3, // DXT5
            0x31495441 => DdsFormat.Bc4, // ATI1
            0x55344342 => DdsFormat.Bc4, // BC4U
            0x32495441 => DdsFormat.Bc5, // ATI2
            0x55354342 => DdsFormat.Bc5, // BC5U
            _ => DdsFormat.Unknown
        };

        return format != DdsFormat.Unknown;
    }

    private static bool TryParseDx10Header(ReadOnlySpan<byte> data, out DdsFormat format, out string error)
    {
        format = DdsFormat.Unknown;
        error = string.Empty;

        if (data.Length < HeaderBytes + Dx10HeaderBytes)
        {
            error = "DDS DX10 扩展头不完整";
            return false;
        }

        uint dxgiFormat = BinaryPrimitives.ReadUInt32LittleEndian(data[128..]);
        uint resourceDimension = BinaryPrimitives.ReadUInt32LittleEndian(data[132..]);
        uint miscFlag = BinaryPrimitives.ReadUInt32LittleEndian(data[136..]);
        uint arraySize = BinaryPrimitives.ReadUInt32LittleEndian(data[140..]);

        if (resourceDimension != D3d10ResourceDimensionTexture2D)
        {
            error = "只支持 2D 纹理（DX10 resourceDimension != TEXTURE2D）";
            return false;
        }

        if ((miscFlag & D3d10ResourceMiscTextureCube) != 0)
        {
            error = "不支持立方体贴图";
            return false;
        }

        if (arraySize > 1)
        {
            error = "不支持纹理数组";
            return false;
        }

        format = dxgiFormat switch
        {
            71 or 72 => DdsFormat.Bc1,   // BC1_UNORM / SRGB
            74 or 75 => DdsFormat.Bc2,   // BC2_UNORM / SRGB
            77 or 78 => DdsFormat.Bc3,   // BC3_UNORM / SRGB
            80 or 81 => DdsFormat.Bc4,   // BC4_UNORM / SNORM
            83 or 84 => DdsFormat.Bc5,   // BC5_UNORM / SNORM
            98 or 99 => DdsFormat.Bc7,   // BC7_UNORM / SRGB
            28 or 29 => DdsFormat.Rgba8, // R8G8B8A8_UNORM / SRGB
            87 or 91 => DdsFormat.Bgra8, // B8G8R8A8_UNORM / SRGB
            88 or 93 => DdsFormat.Bgrx8, // B8G8R8X8_UNORM / SRGB
            _ => DdsFormat.Unknown
        };

        if (format == DdsFormat.Unknown)
        {
            error = $"不支持的 DXGI 格式：{dxgiFormat}";
            return false;
        }

        return true;
    }

    private static bool TryParseUncompressed(
        int rgbBits,
        uint rMask,
        uint gMask,
        uint bMask,
        uint aMask,
        out DdsFormat format,
        out string error)
    {
        format = DdsFormat.Unknown;
        error = string.Empty;

        if (rgbBits == 32)
        {
            bool alpha = (aMask & 0xFF000000) != 0;
            if (rMask == 0x00FF0000 && gMask == 0x0000FF00 && bMask == 0x000000FF)
            {
                format = alpha ? DdsFormat.Bgra8 : DdsFormat.Bgrx8;
                return true;
            }

            if (rMask == 0x000000FF && gMask == 0x0000FF00 && bMask == 0x00FF0000)
            {
                format = DdsFormat.Rgba8;
                return true;
            }

            error = $"不支持的 32 位 RGB 掩码：R=0x{rMask:X8} G=0x{gMask:X8} B=0x{bMask:X8}";
            return false;
        }

        if (rgbBits == 24)
        {
            format = DdsFormat.Bgr8;
            return true;
        }

        error = $"不支持的未压缩位深：{rgbBits}";
        return false;
    }
}
