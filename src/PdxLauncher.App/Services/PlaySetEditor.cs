namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Dlc;

/// <summary>
/// 播放集编辑的业务逻辑：排序、启停、与目录合并、加载文件内容映射、保存并校验。
/// 纯 C#，不引用任何 XAML/UI 类型，可脱离 UI 自检。
/// </summary>
public sealed class PlaySetEditor
{
    private readonly IPlaySetRepository _playSets;
    private readonly ILoadFileWriter _loadFiles;
    private readonly IAppLogger _logger;

    public PlaySetEditor(IPlaySetRepository playSets, ILoadFileWriter loadFiles, IAppLogger logger)
    {
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(loadFiles);
        ArgumentNullException.ThrowIfNull(logger);
        _playSets = playSets;
        _loadFiles = loadFiles;
        _logger = logger;
    }

    // ------------------------------------------------------------ 纯逻辑（无 IO）

    /// <summary>按给定顺序重排 position 字段为 0..n-1。</summary>
    public static IReadOnlyList<PlaySetMod> Renumber(IReadOnlyList<PlaySetMod> mods)
    {
        ArgumentNullException.ThrowIfNull(mods);
        var result = new List<PlaySetMod>(mods.Count);
        foreach (var mod in mods)
        {
            result.Add(mod with { Position = result.Count });
        }

        return result;
    }

    /// <summary>把 index 处的 mod 上移（offset=-1）或下移（offset=+1），并重排 position。</summary>
    public static IReadOnlyList<PlaySetMod> Move(IReadOnlyList<PlaySetMod> mods, int index, int offset)
    {
        ArgumentNullException.ThrowIfNull(mods);
        if (index < 0 || index >= mods.Count)
        {
            return Renumber(mods);
        }

        var target = index + offset;
        if (target < 0 || target >= mods.Count)
        {
            return Renumber(mods);
        }

        var ordered = mods.OrderBy(static m => m.Position).ToList();
        var item = ordered[index];
        ordered.RemoveAt(index);
        ordered.Insert(target, item);
        return Renumber(ordered);
    }

    /// <summary>把 index 处的 mod 设为启用/禁用。</summary>
    public static IReadOnlyList<PlaySetMod> SetEnabledAt(IReadOnlyList<PlaySetMod> mods, int index, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mods);
        if (index < 0 || index >= mods.Count)
        {
            return mods;
        }

