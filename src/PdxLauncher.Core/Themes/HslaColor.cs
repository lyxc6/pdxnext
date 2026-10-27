namespace PdxLauncher.Core.Themes;

/// <summary>
/// CSS 风格颜色解析（纯函数，无 UI 类型）。支持
/// <c>hsla(H, S%, L%, A)</c> / <c>hsl(H, S%, L%)</c> / <c>#RRGGBB</c> / <c>#RGB</c> /
/// <c>rgba(r, g, b, a)</c> / <c>rgb(r, g, b)</c>。
/// Paradox 主题里的颜色正是这些形态（如 <c>hsla(53, 31%, 85%, 0.9)</c>、<c>#ffffff</c>）。
/// </summary>
public static class HslaColor
{
    /// <summary>解析颜色；失败返回 false 并把 <paramref name="color"/> 置为 default。绝不抛异常。</summary>
    public static bool TryParse(string? text, out RgbaColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (value[0] == '#')
        {
            return TryParseHex(value, out color);
        }

        var open = value.IndexOf('(', StringComparison.Ordinal);
        if (open <= 0 || !value.EndsWith(")", StringComparison.Ordinal))
        {
            return false;
        }

        var name = value.Substring(0, open).Trim().ToLowerInvariant();
        var body = value.Substring(open + 1, value.Length - open - 2);
        var parts = body.Split(',', StringSplitOptions.TrimEntries);
        if (parts.Length < 3)
        {
            return false;
        }

        return name switch
        {
            "hsla" => TryParseHsla(parts, out color),
            "hsl" => TryParseHsla(parts, out color),
            "rgba" => TryParseRgba(parts, out color),
            "rgb" => TryParseRgba(parts, out color),
            _ => false,
        };
    }

    /// <summary>解析颜色；失败抛 <see cref="FormatException"/>（仅在你确认输入合法时使用）。</summary>
    public static RgbaColor Parse(string text)
    {
        if (!TryParse(text, out var color))
        {
            throw new FormatException("无法解析颜色: " + text);
        }

        return color;
    }

    private static bool TryParseHex(string value, out RgbaColor color)
    {
        color = default;
        var hex = value.AsSpan(1);
        switch (hex.Length)
        {
            case 3:
            {
                if (!TryParseHexByte(hex[0], out var r) || !TryParseHexByte(hex[1], out var g) || !TryParseHexByte(hex[2], out var b))
                {
                    return false;
                }

                color = new RgbaColor((byte)(r * 17), (byte)(g * 17), (byte)(b * 17), 1.0);
                return true;
            }

            case 6:
            {
                if (!TryParseHexBytePair(hex.Slice(0, 2), out var r)
                    || !TryParseHexBytePair(hex.Slice(2, 2), out var g)
                    || !TryParseHexBytePair(hex.Slice(4, 2), out var b))
                {
                    return false;
                }

                color = new RgbaColor(r, g, b, 1.0);
                return true;
            }

            case 8:
            {
                if (!TryParseHexBytePair(hex.Slice(0, 2), out var r)
                    || !TryParseHexBytePair(hex.Slice(2, 2), out var g)
                    || !TryParseHexBytePair(hex.Slice(4, 2), out var b)
                    || !TryParseHexBytePair(hex.Slice(6, 2), out var a))
                {
                    return false;
                }

                color = new RgbaColor(r, g, b, a / 255.0);
                return true;
            }

            default:
                return false;
        }
    }

    private static bool TryParseHexByte(char ch, out byte value)
    {
        if (ch >= '0' && ch <= '9')
        {
            value = (byte)(ch - '0');
            return true;
        }

        if (ch >= 'a' && ch <= 'f')
        {
            value = (byte)(ch - 'a' + 10);
            return true;
        }

        if (ch >= 'A' && ch <= 'F')
        {
            value = (byte)(ch - 'A' + 10);
            return true;
        }

        value = 0;
        return false;
    }

