namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// Mod 清单区的数据映射（第 3 部分）：把 <see cref="ModLibrarySnapshot"/> 投影成
/// 名称 / 路径 / 来源 三列的行模型，来源文案用本地化键（由展示层翻译）。
/// 只依赖 System.* + Core 契约；不碰 UI 类型，可被自检工程原样链接。
/// <para>全程全本地：只读快照，不构造 URL、不发 HTTP。</para>
/// </summary>
public static class ModLibraryListBuilder
{
    /// <summary>清单的一行（与展示层行模型字段一致）。</summary>
    public sealed record Row(string Name, string Path, string SourceKey);

    /// <summary>来源列的 i18n 键：本地 mod / 订阅（Steam 或 Paradox 渠道）。</summary>
    public static string SourceKey(ModLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return ModOrigins.Normalize(entry.Source, entry.SteamId) == ModOrigin.Subscribed
            ? "ModEditorLibrarySourceSteam"
            : "ModEditorLibrarySourceLocal";
    }

    /// <summary>mod 的展示路径：目录型取 DirPath，压缩包型取 ArchivePath，都没有则空串。</summary>
    public static string DisplayPath(ModLibraryEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return entry.DirPath ?? entry.ArchivePath ?? string.Empty;
    }

    /// <summary>
    /// 把快照投影成清单行：名称按显示名，路径按 <see cref="DisplayPath"/>，来源按 <see cref="SourceKey"/>。
    /// 返回新列表，不修改快照。
    /// </summary>
    public static IReadOnlyList<Row> Build(ModLibrarySnapshot? snapshot)
    {
        if (snapshot?.Mods is null)
        {
            return Array.Empty<Row>();
        }

        var rows = new List<Row>(snapshot.Mods.Count);
        foreach (var entry in snapshot.Mods)
        {
            if (entry is null)
            {
                continue;
            }

            rows.Add(new Row(entry.Name, DisplayPath(entry), SourceKey(entry)));
        }

        return rows;
    }
}
