namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods;

/// <summary>
/// <see cref="WorkshopDependencyMatcher"/> 自检：把 Paradox Mods 的依赖清单
/// 和本机 mod 库对起来时，四种状态（已启用/装了没启用/没装/读不到）都要判准。
/// <para>
/// 关键点：读不到 mod 库时必须给 Unknown 而不是 Missing——
/// 误报「缺前置」比不报更伤用户。注册 id 用数字边界匹配（74417 不能命中 774417）。
/// </para>
/// </summary>
public static class WorkshopDependencyMatcherChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);

        var before = ChecksContext.Failures;
        CheckEmpty();
        CheckUnknownWhenLibraryUnavailable();
        CheckEnabled();
        CheckInstalledButDisabled();
        CheckMissing();
        CheckRegistryIdBoundary();
        CheckSteamIdAndNameFallback();
        return ChecksContext.Failures - before;
    }

    /// <summary>没有依赖清单时给空列表。</summary>
    private static void CheckEmpty()
    {
        ChecksContext.Equal("null 依赖清单给空列表", 0, WorkshopDependencyMatcher.Match(null, [], null).Count);
        ChecksContext.Equal("空依赖清单给空列表", 0, WorkshopDependencyMatcher.Match([], null, null).Count);
    }

    /// <summary>读不到 mod 库 → 全部 Unknown。</summary>
    private static void CheckUnknownWhenLibraryUnavailable()
    {
        var result = WorkshopDependencyMatcher.Match([Requirement("74417", "Unified Icon Library")], null, null);
        ChecksContext.Equal("读不到 mod 库给 1 条结果", 1, result.Count);
        ChecksContext.Equal("读不到 mod 库是 Unknown", WorkshopDependencyState.Unknown, result[0].State);
        ChecksContext.Equal("Unknown 不提醒", false, result[0].NeedsAttention);
        ChecksContext.Equal("Unknown 不标缺失", false, result[0].State == WorkshopDependencyState.Missing);
    }

    /// <summary>装了且在当前播放集启用。</summary>
    private static void CheckEnabled()
    {
        var modId = Guid.NewGuid();
        var installed = new[] { Entry(modId, "Unified Icon Library", "ugc_74417.mod") };
        var result = WorkshopDependencyMatcher.Match(
            [Requirement("74417", "Unified Icon Library")],
            installed,
            new HashSet<Guid> { modId });

        ChecksContext.Equal("启用状态", WorkshopDependencyState.Enabled, result[0].State);
        ChecksContext.Equal("命中本机 mod id", modId, result[0].InstalledModId);
        ChecksContext.Equal("启用不提醒", false, result[0].NeedsAttention);
    }

    /// <summary>装了但没在当前播放集启用 → 提醒。</summary>
    private static void CheckInstalledButDisabled()
    {
        var modId = Guid.NewGuid();
        var installed = new[] { Entry(modId, "Unified Icon Library", "ugc_74417.mod") };
        var result = WorkshopDependencyMatcher.Match(
            [Requirement("74417", "Unified Icon Library")],
            installed,
            new HashSet<Guid>());

        ChecksContext.Equal("装了没启用", WorkshopDependencyState.InstalledDisabled, result[0].State);
        ChecksContext.Equal("装了没启用要提醒", true, result[0].NeedsAttention);
    }

    /// <summary>本机没有 → Missing 并提醒。</summary>
    private static void CheckMissing()
    {
        var installed = new[] { Entry(Guid.NewGuid(), "别的 mod", "ugc_111.mod") };
        var result = WorkshopDependencyMatcher.Match(
            [Requirement("74417", "Unified Icon Library")],
            installed,
            null);

        ChecksContext.Equal("没装是 Missing", WorkshopDependencyState.Missing, result[0].State);
        ChecksContext.Equal("没装要提醒", true, result[0].NeedsAttention);
        ChecksContext.Equal("没装没有命中 id", null, result[0].InstalledModId);
    }

    /// <summary>注册 id 用数字边界：74417 不能命中 774417。</summary>
    private static void CheckRegistryIdBoundary()
    {
        var other = new[] { Entry(Guid.NewGuid(), "另一个 mod", "ugc_774417.mod") };
        var result = WorkshopDependencyMatcher.Match([Requirement("74417", "")], other, null);
        ChecksContext.Equal("数字边界不误命中", WorkshopDependencyState.Missing, result[0].State);

        var exact = new[] { Entry(Guid.NewGuid(), "目标 mod", "ugc_74417.mod") };
        var hit = WorkshopDependencyMatcher.Match([Requirement("74417", "")], exact, null);
        ChecksContext.Equal("精确命中注册 id", WorkshopDependencyState.InstalledDisabled, hit[0].State);
    }

    /// <summary>注册 id 对不上时，退到 SteamId 与名称。</summary>
    private static void CheckSteamIdAndNameFallback()
    {
        var bySteam = new[] { Entry(Guid.NewGuid(), "某 mod", "unrelated", steamId: "2517153104") };
        var steamResult = WorkshopDependencyMatcher.Match(
            [Requirement("2517153104", "某 mod")],
            bySteam,
            null);
        ChecksContext.Equal("按 SteamId 命中", WorkshopDependencyState.InstalledDisabled, steamResult[0].State);

        var byName = new[] { Entry(Guid.NewGuid(), "Unified Icon Library", string.Empty) };
        var nameResult = WorkshopDependencyMatcher.Match(
            [Requirement("74417", "Unified Icon Library")],
            byName,
            null);
        ChecksContext.Equal("按名称命中", WorkshopDependencyState.InstalledDisabled, nameResult[0].State);
    }

    private static PdxModRequirement Requirement(string id, string name) =>
        new(ProductType: "MOD", Id: id, Name: name, Version: 1, DependencyVersion: null);

    private static ModLibraryEntry Entry(Guid modId, string name, string registryId, string? steamId = null) =>
        new(
            ModId: modId,
            Name: name,
            GameRegistryId: registryId,
            DirPath: null,
            ArchivePath: null,
            ThumbnailPath: null,
            RequiredVersion: string.Empty,
            Status: "ready_to_play",
            Source: "paradox",
            SizeBytes: null,
            SubscribedOn: null,
            PlaySetIds: [],
            SteamId: steamId);
}
