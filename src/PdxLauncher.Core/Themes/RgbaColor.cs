namespace PdxLauncher.Core.Themes;

/// <summary>
/// 与 UI 框架无关的 RGBA 颜色值。<see cref="A"/> 为 0..1 的 alpha，
/// 由 App 层自行转换成 WinUI 的 Color/Brush。
/// </summary>
public readonly record struct RgbaColor(byte R, byte G, byte B, double A)
{
    /// <summary>不透明黑。</summary>
    public static RgbaColor Black { get; } = new(0, 0, 0, 1.0);

    /// <summary>不透明白。</summary>
    public static RgbaColor White { get; } = new(255, 255, 255, 1.0);

    /// <summary>转成 <c>#AARRGGBB</c> 形式的字符串，便于日志与断言。</summary>
    public override string ToString() =>
        string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"#{R:X2}{G:X2}{B:X2}{(byte)Math.Round(Math.Clamp(A, 0, 1) * 255):X2}");
}
