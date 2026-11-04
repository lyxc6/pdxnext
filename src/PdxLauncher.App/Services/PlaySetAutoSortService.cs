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

        // 只排启用的 mod：禁用行不参与扫描/建图，也不被移动（加载文件本来就只写启用行）。
        var sortable = new List<PlaySetMod>(mods.Count);
        foreach (var mod in mods)
        {
            if (mod.Enabled)
            {
                sortable.Add(mod);
            }
        }

        var footprints = new List<ModFootprint>(sortable.Count);
        for (var index = 0; index < sortable.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var mod = sortable[index];
            progress?.Report(new AutoSortProgress(AutoSortPhase.ScanningMods, index, sortable.Count, mod.Name));
            footprints.Add(ModFootprintScanner.Scan(
                fileSystem,
                mod.ModId,
                mod.Name,
                mod.DirPath,
                mod.ArchivePath,
                vanillaPaths,
                cancellationToken));
        }

        progress?.Report(new AutoSortProgress(AutoSortPhase.ScanningMods, sortable.Count, sortable.Count));
        var scanElapsed = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var knownIds = new HashSet<Guid>();
        foreach (var mod in sortable)
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

        edges.AddRange(BuildDescriptorEdges(fileSystem, game, sortable, logger, knownIds));

        // V3 / EU5 没有 descriptor.mod，依赖只写在 .metadata/metadata.json 里。
        var metadataEdges = BuildMetadataDependencyEdges(fileSystem, sortable, logger, knownIds);
        edges.AddRange(metadataEdges);

        progress?.Report(new AutoSortProgress(AutoSortPhase.Graph, 0, sortable.Count));
        var graph = ConflictGraphBuilder.Build(footprints);
        edges.AddRange(graph.DerivedPatchEdges);
        edges = DeduplicateEdges(edges);
        var graphElapsed = stopwatch.ElapsedMilliseconds;
        stopwatch.Restart();

        var items = new List<AutoSortInput>(sortable.Count);
        for (var index = 0; index < sortable.Count; index++)
        {
            items.Add(new AutoSortInput(footprints[index], index));
        }

        var result = PlaySetAutoSorter.Sort(items, edges);
        var sortElapsed = stopwatch.ElapsedMilliseconds;

        result = result with { Warnings = MergeWarnings(result.Warnings, footprints, vanillaPaths) };

        // 相位耗时：用来判断 O(n²) 建图是否值得优化（382 mod 实测应在百毫秒量级）。
        logger.Info(
            $"自动排序耗时：原版索引={vanillaElapsed}ms 扫描={scanElapsed}ms 建图={graphElapsed}ms " +
            $"排序={sortElapsed}ms mod={mods.Count} 启用={sortable.Count} 边={edges.Count} " +
            $"元数据依赖={metadataEdges.Count} 冲突={graph.Conflicts.Count}");

        // 回填：启用行按排序结果依次占位，禁用行保持原位不动。
        var sequence = new List<PlaySetMod>(result.Entries.Count);
        foreach (var entry in result.Entries)
        {
            sequence.Add(sortable[entry.OldIndex]);
        }

        var reordered = new List<PlaySetMod>(mods.Count);
        var actualOfSortable = new int[sortable.Count];
        var actualOfSlot = new int[sortable.Count];
        var next = 0;
        for (var index = 0; index < mods.Count; index++)
        {
            if (!mods[index].Enabled)
            {
                reordered.Add(mods[index]);
                continue;
            }

            var slot = next++;
            actualOfSlot[slot] = index;
            reordered.Add(sequence[slot]);
        }

        foreach (var entry in result.Entries)
        {
            actualOfSortable[entry.OldIndex] = actualOfSlot[entry.NewIndex];
        }

        // 预览里的位次一律换成「播放集里的真实行号」：禁用行占着位置时，启用序号不是行号。
        var remapped = new List<AutoSortEntry>(result.Entries.Count);
        foreach (var entry in result.Entries)
        {
            remapped.Add(entry with
            {
                OldIndex = actualOfSortable[entry.OldIndex],
                NewIndex = actualOfSlot[entry.NewIndex],
            });
        }

        result = result with { Entries = remapped };

        return new AutoSortPreview(
            footprints,
            edges,
            graph.Conflicts,
            result,
            PlaySetEditor.Renumber(reordered));
    }

    /// <summary>把「原版索引缺失」「空足迹」并入排序器给出的警告。</summary>
    private static IReadOnlyList<AutoSortWarning> MergeWarnings(
        IReadOnlyList<AutoSortWarning> warnings,
        IReadOnlyList<ModFootprint> footprints,
        IReadOnlyCollection<string> vanillaPaths)
    {
        var merged = new List<AutoSortWarning>(warnings.Count + 2);
        if (vanillaPaths.Count == 0)
        {
            merged.Add(new AutoSortWarning(AutoSortWarningKind.VanillaIndexMissing, Array.Empty<Guid>()));
        }

        merged.AddRange(warnings);

        var empty = new List<Guid>();
        foreach (var footprint in footprints)
        {
            if (footprint.TotalFiles == 0)
            {
                empty.Add(footprint.ModId);
            }
        }

        if (empty.Count > 0)
        {
            merged.Add(new AutoSortWarning(AutoSortWarningKind.EmptyFootprint, empty));
        }

        return merged;
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

    /// <summary>
    /// V3 / EU5 的 <c>.metadata/metadata.json</c> 依赖：relationships 里 <c>rel_type=dependency</c> 的 id
    /// 指向被依赖 mod 的元数据 id（部分 mod 的元数据 id 就是 workshop id，故 SteamId 也参与匹配）。
    /// 被依赖者必须先加载（Before=被依赖方，After=声明方）。
    /// </summary>
    private static IReadOnlyList<SortEdge> BuildMetadataDependencyEdges(
        IFileSystem fileSystem,
        IReadOnlyList<PlaySetMod> mods,
        IAppLogger logger,
        IReadOnlySet<Guid> knownIds)
    {
        var descriptors = new Dictionary<Guid, ModMetadataDescriptor>(mods.Count);
        var byMetadataId = new Dictionary<string, PlaySetMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var mod in mods)
        {
            if (ModMetadataReader.TryRead(fileSystem, mod.DirPath, logger, out var descriptor))
            {
                descriptors[mod.ModId] = descriptor;
                if (descriptor.Id is not null)
                {
                    byMetadataId.TryAdd(descriptor.Id, mod);
                }
            }

            // 没有 metadata.json 的 mod 也可能被别的 mod 按 workshop id 依赖，SteamId 一律参与匹配。
            if (!string.IsNullOrWhiteSpace(mod.SteamId))
            {
                byMetadataId.TryAdd(mod.SteamId, mod);
            }
        }

        if (descriptors.Count == 0)
        {
            return Array.Empty<SortEdge>();
        }

        var edges = new List<SortEdge>();
        foreach (var mod in mods)
        {
            if (!descriptors.TryGetValue(mod.ModId, out var descriptor))
            {
                continue;
            }

            foreach (var dependencyId in descriptor.DependencyIds)
            {
                if (!byMetadataId.TryGetValue(dependencyId, out var dependency) || dependency.ModId == mod.ModId)
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
                    "metadata.json 依赖 id=" + dependencyId));
            }
        }

        return edges;
    }

    /// <summary>
    /// 同一对 mod 可能被游戏库、descriptor、metadata 重复声明：按 (Before, After, Kind) 去重，
    /// 免得预览里对同一对 mod 显示两条一模一样的理由。
    /// </summary>
    private static List<SortEdge> DeduplicateEdges(List<SortEdge> edges)
    {
        var unique = new List<SortEdge>(edges.Count);
        var seen = new HashSet<(Guid Before, Guid After, SortEdgeKind Kind)>();
        foreach (var edge in edges)
        {
            if (seen.Add((edge.Before, edge.After, edge.Kind)))
            {
                unique.Add(edge);
            }
        }

        return unique;
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
