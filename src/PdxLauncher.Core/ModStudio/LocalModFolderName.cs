namespace PdxLauncher.Core.ModStudio;

using System.Globalization;
using System.Text;

/// <summary>
/// 「工坊转本地」落地目录名的唯一规则：允许中文，禁止一定会让 Windows 或游戏加载器出错的形式。
/// <para>
/// 与 <see cref="ModCreator.IsValidFolderName"/> 分开，是因为「新建 mod」页沿用较保守的 ASCII 规则，
/// 而转换必须保留原 mod 的可读名字；两条规则的差异集中在这里，避免页面与转换器各自复制一份。
/// </para>
/// </summary>
public static class LocalModFolderName
{
    /// <summary>目录名最大长度；加上 <c>mod\</c> 前缀与 <c>.mod</c> 后缀仍在 Windows 常规路径长度内。</summary>
    public const int MaxLength = 64;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>首字符只能是字母或数字：<c>.hidden</c> 与 <c>-x</c> 都不该成为 mod 目录。</summary>
    public static bool IsAllowedFirst(char ch) => char.IsLetterOrDigit(ch);

    /// <summary>其余字符允许字母/数字、点、下划线、连字符与词间空格。</summary>
    public static bool IsAllowed(char ch) => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' or ' ';

    /// <summary>
    /// Windows 保留设备名（<c>CON</c>、<c>NUL</c>、<c>COM1</c>…），带任意扩展名也算保留（<c>con.txt</c>）。
    /// </summary>
    public static bool IsReserved(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var trimmed = name.Trim();
        var dot = trimmed.IndexOf('.');
        var stem = (dot >= 0 ? trimmed[..dot] : trimmed).TrimEnd(' ');
        return ReservedNames.Contains(stem);
    }

    /// <summary>
    /// 把任意展示名收敛成可落地的目录名：保留中文与词间单空格，其余分隔符折叠成 <c>_</c>；
    /// 原文没有可用字符时返回 <see cref="FallbackName"/>，由转换器再做撞名递增。
    /// </summary>
    public static string Normalize(string? displayName)
    {
        var raw = (displayName ?? string.Empty).Trim();
        var builder = new StringBuilder(raw.Length);
        foreach (var ch in raw)
        {
            if (IsAllowed(ch))
            {
                if (ch == ' ' && (builder.Length == 0 || builder[^1] == ' '))
                {
                    continue;
                }

                builder.Append(ch);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var name = builder.ToString().Trim(' ', '.', '_', '-');
        if (name.Length == 0)
        {
            return FallbackName;
        }

        if (!IsAllowedFirst(name[0]))
        {
            name = "m" + name;
        }

        if (name.Length > MaxLength)
        {
            name = name[..MaxLength].TrimEnd(' ', '.', '_', '-');
            if (name.Length == 0 || !IsAllowedFirst(name[0]))
            {
                return FallbackName;
            }
        }

        return IsReserved(name) ? "mod_" + name : name;
    }

    /// <summary>展示名里没有任何可用字符时的兜底名。</summary>
    public const string FallbackName = "localmod";

    /// <summary>
    /// 需要纯 ASCII 路径的游戏（Vic3）用这一版：先按中文规则收敛，再把非 ASCII 字符逐个换成
    /// <c>_u&lt;码点&gt;_</c>，并保证结果非空、不撞 Windows 保留名。
    /// <para>
    /// 用 <c>u</c> + 码点十六进制而不是直接丢弃：两个中文 mod 名丢掉汉字后常常都只剩空串，
    /// 又一起落到 <c>localmod</c>；带码点至少还能区分（如 <c>u82e6_u96be_u7684</c>）。
    /// </para>
    /// </summary>
    public static string NormalizeAscii(string? displayName)
    {
        var normalized = Normalize(displayName);
        var builder = new StringBuilder(normalized.Length + 8);
        foreach (var rune in normalized.EnumerateRunes())
        {
            if (rune.Value <= 0x7F)
            {
                builder.Append((char)rune.Value);
                continue;
            }

            // 用 Unicode 码点而不是「逐 char 丢弃」：不同中文名去汉字后常都只剩空串，
            // 又会一起落到 localmod；带码点至少能区分（如 苦难的女囚 → u82e6_u96be_u7684_u5973_u56ea）。
            builder.Append("_u").Append(rune.Value.ToString("x", CultureInfo.InvariantCulture)).Append('_');
        }

        // 连续汉字会得到 `_u82e6__u96be_`：双下划线不影响解析，保留即可，不做全局折叠
        // （折叠可能把标记与相邻的 `_` 粘成一个不完整的标记）。
        var name = builder.ToString().Trim(' ', '.', '_', '-');
        if (name.Length == 0)
        {
            return FallbackName;
        }

        if (!IsAllowedFirst(name[0]))
        {
            name = "m" + name;
        }

        if (name.Length > MaxLength)
        {
            // 截断可能把 `_u82e6_` 切成半个码点标记；从最后一个 `_u` 之前截，保住完整标记。
            var cut = name[..MaxLength];
            var lastMarker = cut.LastIndexOf("_u", StringComparison.Ordinal);
            name = (lastMarker > 0 ? cut[..lastMarker] : cut).TrimEnd(' ', '.', '_', '-');
        }

        return name.Length == 0 ? FallbackName : (IsReserved(name) ? "mod_" + name : name);
    }
}
