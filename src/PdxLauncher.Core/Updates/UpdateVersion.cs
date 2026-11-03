namespace PdxLauncher.Core.Updates;

/// <summary>
/// 启动器版本号（major.minor.patch）。只支持数字段；忽略 <c>+commit</c> 与 <c>-preview</c> 后缀，
/// 因为发布清单只发布稳定版，比较时不需要语义化预发布排序。
/// </summary>
public readonly record struct UpdateVersion(int Major, int Minor, int Patch) : IComparable<UpdateVersion>
{
    public static bool TryParse(string? text, out UpdateVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        var plus = value.IndexOf('+', StringComparison.Ordinal);
        if (plus >= 0)
        {
            value = value[..plus];
        }

        var dash = value.IndexOf('-', StringComparison.Ordinal);
        if (dash >= 0)
        {
            value = value[..dash];
        }

        var parts = value.Split('.');
        if (parts.Length is < 1 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i]) ||
                numbers[i] < 0)
            {
                return false;
            }
        }

        version = new UpdateVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    public static UpdateVersion Parse(string text) =>
        TryParse(text, out var version) ? version : throw new FormatException("无效版本号: " + text);

    public int CompareTo(UpdateVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0)
        {
            return major;
        }

        var minor = Minor.CompareTo(other.Minor);
        return minor != 0 ? minor : Patch.CompareTo(other.Patch);
    }

    public override string ToString() => Major + "." + Minor + "." + Patch;

    public static bool operator >(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(UpdateVersion left, UpdateVersion right) => left.CompareTo(right) < 0;
}
