namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 组装「Mod 库」页的数据：mods 表里的全部 mod（含大小、订阅时间、播放集归属），
/// 再合并磁盘上扫描到、但数据库还没登记的本地 mod（<see cref="ModLibraryEntry.IsUnregistered"/> 为 true）。
/// </summary>
public sealed class ModLibraryService
{
    private readonly IPlaySetRepository _playSets;
    private readonly IModCatalog _mods;
    private readonly IAppLogger _logger;

    public ModLibraryService(IPlaySetRepository playSets, IModCatalog mods, IAppLogger logger)
    {
        _playSets = playSets ?? throw new ArgumentNullException(nameof(playSets));
        _mods = mods ?? throw new ArgumentNullException(nameof(mods));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>读取 Mod 库快照；未选中游戏时返回空快照。</summary>
    public async Task<ModLibrarySnapshot> LoadAsync(GameInstallation? game, CancellationToken cancellationToken)
    {
        if (game is null)
        {
            return ModLibrarySnapshot.Empty;
        }

        var entries = await _playSets.GetModLibraryAsync(game, cancellationToken).ConfigureAwait(false);
        var playSets = await _playSets.GetPlaySetsAsync(game, cancellationToken).ConfigureAwait(false);

        var merged = new List<ModLibraryEntry>(entries);
        var known = new HashSet<Guid>(entries.Select(entry => entry.ModId));

        // 磁盘扫描失败不应该让整个 Mod 库空白：只影响「本地 mod」这一段。
        IReadOnlyList<PlaySetMod> catalog;
        try
        {
            catalog = await _mods.DiscoverAllAsync(game, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Warn("Mod 库：磁盘 mod 扫描失败，仅显示数据库中的 mod：" + ex.Message);
            catalog = Array.Empty<PlaySetMod>();
        }

        // 数据库里的 steamId 可能为空（老库、或官方启动器没写这一列），而磁盘上 mod 的
        // .mod 描述文件 / metadata.json 一般带着创意工坊 id：用扫描结果给已登记的行补齐，
        // 播放集页与 Mod 库页的「在 Steam / 浏览器中打开」才不会平白少一半。
        var scannedSteamIds = new Dictionary<Guid, string>();
        foreach (var mod in catalog)
        {
            if (!string.IsNullOrWhiteSpace(mod.SteamId))
            {
                scannedSteamIds[mod.ModId] = mod.SteamId;
            }
        }

        if (scannedSteamIds.Count > 0)
        {
            for (var index = 0; index < merged.Count; index++)
            {
                var entry = merged[index];
                if (string.IsNullOrWhiteSpace(entry.SteamId)
                    && scannedSteamIds.TryGetValue(entry.ModId, out var scannedSteamId))
                {
                    merged[index] = entry with { SteamId = scannedSteamId };
                }
            }
        }

        foreach (var mod in catalog)
        {
            if (!known.Add(mod.ModId))
            {
                continue;
            }

            // 磁盘上有、官方库里没有：不一定是「本地 mod」——刚订阅的工坊 mod 在官方启动器扫描前就是这样。
            // 拿得到创意工坊编号就按工坊 mod 展示（详情页/Steam/浏览器三个按钮才亮），否则才当本地 mod。
            var source = SteamWorkshop.GetWorkshopId(mod.SteamId, mod.GameRegistryId) is null
                ? ModWorkshopRef.SourceLocal
                : ModWorkshopRef.SourceSteam;

            merged.Add(new ModLibraryEntry(
                mod.ModId,
                mod.Name,
                mod.GameRegistryId,
                mod.DirPath,
                mod.ArchivePath,
                mod.ThumbnailPath,
                mod.RequiredVersion,
                mod.Status,
                source,
                SizeBytes: null,
                SubscribedOn: null,
                Array.Empty<Guid>(),
                IsUnregistered: true,
                SteamId: mod.SteamId));
        }

        merged.Sort(static (left, right) =>
        {
            var byName = string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            return byName != 0 ? byName : left.ModId.CompareTo(right.ModId);
        });

        return new ModLibrarySnapshot(merged, playSets);
    }
}
