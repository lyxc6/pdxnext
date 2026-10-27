namespace PdxLauncher.Core.Online;

using System.Text.RegularExpressions;

/// <summary>
/// 把 WinINET 的 ProxyOverride（localhost;127.*;*.corp.example;&lt;local&gt;）翻译成
/// <see cref="System.Net.WebProxy.BypassList"/> 能接受的正则。
/// 必须自己翻译：WebProxy.BypassList 的每一项都按正则解释，直接塞 *.corp.example 会抛 ArgumentException。
/// </summary>
public static class ProxyBypassPatterns
{
    /// <summary>解析 ProxyOverride；非法项直接丢弃，绝不抛异常（注册表内容不可信）。</summary>
    /// <param name="proxyOverride">ProxyOverride 原值，可为 null。</param>
    /// <returns>是否绕过本地地址，以及逐项转换好的正则列表。</returns>
    public static (bool BypassOnLocal, IReadOnlyList<string> Regexes) Parse(string? proxyOverride)
    {
        if (string.IsNullOrWhiteSpace(proxyOverride))
        {
            return (false, Array.Empty<string>());
        }

        var bypassOnLocal = false;
        var regexes = new List<string>();

        foreach (var raw in proxyOverride.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(raw, "<local>", StringComparison.OrdinalIgnoreCase))
            {
                bypassOnLocal = true;
                continue;
            }

            var regex = ToRegex(raw);
            if (regex is not null)
            {
                regexes.Add(regex);
            }
        }

        return (bypassOnLocal, regexes);
    }

    /// <summary>单个通配项 → 整串匹配的正则；空项返回 null。</summary>
    private static string? ToRegex(string pattern)
    {
        var trimmed = pattern.Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        var builder = new StringBuilder("^");
        foreach (var ch in trimmed)
        {
            // WinINET 只用 * 表示通配；其余字符（含 . 与 :）一律按字面量转义。
            builder.Append(ch == '*' ? ".*" : Regex.Escape(ch.ToString()));
        }

        builder.Append('$');
        return builder.ToString();
    }
}
