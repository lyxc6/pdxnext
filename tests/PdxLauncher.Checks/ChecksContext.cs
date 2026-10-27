namespace PdxLauncher.Checks;

using System.Globalization;

/// <summary>共享断言工具与计数；各 *Checks 类通过它记录失败。</summary>
public static class ChecksContext
{
    public static int Failures { get; private set; }

    public static void Reset() => Failures = 0;

    public static bool Check(string label, bool condition, string? detail = null)
    {
        if (condition)
        {
            Console.WriteLine("  ok   " + label + (detail is null ? string.Empty : "  [" + detail + "]"));
            return true;
        }

        Failures++;
        Console.WriteLine("  FAIL " + label + (detail is null ? string.Empty : "  [" + detail + "]"));
        return false;
    }

    public static bool Equal<T>(string label, T expected, T actual)
    {
        var same = EqualityComparer<T>.Default.Equals(expected, actual);
        return Check(label, same, "期望 " + Format(expected) + "，实际 " + Format(actual));
    }

    public static bool EqualBytes(string label, byte[] expected, byte[] actual)
    {
        if (expected.Length == actual.Length && expected.AsSpan().SequenceEqual(actual))
        {
            return Check(label, true, "len " + expected.Length.ToString(CultureInfo.InvariantCulture));
        }

        return Check(
            label,
            false,
            "期望 " + expected.Length + "B/" + Md5(expected) + "，实际 " + actual.Length + "B/" + Md5(actual));
    }

    public static string Md5(byte[] data)
    {
        var hash = System.Security.Cryptography.MD5.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    public static string FixturePath(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "tests", "fixtures");
            if (Directory.Exists(candidate))
            {
                return Path.Combine(candidate, relative.Replace('/', Path.DirectorySeparatorChar));
            }

            dir = dir.Parent;
        }

        return Path.Combine("tests", "fixtures", relative);
    }

    private static string Format(object? value) => value switch
    {
        null => "null",
        string s => "\"" + s + "\"",
        System.Collections.IEnumerable list and not string =>
            "[" + string.Join(", ", list.Cast<object?>().Select(Format)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "?",
    };
}
