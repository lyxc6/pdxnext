namespace PdxLauncher.App.Services;

using System.Diagnostics;
using System.Text;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 自动排序服务：扫描播放集内各 mod 的实际文件改动足迹（对照原版内容目录），
/// 收集硬边（launcher-v2.sqlite 的 mods_dependencies + descriptor 的 dependencies 块），
/// 交给冲突图与拓扑排序器算出新顺序，产出预览（足迹/边/冲突/结果/重排行）。
/// 只读磁盘与库，不写任何数据；应用与撤销由调用方走 PlaySetEditor.Renumber 与自动保存通道。
/// </summary>
public static class PlaySetAutoSortService
{
    /// <summary>后台线程入口：扫数百 mod 与读库可能耗时，UI 层经此方法在后台跑 BuildPreview。</summary>
    public static Task<AutoSortPreview> BuildPreviewAsync(
        IFileSystem fileSystem,
        GameInstallation game,
        IReadOnlyList<PlaySetMod> mods,
        IAppLogger logger,
        CancellationToken cancellationToken = default,
        IProgress<AutoSortProgress>? progress = null)
    {
        return Task.Run(
            () => BuildPreview(fileSystem, game, mods, logger, cancellationToken, progress),
            cancellationToken);
    }

    /// <summary>
    /// 同步入口：纯读路径，产出预览供差异展示，确认后由调用方应用。
    /// 原版内容根取 &lt;InstallRoot&gt;/game（CK3 实测口径），不存在时退回 InstallRoot。
    /// </summary>
    /// <param name="cancellationToken">取消令牌：每个 mod 之间、原版索引枚举内部按 4096 个文件检查。</param>
    /// <param name="progress">进度回调：原版索引 / 逐 mod 扫描 / 建图三个相位。</param>
    public static AutoSortPreview BuildPreview(
        IFileSystem fileSystem,
        GameInstallation game,
        IReadOnlyList<PlaySetMod> mods,
        IAppLogger logger,
        CancellationToken cancellationToken = default,
        IProgress<AutoSortProgress>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(logger);

        var stopwatch = Stopwatch.StartNew();
        var vanillaPaths = ModFootprintScanner.BuildVanillaIndex(
            fileSystem,
            ResolveContentRoot(fileSystem, game),
            progress,
            cancellationToken);
        var vanillaElapsed = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var footprints = new List<ModFootprint>(mods.Count);
        for (var index = 0; index < mods.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mod = mods[index];
            progress?.Report(new AutoSortProgress(AutoSortPhase.ScanningMods, index, mods.Count, mod.Name));
            footprints.Add(ModFootprintScanner.Scan(
                fileSystem,
                mod.ModId,
                mod.Name,
                mod.DirPath,
                mod.ArchivePath,
                vanillaPaths,
                cancellationToken));
        }

        progress?.Report(new AutoSortProgress(AutoSortPhase.ScanningMods, mods.Count, mods.Count));
        var scanElapsed = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var knownIds = new HashSet<Guid>();
        foreach (var mod in mods)
        {
            knownIds.Add(mod.ModId);
        }

        var edges = new List<SortEdge>();
        foreach (var edge in PlaySetRepository.ReadModDependencyEdges(game, fileSystem, logger))
        {
            if (edge.Before != edge.After && knownIds.Contains(edge.Before) && knownIds.Contains(edge.After))
            {
                edges.Add(edge);
            }
        }

        edges.AddRange(BuildDescriptorEdges(fileSystem, game, mods, logger, knownIds));

        progress?.Report(new AutoSortProgress(AutoSortPhase.Graph, 0, mods.Count));
        var graph = ConflictGraphBuilder.Build(footprints);
        edges.AddRange(graph.DerivedPatchEdges);
        var graphElapsed = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var items = new List<AutoSortInput>(mods.Count);
        for (var index = 0; index < mods.Count; index++)
        {
            items.Add(new AutoSortInput(footprints[index], index));
        }

        var result = PlaySetAutoSorter.Sort(items, edges);
        var sortElapsed = stopwatch.ElapsedMilliseconds;

        // 相位耗时：用来判断 O(n²) 建图是否值得优化（382 mod 实测应在百毫秒量级）。
        logger.Info(
            $"自动排序耗时：原版索引={vanillaElapsed}ms 扫描={scanElapsed}ms 建图={graphElapsed}ms " +
            $"排序={sortElapsed}ms mod={mods.Count} 边={edges.Count} 冲突={graph.Conflicts.Count}");

        var reordered = new List<PlaySetMod>(mods.Count);
        foreach (var entry in result.Entries)
        {
            reordered.Add(mods[entry.OldIndex]);
        }

        return new AutoSortPreview(
            footprints,
            edges,
            graph.Conflicts,
            result,
            PlaySetEditor.Renumber(reordered));
    }