    private static bool TryParseHexBytePair(ReadOnlySpan<char> pair, out byte value)
    {
        value = 0;
        if (pair.Length != 2
            || !TryParseHexByte(pair[0], out var high)
            || !TryParseHexByte(pair[1], out var low))
        {
            return false;
        }

        value = (byte)((high << 4) | low);
        return true;
    }

    private static bool TryParseHsla(string[] parts, out RgbaColor color)
    {
        color = default;
        if (!TryParseNumber(parts[0], out var hueDegrees))
        {
            return false;
        }

        if (!TryParsePercentOrNumber(parts[1], out var saturation))
        {
            return false;
        }

        if (!TryParsePercentOrNumber(parts[2], out var lightness))
        {
            return false;
        }

        var alpha = 1.0;
        if (parts.Length >= 4 && !TryParseAlpha(parts[3], out alpha))
        {
            return false;
        }

        var hue = hueDegrees % 360.0;
        if (hue < 0)
        {
            hue += 360.0;
        }

        var s = Math.Clamp(saturation, 0.0, 1.0);
        var l = Math.Clamp(lightness, 0.0, 1.0);
        var c = (1.0 - Math.Abs((2.0 * l) - 1.0)) * s;
        var hp = hue / 60.0;
        var x = c * (1.0 - Math.Abs((hp % 2.0) - 1.0));
        var m = l - (c / 2.0);

        double r;
        double g;
        double b;
        if (hp < 1.0)
        {
            r = c;
            g = x;
            b = 0.0;
        }
        else if (hp < 2.0)
        {
            r = x;
            g = c;
            b = 0.0;
        }
        else if (hp < 3.0)
        {
            r = 0.0;
            g = c;
            b = x;
        }
        else if (hp < 4.0)
        {
            r = 0.0;
            g = x;
            b = c;
        }
        else if (hp < 5.0)
        {
            r = x;
            g = 0.0;
            b = c;
        }
        else
        {
            r = c;
            g = 0.0;
            b = x;
        }

        color = new RgbaColor(ToByte(r + m), ToByte(g + m), ToByte(b + m), Math.Clamp(alpha, 0.0, 1.0));
        return true;
    }

    private static bool TryParseRgba(string[] parts, out RgbaColor color)
    {
        color = default;
        if (!TryParseNumber(parts[0], out var r)
            || !TryParseNumber(parts[1], out var g)
            || !TryParseNumber(parts[2], out var b))
        {
            return false;
        }

        var alpha = 1.0;
        if (parts.Length >= 4 && !TryParseAlpha(parts[3], out alpha))
        {
            return false;
        }

        color = new RgbaColor(
            ToByte(r / 255.0),
            ToByte(g / 255.0),
            ToByte(b / 255.0),
            Math.Clamp(alpha, 0.0, 1.0));
        return true;
    }

    private static bool TryParseAlpha(string text, out double alpha)
    {
        alpha = 1.0;
        var trimmed = text.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            if (!TryParseNumber(trimmed[..^1], out var percent))
            {
                return false;
            }

            alpha = percent / 100.0;
            return true;
        }

        return TryParseNumber(trimmed, out alpha);
    }

    private static bool TryParsePercentOrNumber(string text, out double value)
    {
        value = 0.0;
        var trimmed = text.Trim();
        if (trimmed.EndsWith("%", StringComparison.Ordinal))
        {
            if (!TryParseNumber(trimmed[..^1], out var percent))
            {
                return false;
            }

            value = percent / 100.0;
            return true;
        }

        if (!TryParseNumber(trimmed, out var raw))
        {
            return false;
        }

        // 没带 % 的 S/L 按 0..100 的百分比理解（与 CSS 的百分号语义一致）
        value = raw > 1.0 ? raw / 100.0 : raw;
        return true;
    }

    private static bool TryParseNumber(string text, out double value) =>
        double.TryParse(
            text.Trim(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);

    private static byte ToByte(double channel)
    {
        var scaled = Math.Clamp(channel, 0.0, 1.0) * 255.0;
        return (byte)Math.Round(scaled, MidpointRounding.AwayFromZero);
    }
}
