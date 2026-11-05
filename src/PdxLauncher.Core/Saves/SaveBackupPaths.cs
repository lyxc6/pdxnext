namespace PdxLauncher.Core.Saves;

using System.Globalization;

/// <summary>
/// 存档备份的路径与命名规则、配额常量与回收站落点。
/// <para>
/// 所有备份产物都落在 <c>&lt;data&gt;\save-backups\</c> 下；回收站也在其内，因此除「恢复写回目标目录」
/// 与「删除真实存档移出源」这两处用户显式动作外，本服务不会写到数据根之外。
/// </para>
/// <para>
/// 备份文件名规则：<c>&lt;yyyyMMdd-HHmmss&gt;__&lt;安全名&gt;.bak</c>，外加同名 <c>.bak.json</c>
/// sidecar 与写入过程中的 <c>.tmp</c>/<c>.restore.part</c> 临时文件。安全名替换
/// <c>&lt;&gt;:"/\|?*</c> 与控制字符为 <c>_</c>、去掉尾随点/空格、按 UTF-16 编码字节数（BMP 每字 2 字节、代理对 4 字节）截断到
/// <see cref="MaxSafeNameBytes"/> 并保留扩展名（注意这不是 UTF-8 字节数）；中文与 emoji 原样保留，不做转码。
/// </para>
/// </summary>
public static class SaveBackupPaths
{
    /// <summary>数据根下的备份目录名。</summary>
    public const string RootFolderName = "save-backups";

    /// <summary>备份目录下的回收站目录名（被替换的存档与「删除」的存档都进这里）。</summary>
    public const string TrashFolderName = "trash";

    /// <summary>备份正文扩展名（追加在原始扩展名之后）。</summary>
    public const string BackupExtension = ".bak";

    /// <summary>sidecar 扩展名（拼在 <c>.bak</c> 之后）。</summary>
    public const string SidecarExtension = ".json";

    /// <summary>写入备份正文与 sidecar 时使用的临时扩展名。</summary>
    public const string PartialExtension = ".tmp";

    /// <summary>恢复写回目标时使用的半截文件扩展名。</summary>
    public const string RestorePartialExtension = ".restore.part";

    /// <summary>备份总量配额，默认 2 GiB。</summary>
    public const long DefaultMaxTotalBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>单个游戏的备份条目数配额，默认 200。</summary>
    public const int DefaultMaxEntriesPerGame = 200;

    /// <summary>
    /// 备份文件名安全名部分的长度上限，按 UTF-16 编码字节数计（<c>Encoding.Unicode.GetByteCount</c>：
    /// BMP 字符 2 字节、代理对 4 字节），不是 UTF-8 字节数；截断时按码元边界整体取舍，绝不劈开代理对。
    /// </summary>
    public const int MaxSafeNameBytes = 120;

    /// <summary>恢复目标文件名的长度上限（同 <see cref="MaxSafeNameBytes"/>，按 UTF-16 编码字节数计；比备份名宽松，但仍要满足 NTFS 255 字符限制）。</summary>
    public const int MaxRestoreNameBytes = 240;

    /// <summary>gameId 作为目录名时的字节上限。</summary>
    public const int MaxGameFolderBytes = 64;

    private const string BackupStampFormat = "yyyyMMdd-HHmmss";
    private const string RestoreStampFormat = "yyyyMMdd-HHmm";
    private const string FallbackName = "save";
    private const string RestoreMarker = " (恢复 ";
    private const int ConflictSuffixMinIndex = 2;

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    /// <summary>备份根目录：<c>&lt;dataRoot&gt;\save-backups</c>。</summary>
    public static string Root(string dataRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        return Path.Combine(dataRoot, RootFolderName);
    }

    /// <summary>某个游戏的备份目录：<c>&lt;dataRoot&gt;\save-backups\&lt;gameId&gt;</c>。</summary>
    public static string GameDirectory(string dataRoot, string gameId) =>
        Path.Combine(Root(dataRoot), SafeGameFolderName(gameId));

    /// <summary>某个游戏的回收站目录：<c>&lt;dataRoot&gt;\save-backups\trash\&lt;gameId&gt;</c>。</summary>
    public static string TrashDirectory(string dataRoot, string gameId) =>
        Path.Combine(Root(dataRoot), TrashFolderName, SafeGameFolderName(gameId));

    /// <summary>备份时间戳，固定 <c>yyyyMMdd-HHmmss</c>，不受当前区域设置影响。</summary>
    public static string BackupStamp(DateTimeOffset localTime) =>
        localTime.ToString(BackupStampFormat, CultureInfo.InvariantCulture);

    /// <summary>恢复目标时间戳，固定 <c>yyyyMMdd-HHmm</c>。</summary>
    public static string RestoreStamp(DateTimeOffset localTime) =>
        localTime.ToString(RestoreStampFormat, CultureInfo.InvariantCulture);

    /// <summary>备份正文文件名：<c>&lt;时间戳&gt;__&lt;安全名&gt;.bak</c>。</summary>
    public static string BackupFileName(DateTimeOffset localTime, string originalFileName) =>
        BackupStamp(localTime) + "__" + SafeFileName(originalFileName) + BackupExtension;

    /// <summary>sidecar 路径：备份正文路径追加 <c>.json</c>。</summary>
    public static string SidecarPath(string backupPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(backupPath);
        return backupPath + SidecarExtension;
    }