        var ordered = mods.OrderBy(static m => m.Position).ToList();
        ordered[index] = ordered[index] with { Enabled = enabled };
        return Renumber(ordered);
    }

    /// <summary>全部启用或全部禁用。</summary>
    public static IReadOnlyList<PlaySetMod> SetAllEnabled(IReadOnlyList<PlaySetMod> mods, bool enabled)
    {
        ArgumentNullException.ThrowIfNull(mods);
        return Renumber(mods.Select(m => m with { Enabled = enabled }).ToList());
    }

    /// <summary>
    /// 把 sqlite 里保存的播放集行与磁盘上真实存在的 mod 目录合并：
    /// 保留已保存的顺序与启用状态，丢掉磁盘上已不存在的 mod，把新发现的 mod 追加到末尾（默认禁用）。
    /// 匹配键优先 gameRegistryId，其次 dirPath，最后 modId。
    /// </summary>
    public static IReadOnlyList<PlaySetMod> MergeWithCatalog(
        IReadOnlyList<PlaySetMod> saved,
        IReadOnlyList<PlaySetMod> catalog)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(catalog);

        var byKey = new Dictionary<string, PlaySetMod>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in catalog)
        {
            var key = ModKey(item);
            if (!byKey.ContainsKey(key))
            {
                byKey[key] = item;
            }
        }

        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<PlaySetMod>(catalog.Count);
        foreach (var item in saved.OrderBy(static m => m.Position))
        {
            var key = ModKey(item);
            if (!byKey.TryGetValue(key, out var match))
            {
                continue;
            }

            if (!used.Add(key))
            {
                continue;
            }

            result.Add(match with { Enabled = item.Enabled, Position = result.Count });
        }

        foreach (var item in catalog)
        {
            var key = ModKey(item);
            if (!used.Add(key))
            {
                continue;
            }

            result.Add(item with { Enabled = false, Position = result.Count });
        }

        return result;
    }

    /// <summary>
    /// 把 playsets_dlcs 的启用状态合并到磁盘上真实存在的 DLC 目录：
    /// 没有记录的 DLC 视为启用（官方行为：无激活 playset ⇒ 全部 DLC 启用）。
    /// <para>
    /// 存储约定（不是 bug，别改）：pops_id 为空的 DLC 用 <see cref="DlcInfo.FilePathRelativeToDlcPath"/>
    /// （相对 dlcPath 的 POSIX 路径）作为 <see cref="PlaySetDlc.DlcId"/> 存进 sqlite。
    /// 若存空串，真机 Stellaris 那 4 个空 pops_id 的 DLC 在 playsets_dlcs 里会互相覆盖成一行；
    /// 用有效 id 才能保证启用状态正确往返（本方法与 DlcRowViewModel.ToPlaySetDlc() 用同一套 key）。
    /// 对 pops_id 非空的 DLC，有效 id 就等于 pops_id，与官方 launcher-v2.sqlite 完全一致。
    /// </para>
    /// </summary>
    public static IReadOnlyList<PlaySetDlc> MergeDlcs(
        IReadOnlyList<PlaySetDlc> saved,
        IReadOnlyList<DlcInfo> catalog)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(catalog);

        var savedStates = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var dlc in saved)
        {
            savedStates[dlc.DlcId] = dlc.Enabled;
        }

        var result = new List<PlaySetDlc>(catalog.Count);
        foreach (var info in catalog)
        {
            // pops_id 允许为空（真机 Stellaris 有 4 个 .dlc 是 "pops_id = \"\""），
            // 空时用相对 dlcPath 的 POSIX 路径当有效 id，否则这 4 个会撞成同一个 key。
            // DlcCatalog.GetEffectiveDlcId 保证非空且唯一（路径相对 dlcPath，天然唯一）。
            var effectiveId = DlcCatalog.GetEffectiveDlcId(info);
            var enabled = !savedStates.TryGetValue(effectiveId, out var state) || state;
            result.Add(new PlaySetDlc(effectiveId, enabled));
        }

        return result;
    }

    /// <summary>按搜索词过滤（名称 / registryId / 目录），空搜索返回全部。</summary>
    public static IReadOnlyList<PlaySetMod> Filter(IReadOnlyList<PlaySetMod> mods, string? query)
    {
        ArgumentNullException.ThrowIfNull(mods);
        if (string.IsNullOrWhiteSpace(query))
        {
            return mods;
        }

        var trimmed = query.Trim();
        return mods.Where(m =>
                m.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                m.GameRegistryId.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
                m.DirPath.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>mod 的稳定匹配键（大小写不敏感）。</summary>
    public static string ModKey(PlaySetMod mod)
    {
        ArgumentNullException.ThrowIfNull(mod);
        if (!string.IsNullOrWhiteSpace(mod.GameRegistryId))
        {
            return "reg:" + mod.GameRegistryId.Trim();
        }

        if (!string.IsNullOrWhiteSpace(mod.DirPath))
        {
            return "dir:" + mod.DirPath.Trim().TrimEnd('\\', '/');
        }

        return "id:" + mod.ModId.ToString("N");
    }

    /// <summary>
    /// 根据 formatVersion 把播放集状态映射成加载文件内容。
    /// V0：disabled_dlcs = 相对 dlcPath 的 POSIX .dlc 路径，enabled_mods = gameRegistryId；
    /// V1：disabledDLC = pops_id（pops_id 为空的 DLC 跳过，无法表达），enabledMods = archivePath（无 archive 的丢弃）；
    /// V1_1：disabledDLC 同上，enabledMods = 先 dirPath 组再 archivePath 组。
    /// enabledUGC 恒为空：sqlite 不区分 UGC 与普通 mod，官方样本里它也是 []。
    /// <para>
    /// 输入 dlcs 的 <see cref="PlaySetDlc.DlcId"/> 是"有效 id"（见 <see cref="MergeDlcs"/>）：
    /// pops_id 为空的 DLC 存的是 <see cref="DlcInfo.FilePathRelativeToDlcPath"/>，不是空串。这是约定，不是 bug。
    /// 匹配时同样用 <see cref="DlcCatalog.GetEffectiveDlcId"/>，两侧 key 才能对齐。
    /// </para>
    /// <para>
    /// 输出按格式分流：V0 用 .dlc 相对路径（pops_id 为空也能禁用）；
    /// V1/V1_1 只能写 pops_id，空 pops_id 的 DLC 会被跳过（官方 mapPopsId 同样丢弃，不伪造 id）。
    /// </para>
    /// </summary>
    public static LoadFileContent BuildLoadFileContent(
        GameInstallation game,
        IReadOnlyList<PlaySetMod> mods,
        IReadOnlyList<PlaySetDlc> dlcs,
        IReadOnlyList<DlcInfo> dlcCatalog)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(dlcs);
        ArgumentNullException.ThrowIfNull(dlcCatalog);

        var ordered = Renumber(mods);
        var enabled = ordered.Where(static m => m.Enabled).ToList();

        // 注意：这里的 DlcId 是"有效 id"——pops_id 为空的 DLC 存的是 FilePathRelativeToDlcPath（约定，不是 bug），
        // 所以下面与 DlcCatalog.GetEffectiveDlcId(dlc) 对齐，不能直接用 dlc.PopsId 比。
        var disabledDlcIds = dlcs.Where(static d => !d.Enabled)
            .Select(static d => d.DlcId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var disabledFilePaths = new List<string>();
        var disabledPopsIds = new List<string>();

        // V0 的 disabled_dlcs 用"相对 dlcPath 的 POSIX .dlc 路径"，pops_id 为空也能禁用；
        // V1/V1_1 的 disabledDLC 只能写 pops_id，空 pops_id 无法表达（官方 mapPopsId 会丢弃）。
        var contentLoadFormat = game.Format is LoadFileFormat.ContentLoadV1 or LoadFileFormat.ContentLoadV1_1;
        foreach (var dlc in dlcCatalog)
        {
            var effectiveId = DlcCatalog.GetEffectiveDlcId(dlc);
            if (!disabledDlcIds.Contains(effectiveId))
            {
                continue;
            }

            if (contentLoadFormat)
            {
                if (DlcCatalog.CanBeDisabledInContentLoad(dlc))
                {
                    disabledPopsIds.Add(dlc.PopsId);
                }
            }
            else if (!string.IsNullOrWhiteSpace(dlc.FilePathRelativeToDlcPath))
            {
                disabledFilePaths.Add(dlc.FilePathRelativeToDlcPath);
            }
        }

        var registryIds = new List<string>();
        var dirPaths = new List<string>();
        var archivePaths = new List<string>();

        switch (game.Format)
        {
            case LoadFileFormat.DlcLoadV0:
                foreach (var mod in enabled)
                {
                    if (!string.IsNullOrWhiteSpace(mod.GameRegistryId))
                    {
                        registryIds.Add(mod.GameRegistryId);
                    }
                }

                break;

            case LoadFileFormat.ContentLoadV1:
                foreach (var mod in enabled)
                {
                    var archive = mod.ArchivePath;
                    if (!string.IsNullOrWhiteSpace(archive))
                    {
                        archivePaths.Add(archive);
                    }
                }

                break;

            case LoadFileFormat.ContentLoadV1_1:
                foreach (var mod in enabled)
                {
                    var archive = mod.ArchivePath;
                    if (string.IsNullOrWhiteSpace(archive))
                    {
                        if (!string.IsNullOrWhiteSpace(mod.DirPath))
                        {
                            dirPaths.Add(mod.DirPath);
                        }
                    }
                    else
                    {
                        archivePaths.Add(archive);
                    }
                }

                break;

            default:
                break;
        }

        return new LoadFileContent(
            disabledFilePaths,
            disabledPopsIds,
            registryIds,
            dirPaths,
            archivePaths,
            Array.Empty<string>());
    }

    /// <summary>比较"应当写入的内容"与"回读到的内容"，返回差异描述（空表示一致）。</summary>
    public static IReadOnlyList<string> CompareContent(
        GameInstallation game,
        LoadFileContent expected,
        LoadFileContent actual)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        var diffs = new List<string>();
        switch (game.Format)
        {
            case LoadFileFormat.DlcLoadV0:
                AddSequenceDiff(diffs, "disabled_dlcs", expected.DisabledDlcFilePaths, actual.DisabledDlcFilePaths);
                AddSequenceDiff(diffs, "enabled_mods", expected.EnabledModRegistryIds, actual.EnabledModRegistryIds);
                break;

            case LoadFileFormat.ContentLoadV1:
                AddSequenceDiff(diffs, "disabledDLC", expected.DisabledDlcPopsIds, actual.DisabledDlcPopsIds);
                AddSequenceDiff(diffs, "enabledMods(archive)", expected.EnabledModArchivePaths, actual.EnabledModArchivePaths);
                break;

            case LoadFileFormat.ContentLoadV1_1:
                AddSequenceDiff(diffs, "disabledDLC", expected.DisabledDlcPopsIds, actual.DisabledDlcPopsIds);
                AddSequenceDiff(diffs, "enabledMods(dir)", expected.EnabledModDirPaths, actual.EnabledModDirPaths);
                AddSequenceDiff(diffs, "enabledMods(archive)", expected.EnabledModArchivePaths, actual.EnabledModArchivePaths);
                break;

            default:
                diffs.Add("unknown-format:" + game.Format);
                break;
        }

        return diffs;
    }

    private static void AddSequenceDiff(
        List<string> diffs,
        string name,
        IReadOnlyList<string> expected,
        IReadOnlyList<string> actual)
    {
        if (expected.Count != actual.Count)
        {
            diffs.Add("count:" + name + ":" + expected.Count + "!=" + actual.Count);
            return;
        }

        for (var i = 0; i < expected.Count; i++)
        {
            if (!string.Equals(expected[i], actual[i], StringComparison.Ordinal))
            {
                diffs.Add(name + "[" + i + "] \"" + expected[i] + "\" != \"" + actual[i] + "\"");
                return;
            }
        }
    }

    // ------------------------------------------------------------ 保存（IO）

    /// <summary>
    /// 保存播放集：先写 sqlite（mods + dlcs）；<paramref name="writeLoadFile"/> 为 true 时
    /// 再原子写加载文件并回读逐项比对，为 false 时不动游戏加载文件（非激活播放集）。
    /// 任一环节失败返回 AppResult.Fail，绝不抛给 UI。
    /// </summary>
    public async Task<AppResult<LoadFileContent?>> SaveAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> mods,
        IReadOnlyList<PlaySetDlc> dlcs,
        IReadOnlyList<DlcInfo> dlcCatalog,
        CancellationToken cancellationToken,
        bool writeLoadFile = true)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentNullException.ThrowIfNull(mods);
        ArgumentNullException.ThrowIfNull(dlcs);
        ArgumentNullException.ThrowIfNull(dlcCatalog);

        // EU5 没有加载文件（播放集/DLC 全在 playsets.json 里），所以即使调用方要求写，也要跳过。
        // 这里统一收口，避免每个调用点各判一次还漏判。
        var writesLoadFile = writeLoadFile && game.WritesLoadFile;
        var expected = writesLoadFile ? BuildLoadFileContent(game, mods, dlcs, dlcCatalog) : null;

        // 期望写进 playsets_mods 的行就是这里重排后的顺序（position = 0..n-1），
        // 回读校验必须用同一份列表，否则会把「重排」误判成差异。
        var orderedMods = Renumber(mods);

        try
        {
            var modsResult = await _playSets.SavePlaySetModsAsync(game, playSetId, orderedMods, cancellationToken)
                .ConfigureAwait(false);
            if (!modsResult.IsSuccess)
            {
                _logger.Error("保存播放集失败（gameId=" + game.GameId + "）：写 mods 失败 —— " + modsResult.ErrorMessage);
                return AppResult<LoadFileContent?>.Fail(
                    modsResult.Kind,
                    "step=mods;" + modsResult.ErrorMessage);
            }

            var dlcsResult = await _playSets.SavePlaySetDlcsAsync(game, playSetId, dlcs, cancellationToken)
                .ConfigureAwait(false);
            if (!dlcsResult.IsSuccess)
            {
                _logger.Error("保存播放集失败（gameId=" + game.GameId + "）：写 dlcs 失败 —— " + dlcsResult.ErrorMessage);
                return AppResult<LoadFileContent?>.Fail(
                    dlcsResult.Kind,
                    "step=dlcs;" + dlcsResult.ErrorMessage);
            }

            if (expected is not null)
            {
                await _loadFiles.WriteAsync(game, expected, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("保存播放集失败（gameId=" + game.GameId + "）", ex);
            return AppResult<LoadFileContent?>.Fail(ErrorKind.Io, "step=loadfile;" + ex.Message);
        }

        // 回读校验只针对 sqlite 仓储：它用「DELETE 全表 + 逐行 INSERT」改写播放集，
        // 一旦中途失败或提交语义与预期不符，行数/顺序就可能和写进去的不一致，
        // 而这正是加载文件校验兜不住的部分（加载文件写成功 ≠ 仓储写成功）。
        // EU5 playsets.json / CS2 playset_config.json 走整份文档原子替换，没有这个风险；
        // 且它们的 ModId 由路径或 sourceId 派生，比身份会产生假差异，所以不在这一步校验。
        if (game.WritesLoadFile)
        {
            var storeDiffs = await VerifySqliteStoreAsync(game, playSetId, orderedMods, dlcs, cancellationToken)
                .ConfigureAwait(false);
            if (storeDiffs.Count > 0)
            {
                var message = "step=verify-store;" + string.Join(", ", storeDiffs);
                _logger.Warn(message);
                return AppResult<LoadFileContent?>.Fail(ErrorKind.Io, message);
            }
        }

        if (expected is null)
        {
            return AppResult<LoadFileContent?>.Ok(null);
        }

        LoadFileContent actual;
        try
        {
            actual = await _loadFiles.ReadAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("保存后回读加载文件失败（gameId=" + game.GameId + "）", ex);
            return AppResult<LoadFileContent?>.Fail(ErrorKind.Io, "step=readback;" + ex.Message);
        }

        var diffs = CompareContent(game, expected, actual);
        if (diffs.Count > 0)
        {
            var message = "step=verify;" + string.Join(", ", diffs);
            _logger.Warn(message);
            return AppResult<LoadFileContent?>.Fail(ErrorKind.Io, message);
        }

        return AppResult<LoadFileContent?>.Ok(actual);
    }

    /// <summary>
    /// 保存后回读 sqlite 仓储，比对「写进去的」与「读出来的」是否一致。
    /// 只比能稳定复现的字段：mod 的 id / 启用态 / 顺序，DLC 的 id / 启用态。
    /// 名称、缩略图等来自 mods 表或磁盘扫描的派生字段不比，避免把环境差异当成保存失败。
    /// </summary>
    private async Task<IReadOnlyList<string>> VerifySqliteStoreAsync(
        GameInstallation game,
        Guid playSetId,
        IReadOnlyList<PlaySetMod> expectedMods,
        IReadOnlyList<PlaySetDlc> expectedDlcs,
        CancellationToken cancellationToken)
    {
        var diffs = new List<string>();

        IReadOnlyList<PlaySetMod> actualMods;
        try
        {
            actualMods = await _playSets.GetPlaySetModsAsync(game, playSetId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("保存后回读播放集 mod 行失败（gameId=" + game.GameId + "）", ex);
            diffs.Add("store-readback-mods:" + ex.Message);
            return diffs;
        }

        // sqlite 仓储的 playsets_mods 行总是「每个传入的 mod 一行」，没有跳过条件，
        // 所以期望值就是传入的列表本身（已按 position 重排）。
        var expectedModKeys = new List<string>(expectedMods.Count);
        foreach (var mod in expectedMods)
        {
            expectedModKeys.Add(FormatModKey(mod));
        }

        var actualModKeys = new List<string>(actualMods.Count);
        foreach (var mod in actualMods)
        {
            actualModKeys.Add(FormatModKey(mod));
        }

        AddSequenceDiff(diffs, "store-mods", expectedModKeys, actualModKeys);

        IReadOnlyList<PlaySetDlc> actualDlcs;
        try
        {
            actualDlcs = await _playSets.GetPlaySetDlcsAsync(game, playSetId, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("保存后回读播放集 DLC 行失败（gameId=" + game.GameId + "）", ex);
            diffs.Add("store-readback-dlcs:" + ex.Message);
            return diffs;
        }

        // 与 SavePlaySetDlcsAsync 的跳过条件保持一致：DlcId 为空的条目不落盘。
        var expectedDlcKeys = new List<string>(expectedDlcs.Count);
        foreach (var dlc in expectedDlcs)
        {
            if (!string.IsNullOrEmpty(dlc.DlcId))
            {
                expectedDlcKeys.Add(FormatDlcKey(dlc));
            }
        }

        var actualDlcKeys = new List<string>(actualDlcs.Count);
        foreach (var dlc in actualDlcs)
        {
            actualDlcKeys.Add(FormatDlcKey(dlc));
        }

        // 回读为空但期望非空时无法判定：可能是该库根本没有 playsets_dlcs 表（Stellaris），
        // 此时 SavePlaySetDlcsAsync 会 Warn 后合法跳过，不应当成保存失败。
        // 反过来「期望为空、回读非空」一定是差异，照常报。
        if (actualDlcKeys.Count > 0 || expectedDlcKeys.Count == 0)
        {
            AddSequenceDiff(diffs, "store-dlcs", expectedDlcKeys, actualDlcKeys);
        }
        else
        {
            _logger.Warn("保存后回读播放集 DLC 行为空（gameId=" + game.GameId + "），跳过 DLC 比对。");
        }

        return diffs;
    }

    private static string FormatModKey(PlaySetMod mod) => mod.ModId.ToString("D") + ":" + (mod.Enabled ? 1 : 0);

    private static string FormatDlcKey(PlaySetDlc dlc) => dlc.DlcId + ":" + (dlc.Enabled ? 1 : 0);

    /// <summary>
    /// 重命名播放集。IPlaySetRepository.RenamePlaySetAsync 返回 false 表示目标不存在或写入失败。
    /// </summary>
    public async Task<AppResult<bool>> RenamePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        string newName,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);

        try
        {
            var renamed = await _playSets
                .RenamePlaySetAsync(game, playSetId, newName.Trim(), cancellationToken)
                .ConfigureAwait(false);
            if (!renamed.IsSuccess || !renamed.Value)
            {
                var code = renamed.IsSuccess ? "playSet-not-found:" + playSetId.ToString("D") : renamed.ErrorMessage;
                var kind = renamed.IsSuccess ? ErrorKind.NotFound : renamed.Kind;
                _logger.Warn("重命名播放集失败（id=" + playSetId.ToString("N") + "，name=" + newName.Trim() + "）：" + code);
                return AppResult<bool>.Fail(kind!, code!);
            }

            _logger.Info("已重命名播放集（id=" + playSetId.ToString("N") + "）为「" + newName.Trim() + "」");
            return AppResult<bool>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("重命名播放集失败（id=" + playSetId.ToString("N") + "）", ex);
            return AppResult<bool>.Fail(ErrorKind.Io, "rename:" + ex.Message);
        }
    }

    /// <summary>
    /// 删除播放集。IPlaySetRepository.DeletePlaySetAsync 返回 false 表示目标不存在、是活动播放集或写入失败。
    /// 调用方必须先在界面上做二次确认。
    /// </summary>
    public async Task<AppResult<bool>> DeletePlaySetAsync(
        GameInstallation game,
        Guid playSetId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);

        try
        {
            var deleted = await _playSets
                .DeletePlaySetAsync(game, playSetId, cancellationToken)
                .ConfigureAwait(false);
            if (!deleted.IsSuccess || !deleted.Value)
            {
                var code = deleted.IsSuccess ? "playSet-not-found:" + playSetId.ToString("D") : deleted.ErrorMessage;
                var kind = deleted.IsSuccess ? ErrorKind.NotFound : deleted.Kind;
                _logger.Warn("删除播放集失败（id=" + playSetId.ToString("N") + "）：" + code);
                return AppResult<bool>.Fail(kind!, code!);
            }

            _logger.Info("已删除播放集（id=" + playSetId.ToString("N") + "）");
            return AppResult<bool>.Ok(true);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("删除播放集失败（id=" + playSetId.ToString("N") + "）", ex);
            return AppResult<bool>.Fail(ErrorKind.Io, "delete:" + ex.Message);
        }
    }
}
