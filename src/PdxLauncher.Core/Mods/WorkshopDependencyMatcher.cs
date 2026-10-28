namespace PdxLauncher.Core.Mods;

using PdxLauncher.Core.Contracts;

/// <summary>前置依赖在本机的状态。</summary>
public enum WorkshopDependencyState
{
    /// <summary>装了，而且在当前播放集里是启用状态。</summary>
    Enabled,

    /// <summary>装了，但当前播放集没启用。</summary>
    InstalledDisabled,

    /// <summary>本机没有（能读到 mod 库才敢这么判）。</summary>
    Missing,

    /// <summary>读不到 mod 库，无法判断。</summary>
    Unknown,
}

/// <summary>一条依赖的判定结果。</summary>
/// <param name="Requirement">服务端给的依赖。</param>
/// <param name="State">本机状态。</param>
/// <param name="InstalledModId">命中的已安装 mod id（mods.id，Guid；没命中为 null）。</param>
public sealed record WorkshopDependency(
    PdxModRequirement Requirement,
    WorkshopDependencyState State,
    Guid? InstalledModId = null)
{
    /// <summary>是否值得提醒用户（缺失或未启用）。</summary>
    public bool NeedsAttention => State is WorkshopDependencyState.Missing or WorkshopDependencyState.InstalledDisabled;
}

/// <summary>
/// 把 Paradox Mods 的依赖清单和本机 mod 库对起来。
/// <para>
/// 纯逻辑、不碰 IO，检查工程可直接自检。匹配顺序：注册 id → SteamID → 名称
/// （服务端 <c>id</c> 对应本地 <c>GameRegistryId</c> 里的数字，<c>steamId</c> 对应 Steam 订阅）。
/// 读不到 mod 库时（<c>installed == null</c>）一律给 <see cref="WorkshopDependencyState.Unknown"/>，
/// 宁可不判也不误报「未安装」。
/// </para>
/// </summary>
public static class WorkshopDependencyMatcher
{
    /// <summary>逐条判定依赖状态。</summary>
    /// <param name="requirements">依赖清单；null 或空返回空列表。</param>
    /// <param name="installed">本机 mod 库；null 表示读不到（结果全是 Unknown）。</param>
    /// <param name="enabledModIds">当前播放集里启用中的 mod id（Guid）集合；null 视为全部未启用。</param>
    public static IReadOnlyList<WorkshopDependency> Match(
        IReadOnlyList<PdxModRequirement>? requirements,
        IReadOnlyList<ModLibraryEntry>? installed,
        IReadOnlySet<Guid>? enabledModIds)
    {
        if (requirements is null || requirements.Count == 0)
        {
            return [];
        }

        var results = new List<WorkshopDependency>(requirements.Count);
        foreach (var requirement in requirements)
        {
            if (installed is null)
            {
                results.Add(new WorkshopDependency(requirement, WorkshopDependencyState.Unknown));
                continue;
            }

            var found = Find(installed, requirement);
            if (found is null)
            {
                results.Add(new WorkshopDependency(requirement, WorkshopDependencyState.Missing));
                continue;
            }

            var enabled = enabledModIds is not null && enabledModIds.Contains(found.ModId);
            results.Add(new WorkshopDependency(
                requirement,
                enabled ? WorkshopDependencyState.Enabled : WorkshopDependencyState.InstalledDisabled,
                found.ModId));
        }

        return results;
    }

    private static ModLibraryEntry? Find(IReadOnlyList<ModLibraryEntry> installed, PdxModRequirement requirement)
    {
        foreach (var entry in installed)
        {
            if (MatchesRegistryId(entry.GameRegistryId, requirement.Id))
            {
                return entry;
            }
        }

        foreach (var entry in installed)
        {
            if (!string.IsNullOrWhiteSpace(entry.SteamId)
                && string.Equals(entry.SteamId, requirement.Id, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        if (string.IsNullOrWhiteSpace(requirement.Name))
        {
            return null;
        }

        foreach (var entry in installed)
        {
            if (string.Equals(entry.Name, requirement.Name, StringComparison.OrdinalIgnoreCase))
            {
                return entry;
            }
        }

        return null;
    }

    /// <summary>
    /// 注册 id 里找依赖 id：本地是 <c>ugc_74417.mod</c> 这种形式，服务端给的是 <c>74417</c>。
    /// 用「数字边界」判断，避免 74417 命中 774417。
    /// </summary>
    private static bool MatchesRegistryId(string? registryId, string id)
    {
        if (string.IsNullOrWhiteSpace(registryId) || string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        if (string.Equals(registryId, id, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var position = registryId.IndexOf(id, StringComparison.OrdinalIgnoreCase);
        while (position >= 0)
        {
            var before = position == 0 ? '\0' : registryId[position - 1];
            var afterIndex = position + id.Length;
            var after = afterIndex >= registryId.Length ? '\0' : registryId[afterIndex];

            if (!char.IsAsciiDigit(before) && !char.IsAsciiDigit(after))
            {
                return true;
            }

            position = registryId.IndexOf(id, position + 1, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }
}