    /// <summary>回收站内的文件名（不带 <c>.bak</c>，保留原始扩展名）。</summary>
    public static string TrashFileName(DateTimeOffset localTime, string originalFileName) =>
        BackupStamp(localTime) + "__" + SafeFileName(originalFileName);

    /// <summary>
    /// 默认（不覆盖）恢复的目标文件名：<c>&lt;原名&gt; (恢复 yyyyMMdd-HHmm).&lt;ext&gt;</c>，
    /// 截断到 <see cref="MaxRestoreNameBytes"/> 并保留扩展名。
    /// </summary>
    public static string RestoreFileName(string originalFileName, DateTimeOffset localTime)
    {
        var safe = ReplaceIllegalCharacters(originalFileName);
        return AppendBeforeExtension(safe, RestoreMarker + RestoreStamp(localTime) + ")", MaxRestoreNameBytes);
    }

    /// <summary>
    /// 在扩展名之前插入冲突后缀：<c>stem.ext</c> → <c>stem-2.ext</c>。索引从 2 起。
    /// 插入后整体仍不超过 <paramref name="maxBytes"/>。
    /// </summary>
    public static string WithConflictSuffix(string fileName, int index, int maxBytes = MaxSafeNameBytes)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(index, ConflictSuffixMinIndex);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxBytes);
        return AppendBeforeExtension(fileName, "-" + index.ToString(CultureInfo.InvariantCulture), maxBytes);
    }

    /// <summary>
    /// 把任意文件名转成安全的备份文件名主体：替换非法字符与控制字符、去掉尾随点/空格、
    /// 保留名加前缀、按 <see cref="MaxSafeNameBytes"/> 截断（保留扩展名）。
    /// 中文与 emoji 原样保留。
    /// </summary>
    public static string SafeFileName(string? originalFileName) =>
        Sanitize(originalFileName, MaxSafeNameBytes);

    /// <summary>把 gameId 转成安全的目录名，按 <see cref="MaxGameFolderBytes"/> 截断。</summary>
    public static string SafeGameFolderName(string? gameId) =>
        Sanitize(gameId, MaxGameFolderBytes);

    private static string Sanitize(string? value, int maxBytes)
    {
        var name = ReplaceIllegalCharacters(value);
        if (IsReservedName(name))
        {
            name = "_" + name;
        }

        name = TrimFileNameToBytes(name, maxBytes).TrimEnd('.', ' ');
        return name.Length == 0 ? FallbackName : name;
    }

    private static string ReplaceIllegalCharacters(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return FallbackName;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            if (char.IsControl(character) ||
                character is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*')
            {
                builder.Append('_');
                continue;
            }

            builder.Append(character);
        }

        var sanitized = builder.ToString().TrimEnd('.', ' ');
        return sanitized.Length == 0 ? FallbackName : sanitized;
    }

    private static bool IsReservedName(string name)
    {
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        var stem = dot < 0 ? name : name[..dot];
        return ReservedNames.Contains(stem);
    }

    private static string AppendBeforeExtension(string fileName, string insertion, int maxBytes)
    {
        var safe = ReplaceIllegalCharacters(fileName);
        var extension = Path.GetExtension(safe);
        var stem = safe[..(safe.Length - extension.Length)];
        var extensionBytes = UnicodeBytes(extension);
        var insertionBytes = UnicodeBytes(insertion);
        var budget = maxBytes - extensionBytes - insertionBytes;
        if (budget < 2)
        {
            return TrimFileNameToBytes(safe, maxBytes);
        }

        var trimmedStem = TrimToBytes(stem, budget);
        if (trimmedStem.Length == 0)
        {
            trimmedStem = TrimToBytes(FallbackName, budget);
        }

        return trimmedStem + insertion + extension;
    }

    /// <summary>截断文件名主体到 <paramref name="maxBytes"/>，保留扩展名。</summary>
    private static string TrimFileNameToBytes(string name, int maxBytes)
    {
        if (UnicodeBytes(name) <= maxBytes)
        {
            return name;
        }

        var extension = Path.GetExtension(name);
        var extensionBytes = UnicodeBytes(extension);
        if (extensionBytes >= maxBytes || extension.Length >= name.Length)
        {
            return TrimToBytes(name, maxBytes);
        }

        var stem = name[..(name.Length - extension.Length)];
        var trimmedStem = TrimToBytes(stem, maxBytes - extensionBytes);
        return trimmedStem.Length == 0 ? TrimToBytes(name, maxBytes) : trimmedStem + extension;
    }

    /// <summary>
    /// 按 UTF-16 码元宽度截断：BMP 字符 2 字节，合法代理对 4 字节且绝不劈开。
    /// </summary>
    private static string TrimToBytes(string text, int maxBytes)
    {
        if (UnicodeBytes(text) <= maxBytes)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var used = 0;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (char.IsHighSurrogate(character) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            {
                if (used + 4 > maxBytes)
                {
                    break;
                }

                builder.Append(character).Append(text[index + 1]);
                used += 4;
                index++;
                continue;
            }

            if (char.IsLowSurrogate(character))
            {
                continue;
            }

            if (used + 2 > maxBytes)
            {
                break;
            }

            builder.Append(character);
            used += 2;
        }

        return builder.ToString();
    }

    private static int UnicodeBytes(string text) => Encoding.Unicode.GetByteCount(text);
}
