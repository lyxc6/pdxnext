using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

namespace PdxLauncher.Core.LoadFiles;

/// <summary>
/// 「这一行 mod 会不会被加载文件丢掉，为什么」——纯函数，不做 IO（存在性由调用方注入谓词）。
/// </summary>
/// <remarks>
/// 加载文件的每种格式能表达的东西不一样，原本这些判断散在 <c>BuildLoadFileContent</c> 的
/// 各个 <c>if (string.IsNullOrWhiteSpace(...))</c> 里，丢弃发生时玩家完全不知情。
/// 现在统一从这里出结论：返回 null 表示会正常写进去，返回键表示会被跳过／大概率不生效。
/// 文案键由展示层翻译（Core 不能引用生成的 <c>Keys</c>）。
/// </remarks>
public static class ModLoadRules
{
    /// <summary>行里既没有目录路径也没有压缩包路径：任何格式都写不出这一行。</summary>
    public const string WarnNoPath = "ModsWarnNoPath";

    /// <summary>V0（<c>dlc_load.json</c>）只能写 gameRegistryId，空值无法表达。</summary>
    public const string WarnNoRegistryId = "ModsWarnNoRegistryId";

    /// <summary>V1（<c>content_load.json</c>）只能写 archivePath，目录型 mod 无法表达。</summary>
    public const string WarnNoArchive = "ModsWarnNoArchive";

    /// <summary>路径写进加载文件了，但磁盘上找不到（游戏会静默忽略）。</summary>
    public const string WarnMissingPath = "ModsWarnMissingPath";

    /// <summary>游戏只吃 ASCII 路径，而这个 mod 的目录名／文件名带非 ASCII 字符。</summary>
    public const string WarnNonAsciiPath = "ModsWarnNonAsciiPath";

    /// <summary>游戏只吃 ASCII 路径，mod 自身名字没问题，但上层目录（如用户名）带非 ASCII。</summary>
    public const string WarnNonAsciiParent = "ModsWarnNonAsciiParent";

    /// <summary>字符串里有没有非 ASCII 字符（> 0x7F 就算）。</summary>
    public static bool ContainsNonAscii(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        foreach (var ch in value)
        {
            if (ch > 0x7F)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 判断一行 mod 会不会被加载文件跳过。
    /// </summary>
    /// <param name="game">目标游戏（决定加载文件格式与路径限制）。</param>
    /// <param name="mod">播放集里的一行。</param>
    /// <param name="directoryExists">目录存在谓词；不传则不判 <see cref="WarnMissingPath"/>。</param>
    /// <param name="fileExists">文件存在谓词；不传则不判 <see cref="WarnMissingPath"/>。</param>
    /// <returns>提示文案键，或 null（不会被跳过）。</returns>
    public static string? DescribeSkipKey(
        GameInstallation game,
        PlaySetMod mod,
        Func<string, bool>? directoryExists = null,
        Func<string, bool>? fileExists = null)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mod);

        // 只有「启用」的行才会进加载文件；停用的行不写属于正常行为，不该报警告。
        if (!mod.Enabled)
        {
            return null;
        }

        // CS2 的启停与顺序全在 playset_config.json 里，本来就没有加载文件可写。
        if (game.Format == LoadFileFormat.PlaysetConfigV1)
        {
            return null;
        }

        var hasDir = !string.IsNullOrWhiteSpace(mod.DirPath);
        var hasArchive = !string.IsNullOrWhiteSpace(mod.ArchivePath);

        if (!hasDir && !hasArchive)
        {
            // 真机案例：官方库里某条订阅 mod 的 dirPath/archivePath 都是 NULL（订阅时没落盘），
            // 加载文件里直接少一行，游戏里等于没启用。
            return WarnNoPath;
        }

        switch (game.Format)
        {
            case LoadFileFormat.DlcLoadV0:
                if (string.IsNullOrWhiteSpace(mod.GameRegistryId))
                {
                    return WarnNoRegistryId;
                }

                break;

            case LoadFileFormat.ContentLoadV1:
                if (!hasArchive)
                {
                    return WarnNoArchive;
                }

                break;

            case LoadFileFormat.ContentLoadV1_1:
                // 目录型与压缩包型都能表达，不用额外判。
                break;
        }

        // 游戏实际读的路径：有压缩包用压缩包，否则用目录。
        var effectivePath = hasArchive ? mod.ArchivePath! : mod.DirPath!;

        var missing = hasArchive
            ? fileExists is not null && !fileExists(effectivePath)
            : directoryExists is not null && !directoryExists(effectivePath);
        if (missing)
        {
            return WarnMissingPath;
        }

        if (!GamePathRules.RequiresAsciiPaths(game.GameId))
        {
            return null;
        }

        var leaf = LeafOf(effectivePath);
        if (ContainsNonAscii(leaf))
        {
            return WarnNonAsciiPath;
        }

        // 名字本身是 ASCII，但上层目录带中文（典型：Windows 用户名是中文）——
        // 这种情况我们没法替用户改名，只能提示去改 gameDataPath。
        return ContainsNonAscii(ParentOf(effectivePath)) ? WarnNonAsciiParent : null;
    }

    /// <summary>路径的末段（目录名／文件名）。</summary>
    internal static string LeafOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index < 0 ? trimmed : trimmed[(index + 1)..];
    }

    /// <summary>路径去掉末段之后剩下的部分。</summary>
    internal static string ParentOf(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var index = trimmed.LastIndexOfAny(['\\', '/']);
        return index < 0 ? string.Empty : trimmed[..index];
    }
}
