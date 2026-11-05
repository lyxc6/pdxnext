namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一次成就取数的完整结果。不可变，页面拿到后只读。
/// <para>
/// <see cref="Availability"/> 默认是 <see cref="AchievementAvailability.NotSupported"/>：
/// 「还没接数据源」是默认状态，绝不能默认成 <see cref="AchievementAvailability.Available"/>，
/// 否则空列表会被界面解释成「这款游戏没有成就」，掩盖掉真正的接入缺口。
/// </para>
/// </summary>
public sealed record AchievementSnapshot
{
    /// <summary>数据可用性；默认 <see cref="AchievementAvailability.NotSupported"/>。</summary>
    public AchievementAvailability Availability { get; init; } = AchievementAvailability.NotSupported;

    /// <summary>成就列表；不可用时为空集合。</summary>
    public IReadOnlyList<AchievementEntry> Entries { get; init; } = [];

    /// <summary>取数时间（UTC）。</summary>
    public DateTimeOffset FetchedAtUtc { get; init; }

    /// <summary>
    /// 给界面用的原因说明<b>键</b>（如 <c>achievements.unavailable.steamUnavailable</c>），
    /// 不是中文文案；null 表示没有额外说明。展示层优先按 <see cref="Availability"/> 选文案，
    /// 这里只用于数据源想补充更具体的原因。
    /// </summary>
    public string? Message { get; init; }
}
