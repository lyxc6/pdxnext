namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 把「自有库里的 Mod 库行」与「磁盘扫描到的 mod」合并成只含本地 mod 的清单。
/// <para>
/// 创意工坊目录里的条目一律排除；本地 mod 的 <c>Source</c> 强制为 local，
/// 即使历史数据里带了 <c>steamId</c>（订阅 mod 被转成本地目录的情况）。
/// </para>
/// </summary>
public static class LocalModInventory
{
    /// <summary>合并；<paramref name="known"/> 的优先级高于扫描结果。</summary>
    public static IReadOnlyList<ModLibraryEntry> Merge(
        GameInstallation game,
        IReadOnlyList<ModLibraryEntry>? known,
        IReadOnlyList<PlaySetMod>? catalog,
        IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var rows = new Dictionary<Guid, ModLibraryEntry>();
        foreach (var entry in known ?? Array.Empty<ModLibraryEntry>())
        {
            if (IsLocal(game, entry.DirPath, entry.ArchivePath, fileSystem))
            {
                rows[entry.ModId] = entry with
                {
                    Source = ModOrigins.LocalSource,
                    SteamId = null,
                };
            }
        }

        foreach (var mod in catalog ?? Array.Empty<PlaySetMod>())
        {
            if (rows.ContainsKey(mod.ModId) || !IsLocal(game, mod.DirPath, mod.ArchivePath, fileSystem))
            {
                continue;
            }

            rows[mod.ModId] = new ModLibraryEntry(
                mod.ModId,
                mod.Name,
                mod.GameRegistryId,
                mod.DirPath,
                mod.ArchivePath,
                mod.ThumbnailPath,
                mod.RequiredVersion,
                mod.Status,
                ModOrigins.LocalSource,
                SizeBytes: null,
                SubscribedOn: null,
                PlaySetIds: Array.Empty<Guid>(),
                IsUnregistered: true,
                SteamId: null);
        }

        return rows.Values
            .OrderBy(static entry => entry.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    /// <summary>判断一条记录是否是本游戏数据目录下的本地 mod（非创意工坊内容）。</summary>
    public static bool IsLocal(GameInstallation game, string? dirPath, string? archivePath, IFileSystem fileSystem)
    {
        if (!string.IsNullOrWhiteSpace(dirPath))
        {
            var full = Normalize(fileSystem, dirPath!);
            if (IsWorkshopPath(full))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(game.ModDir)
                && IsUnder(full, Normalize(fileSystem, game.ModDir)))
            {
                return true;
            }
        }

        if (!string.IsNullOrWhiteSpace(archivePath))
        {
            var full = Normalize(fileSystem, archivePath!);
            return !IsWorkshopPath(full);
        }

        return false;
    }

    private static bool IsWorkshopPath(string path) =>
        path.Contains(Path.DirectorySeparatorChar + "steamapps" + Path.DirectorySeparatorChar + "workshop" + Path.DirectorySeparatorChar + "content" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || path.Contains(Path.DirectorySeparatorChar + "workshop" + Path.DirectorySeparatorChar + "content" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static bool IsUnder(string path, string root)
    {
        if (string.Equals(path, root, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(IFileSystem fileSystem, string path)
    {
        try
        {
            return fileSystem.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
