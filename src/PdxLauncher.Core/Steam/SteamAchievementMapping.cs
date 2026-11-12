namespace PdxLauncher.Core.Steam;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 把宿主返回的成就数据映射成页面用的 <see cref="AchievementSnapshot"/>。
/// <para>
/// 单独抽成静态类（不依赖 WinUI、不碰原生库），这样自检工程可以直接引用它，
/// 把「坏 JSON / 空数组 / Steam 不可用」这些分支钉死在纯逻辑上。
/// </para>
/// </summary>
public static class SteamAchievementMapping
{
    /// <summary>Steam 侧不可用（没启动、Init 失败、超时等）时的提示键。</summary>
    public const string SteamUnavailableMessageKey = "achievements.unavailable.steamUnavailable";

    /// <summary>成功读到数据但一条成就都没有时的提示键。</summary>
    public const string EmptyMessageKey = "achievements.unavailable.empty";

    /// <summary>
    /// 既没有本地化、也没有 Steam 显示名时的占位符。
    /// <para>绝不能用 API 名兜底：API 名是 <c>achievement_vox_populi</c> 这种内部标识，不是给人看的。</para>
    /// </summary>
    public const string UnknownName = "—";

    /// <summary>
    /// 宿主结果 → 快照。
    /// <para>结果码语义：<see cref="SteamAchievementCodes.NoAchievements"/> 归为
    /// <see cref="AchievementAvailability.Empty"/>，其余非成功码一律归为
    /// <see cref="AchievementAvailability.SteamUnavailable"/> —— 「Steam 没运行」绝不能显示成「没有成就」。</para>
    /// </summary>
    /// <param name="result">宿主结果；null 表示宿主输出无法解码。</param>
    /// <param name="fetchedAtUtc">取数时间；为空则用当前 UTC 时间。</param>
    /// <param name="localization">游戏本地化字典（见 <see cref="SteamAchievementLocalization.Load"/>）；可为空。</param>
    public static AchievementSnapshot FromHostResult(
        SteamHostAchievementsResult? result,
        DateTimeOffset? fetchedAtUtc = null,
        IReadOnlyDictionary<string, string>? localization = null)
    {
        var fetchedAt = fetchedAtUtc ?? DateTimeOffset.UtcNow;
        if (result is null)
        {
            return SteamUnavailable(fetchedAt);
        }

        if (result.Ok)
        {
            var entries = MapEntries(result.Items, localization);
            if (entries.Count == 0)
            {
                return Empty(fetchedAt);
            }

            return new AchievementSnapshot
            {
                Availability = AchievementAvailability.Available,
                Entries = entries,
                FetchedAtUtc = fetchedAt,
                Code = SteamAchievementCodes.Ok,
            };
        }

        if (result.Code == SteamAchievementCodes.NoAchievements)
        {
            return Empty(fetchedAt) with { Code = result.Code };
        }

        return SteamUnavailable(fetchedAt) with { Code = result.Code };
    }

    /// <summary>把宿主条目映射成 <see cref="AchievementEntry"/>（过滤掉没有 id 的脏数据）。</summary>
    /// <param name="items">宿主条目。</param>
    /// <param name="localization">游戏本地化字典；有值时优先于 Steam 的英文显示名。</param>
    public static IReadOnlyList<AchievementEntry> MapEntries(
        IReadOnlyList<SteamHostAchievementItem>? items,
        IReadOnlyDictionary<string, string>? localization = null)
    {
        if (items is null || items.Count == 0)
        {
            return Array.Empty<AchievementEntry>();
        }

        var entries = new List<AchievementEntry>(items.Count);
        foreach (var item in items)
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name))
            {
                continue;
            }

            var localizedName = SteamAchievementLocalization.Name(localization, item.Name);
            var localizedDescription = SteamAchievementLocalization.Description(localization, item.Name);

            entries.Add(new AchievementEntry(
                Id: item.Name,
                Name: ResolveName(item, localizedName),
                Description: string.IsNullOrWhiteSpace(localizedDescription)
                    ? item.Description ?? string.Empty
                    : localizedDescription,
                IsHidden: item.Hidden,
                IsUnlocked: item.Achieved,
                UnlockTimeUtc: ToUnlockTime(item.Achieved, item.UnlockTime),
                GlobalPercent: ToPercent(item.GlobalPercent),
                IconUrl: null));
        }

        return entries;
    }

    /// <summary>名字优先级：本地化 → Steam displayName → 占位符；API 名永远不露给用户。</summary>
    private static string ResolveName(SteamHostAchievementItem item, string? localizedName)
    {
        if (!string.IsNullOrWhiteSpace(localizedName))
        {
            return localizedName;
        }

        // 宿主以前会用 API 名填 DisplayName，那种值等于内部标识，不能直接显示。
        if (!string.IsNullOrWhiteSpace(item.DisplayName)
            && !string.Equals(item.DisplayName, item.Name, StringComparison.Ordinal))
        {
            return item.DisplayName;
        }

        return UnknownName;
    }

    /// <summary>「这款游戏不支持 / 不是已知 Steam 游戏」。</summary>
    public static AchievementSnapshot NotSupported(DateTimeOffset? fetchedAtUtc = null) => new()
    {
        Availability = AchievementAvailability.NotSupported,
        Entries = Array.Empty<AchievementEntry>(),
        FetchedAtUtc = fetchedAtUtc ?? DateTimeOffset.UtcNow,
        Message = Abstractions.NullAchievementSource.NotSupportedMessageKey,
    };

    /// <summary>「Steam 侧暂时不可用」。</summary>
    public static AchievementSnapshot SteamUnavailable(DateTimeOffset? fetchedAtUtc = null) => new()
    {
        Availability = AchievementAvailability.SteamUnavailable,
        Entries = Array.Empty<AchievementEntry>(),
        FetchedAtUtc = fetchedAtUtc ?? DateTimeOffset.UtcNow,
        Message = SteamUnavailableMessageKey,
    };

    /// <summary>「成功拿到数据，但这游戏没有成就」。</summary>
    public static AchievementSnapshot Empty(DateTimeOffset? fetchedAtUtc = null) => new()
    {
        Availability = AchievementAvailability.Empty,
        Entries = Array.Empty<AchievementEntry>(),
        FetchedAtUtc = fetchedAtUtc ?? DateTimeOffset.UtcNow,
        Message = EmptyMessageKey,
    };

    private static DateTimeOffset? ToUnlockTime(bool achieved, uint unlockTime)
    {
        if (!achieved || unlockTime == 0)
        {
            return null;
        }

        // Steam 的时间戳是 Unix 秒；异常值（例如 0xFFFFFFFF）宁可丢掉也不让页面崩。
        try
        {
            return DateTimeOffset.FromUnixTimeSeconds(unlockTime);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static double? ToPercent(double? percent)
    {
        if (percent is not { } value || double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        return Math.Clamp(Math.Round(value, 1, MidpointRounding.AwayFromZero), 0d, 100d);
    }
}
