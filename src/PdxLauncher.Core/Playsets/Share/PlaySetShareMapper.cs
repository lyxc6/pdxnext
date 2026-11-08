namespace PdxLauncher.Core.Playsets.Share;

using PdxLauncher.Core.Contracts;

/// <summary>导出计划：一份可直接写盘的分享文档 + 被排除条目的计数。</summary>
/// <param name="Document">要写入的分享文档。</param>
/// <param name="LocalExcluded">因为来源是「本地」而按官方规则排除的条目数。</param>
/// <param name="UnsupportedMods">既没有 Steam id 也没有 PDX id、无法表达的条目。</param>
public sealed record PlaySetShareExportPlan(
    PlaySetShareDocument Document,
    int LocalExcluded,
    IReadOnlyList<PlaySetMod> UnsupportedMods);

/// <summary>导入时本地找不到（或没下载完）的条目。</summary>
/// <param name="DisplayName">分享文件里的显示名。</param>
/// <param name="SteamId">Steam 创意工坊 id（有则可用于一键订阅）。</param>
/// <param name="PdxId">PDX Mods id（需要登录 Paradox 账号才能订阅）。</param>
public sealed record PlaySetShareMissingMod(
    string DisplayName,
    string? SteamId,
    string? PdxId);

/// <summary>把分享文档对齐到本地 Mod 库的结果。</summary>
/// <param name="Resolved">本地已安装、可直接写进播放集的条目（已按 position 重排为 0..n-1）。</param>
/// <param name="Missing">本地没安装的条目。</param>
/// <param name="DuplicateIds">同一文件里重复出现的 mod id；非空时导入应直接拒绝（与官方行为一致）。</param>
public sealed record PlaySetShareResolution(
    IReadOnlyList<PlaySetMod> Resolved,
    IReadOnlyList<PlaySetShareMissingMod> Missing,
    IReadOnlyList<string> DuplicateIds);

/// <summary>
/// 官方分享 JSON 与本地 <see cref="PlaySetMod"/> / <see cref="ModLibraryEntry"/> 之间的映射。
/// <para>纯函数、不碰文件与数据库，便于自检用固定样本钉住口径。</para>
/// </summary>
public static class PlaySetShareMapper
{
    /// <summary>
    /// 生成导出文档。口径与官方 <c>fromPlayset</c> 一致：
    /// <list type="bullet">
    /// <item>本地 mod 一律排除（官方导入端本来就不认本地 mod）；</item>
    /// <item>PDX Mods 写 <c>pdxId</c>（锁定版本时再写 <c>version</c>），其余订阅 mod 写 <c>steamId</c>；</item>
    /// <item>认不出平台 id 的条目无法表达，单独计数交给界面提示。</item>
    /// </list>
    /// </summary>
    public static PlaySetShareExportPlan BuildExport(
        GameInstallation game,
        PlaySet playSet,
        IReadOnlyList<PlaySetMod> mods)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(playSet);
        ArgumentNullException.ThrowIfNull(mods);

        var shareMods = new List<PlaySetShareMod>(mods.Count);
        var unsupported = new List<PlaySetMod>();
        var localExcluded = 0;

        foreach (var mod in mods.OrderBy(static mod => mod.Position))
        {
            if (ModOrigins.Normalize(mod.Source, mod.SteamId) == ModOrigin.Local)
            {
                localExcluded++;
                continue;
            }

            var pdxId = PdxModIds.Normalize(mod.PdxId)
                ?? PdxModIds.FromRegistryId(mod.GameRegistryId);
            var steamId = PdxModIds.Normalize(mod.SteamId);

            if (pdxId is not null)
            {
                shareMods.Add(new PlaySetShareMod(
                    mod.Name,
                    mod.Enabled,
                    mod.Position,
                    PdxId: pdxId,
                    Version: PdxModIds.Normalize(mod.PdxVersion)));
                continue;
            }

            if (PdxModIds.LooksNumeric(steamId))
            {
                shareMods.Add(new PlaySetShareMod(mod.Name, mod.Enabled, mod.Position, SteamId: steamId));
                continue;
            }

            unsupported.Add(mod);
        }