    /// <summary>
    /// 生成预览的「输入签名」：只看 mod 身份、位置与启用状态。
    /// 改名不改变排序依据，故不计入；预览到确认之间位置/启用变了签名即不相等。
    /// </summary>
    public static string ComputeInputSignature(IReadOnlyList<PlaySetMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);

        var builder = new StringBuilder(mods.Count * 40);
        foreach (var mod in mods)
        {
            builder.Append(mod.ModId.ToString("N"))
                .Append('|').Append(mod.Position)
                .Append('|').Append(mod.Enabled ? '1' : '0')
                .Append('\n');
        }

        return builder.ToString();
    }

    /// <summary>
    /// descriptor 的 dependencies 块（workshop_item_id）按 SteamId 匹配成硬边：
    /// 被声明依赖的 mod 必须先于声明方加载（Before=依赖方，After=声明方）。
    /// </summary>
    private static IReadOnlyList<SortEdge> BuildDescriptorEdges(
        IFileSystem fileSystem,
        GameInstallation game,
        IReadOnlyList<PlaySetMod> mods,
        IAppLogger logger,
        IReadOnlySet<Guid> knownIds)
    {
        var bySteamId = new Dictionary<string, PlaySetMod>(StringComparer.Ordinal);
        foreach (var mod in mods)
        {
            if (!string.IsNullOrWhiteSpace(mod.SteamId) && !bySteamId.ContainsKey(mod.SteamId))
            {
                bySteamId[mod.SteamId] = mod;
            }
        }

        if (bySteamId.Count == 0)
        {
            return Array.Empty<SortEdge>();
        }

        var edges = new List<SortEdge>();
        foreach (var mod in mods)
        {
            var descriptorText = ReadDescriptorText(fileSystem, game, mod, logger);
            if (string.IsNullOrEmpty(descriptorText))
            {
                continue;
            }

            foreach (var steamId in DescriptorDependencyParser.ParseSteamIds(descriptorText))
            {
                if (!bySteamId.TryGetValue(steamId, out var dependency) || dependency.ModId == mod.ModId)
                {
                    continue;
                }

                if (!knownIds.Contains(dependency.ModId))
                {
                    continue;
                }

                edges.Add(new SortEdge(
                    dependency.ModId,
                    mod.ModId,
                    SortEdgeKind.DeclaredDependency,
                    "descriptor 依赖 workshop_item_id=" + steamId));
            }
        }

        return edges;
    }

    /// <summary>读 descriptor 原文：优先 mod 目录里的 descriptor.mod，退回 mod 目录同名 .mod（官方旧式放法）。读不到按无依赖处理。</summary>
    private static string? ReadDescriptorText(IFileSystem fileSystem, GameInstallation game, PlaySetMod mod, IAppLogger logger)
    {
        var candidates = new List<string>(2);
        if (!string.IsNullOrEmpty(mod.DirPath))
        {
            candidates.Add(fileSystem.Combine(mod.DirPath, "descriptor.mod"));
            var folderName = fileSystem.GetFileName(mod.DirPath.TrimEnd('\\', '/'));
            if (!string.IsNullOrEmpty(folderName))
            {
                candidates.Add(fileSystem.Combine(game.ModDir, folderName + ".mod"));
            }
        }

        foreach (var candidate in candidates)
        {
            try
            {
                if (fileSystem.FileExists(candidate))
                {
                    return fileSystem.ReadAllText(candidate);
                }
            }
            catch (IOException exception)
            {
                logger.Warn("读取 descriptor 失败（" + candidate + "）：" + exception.Message);
            }
            catch (UnauthorizedAccessException exception)
            {
                logger.Warn("读取 descriptor 无权限（" + candidate + "）：" + exception.Message);
            }
        }

        return null;
    }

    /// <summary>原版内容根：&lt;InstallRoot&gt;/game 存在则用之，否则退回 InstallRoot；都不存在返回空索引（全部按新增处理）。</summary>
    private static string ResolveContentRoot(IFileSystem fileSystem, GameInstallation game)
    {
        var gameSubDir = fileSystem.Combine(game.InstallRoot, "game");
        if (fileSystem.DirectoryExists(gameSubDir))
        {
            return gameSubDir;
        }

        return game.InstallRoot;
    }
}
