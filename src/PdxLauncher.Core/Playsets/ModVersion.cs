namespace PdxLauncher.Core.Playsets;

/// <summary>
/// mod 的 requiredVersion 与当前游戏版本之间的兼容判断。
/// <para>Paradox 的 mod 用 "1.13.*" 这类声明，游戏版本又混着代号（"1.13.11 (Matcha)"），
/// 因此只按「文本里第一段、第二段数字」比较 major.minor，不做完整的语义版本比较。</para>
/// </summary>
public static class ModVersion
{
    /// <summary>从版本文本里读出 major / minor；凑不出两段数字时返回 false。</summary>
    /// <param name="version">版本文本，例如 "1.13.*"、"1.13.11 (Matcha)"。</param>
    /// <param name="major">第一段数字。</param>
    /// <param name="minor">第二段数字。</param>
    public static bool TryReadMajorMinor(string? version, out int major, out int minor)
    {
        major = 0;
        minor = 0;
        if (string.IsNullOrWhiteSpace(version))
        {
            return false;
        }

        var numbers = new List<int>(3);
        var current = 0;
        var hasDigit = false;
        foreach (var ch in version)
        {
            if (char.IsDigit(ch))
            {
                current = (current * 10) + (ch - '0');
                hasDigit = true;
                continue;
            }

            if (!hasDigit)
            {
                continue;
            }

            numbers.Add(current);
            current = 0;
            hasDigit = false;
            if (numbers.Count == 2)
            {
                break;
            }
        }

        if (hasDigit && numbers.Count < 2)
        {
            numbers.Add(current);
        }

        if (numbers.Count < 2)
        {
            return false;
        }

        major = numbers[0];
        minor = numbers[1];
        return true;
    }

    /// <summary>
    /// mod 要求的版本是否低于当前游戏版本（只比 major.minor）。
    /// 任意一边解析不出两段数字时**不**判定过旧，避免把没有 requiredVersion 的 mod 全标成警告。
    /// </summary>
    /// <param name="requiredVersion">mods.requiredVersion（可能带 "*"）；空串表示未声明。</param>
    /// <param name="gameVersion">当前游戏版本，例如 "1.13.11 (Matcha)"。</param>
    public static bool IsOutdated(string? requiredVersion, string? gameVersion)
    {
        if (!TryReadMajorMinor(requiredVersion, out var requiredMajor, out var requiredMinor))
        {
            return false;
        }

        if (!TryReadMajorMinor(gameVersion, out var gameMajor, out var gameMinor))
        {
            return false;
        }

        if (requiredMajor != gameMajor)
        {
            return requiredMajor < gameMajor;
        }

        return requiredMinor < gameMinor;
    }
}