        return new PlaySetShareExportPlan(
            new PlaySetShareDocument(game.GameId, playSet.Name, shareMods),
            localExcluded,
            unsupported);
    }

    /// <summary>
    /// 把分享文档的条目对到本地 Mod 库。匹配顺序：<c>steamId</c> → <c>pdxId</c> → 显示名（忽略大小写）。
    /// 只有「磁盘上有内容」的条目才算可导入；订阅了但还没下载完的按缺失处理（Steam 下载完再导入同一份文件即可补上）。
    /// </summary>
    public static PlaySetShareResolution Resolve(
        GameInstallation game,
        PlaySetShareDocument document,
        IReadOnlyList<ModLibraryEntry> library)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(library);

        var steamIndex = new Dictionary<string, ModLibraryEntry>(StringComparer.Ordinal);
        var pdxIndex = new Dictionary<string, ModLibraryEntry>(StringComparer.Ordinal);
        var nameIndex = new Dictionary<string, ModLibraryEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in library)
        {
            if (!HasContent(entry))
            {
                continue;
            }

            var steamId = PdxModIds.Normalize(entry.SteamId);
            if (steamId is not null && !steamIndex.ContainsKey(steamId))
            {
                steamIndex[steamId] = entry;
            }

            var pdxId = PdxModIds.Normalize(entry.PdxId)
                ?? PdxModIds.FromRegistryId(entry.GameRegistryId);
            if (pdxId is not null && !pdxIndex.ContainsKey(pdxId))
            {
                pdxIndex[pdxId] = entry;
            }

            var name = entry.Name?.Trim();
            if (!string.IsNullOrEmpty(name) && !nameIndex.ContainsKey(name))
            {
                nameIndex[name] = entry;
            }
        }

        var resolved = new List<PlaySetMod>(document.Mods.Count);
        var missing = new List<PlaySetShareMissingMod>();
        var duplicateIds = new List<string>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        var ordered = document.Mods.OrderBy(static mod => mod.Position).ToList();
        foreach (var shareMod in ordered)
        {
            var steamId = PdxModIds.Normalize(shareMod.SteamId);
            var pdxId = PdxModIds.Normalize(shareMod.PdxId);
            var key = steamId ?? ("pdx:" + pdxId);
            if (!seenIds.Add(key))
            {
                duplicateIds.Add(key);
                continue;
            }

            ModLibraryEntry? entry = null;
            if (steamId is not null && steamIndex.TryGetValue(steamId, out var bySteam))
            {
                entry = bySteam;
            }
            else if (pdxId is not null && pdxIndex.TryGetValue(pdxId, out var byPdx))
            {
                entry = byPdx;
            }
            else if (!string.IsNullOrWhiteSpace(shareMod.DisplayName)
                && nameIndex.TryGetValue(shareMod.DisplayName.Trim(), out var byName))
            {
                entry = byName;
            }

            if (entry is null)
            {
                missing.Add(new PlaySetShareMissingMod(shareMod.DisplayName, steamId, pdxId));
                continue;
            }

            resolved.Add(new PlaySetMod(
                entry.ModId,
                string.IsNullOrWhiteSpace(entry.Name) ? shareMod.DisplayName : entry.Name,
                entry.DirPath ?? string.Empty,
                entry.ArchivePath,
                entry.GameRegistryId,
                entry.ThumbnailPath,
                entry.RequiredVersion,
                entry.Status,
                shareMod.Enabled,
                resolved.Count,
                SteamId: entry.SteamId,
                Source: entry.Source,
                ModVersion: entry.ModVersion ?? string.Empty,
                PdxId: entry.PdxId));
        }

        return new PlaySetShareResolution(resolved, missing, duplicateIds);
    }

    /// <summary>文件里的游戏 id 是否与当前游戏一致（官方导入同样硬校验）。</summary>
    public static bool MatchesGame(GameInstallation game, PlaySetShareDocument document) =>
        string.Equals(document.Game, game.GameId, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 生成播放集名：重名时追加 <c> (2)</c>、<c> (3)</c>……（EU5 用名字作播放集身份，不能重名）。
    /// </summary>
    public static string EnsureUniqueName(string desired, IEnumerable<string> existingNames)
    {
        var baseName = (desired ?? string.Empty).Trim();
        if (baseName.Length == 0)
        {
            baseName = "Imported playset";
        }

        // 官方库 name 列是 varchar(255)，留出追加后缀的余量。
        if (baseName.Length > 200)
        {
            baseName = baseName[..200].TrimEnd();
        }

        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var existing in existingNames)
        {
            if (!string.IsNullOrWhiteSpace(existing))
            {
                taken.Add(existing.Trim());
            }
        }

        if (!taken.Contains(baseName))
        {
            return baseName;
        }

        for (var index = 2; index < 1000; index++)
        {
            var candidate = baseName + " (" + index + ")";
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }

        return baseName + " (" + DateTimeOffset.UtcNow.ToUnixTimeSeconds() + ")";
    }

    private static bool HasContent(ModLibraryEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.DirPath) || !string.IsNullOrWhiteSpace(entry.ArchivePath);
}
