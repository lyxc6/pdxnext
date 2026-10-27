namespace PdxLauncher.Imaging.Dds;

/// <summary>DDS 像素数据格式。sRGB 变体归一到同一条解码路径。</summary>
public enum DdsFormat
{
    /// <summary>无法识别的格式。</summary>
    Unknown = 0,

    /// <summary>BC1（DXT1）：RGB + 1 位透明。</summary>
    Bc1,

    /// <summary>BC2（DXT3）：RGB + 4 位显式 alpha。</summary>
    Bc2,

    /// <summary>BC3（DXT5）：RGB + 插值 alpha。</summary>
    Bc3,

    /// <summary>BC4（ATI1）：单通道灰度。</summary>
    Bc4,

    /// <summary>BC5（ATI2）：双通道，常用于法线贴图。</summary>
    Bc5,

    /// <summary>BC7（DXGI_FORMAT_BC7_UNORM）：高质量 RGBA 压缩。</summary>
    Bc7,

    /// <summary>未压缩 32 位 RGBA（字节序 R,G,B,A）。</summary>
    Rgba8,

    /// <summary>未压缩 32 位 BGRA（字节序 B,G,R,A）。</summary>
    Bgra8,

    /// <summary>未压缩 32 位 BGRX（第 4 字节无意义，按不透明处理）。</summary>
    Bgrx8,

    /// <summary>未压缩 24 位 BGR。</summary>
    Bgr8
}
