namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一条成就的只读快照。数据源负责把平台字段（Steam / 本地缓存）翻译成这份契约，
/// 展示层只认这里，不认任何平台 API。
/// <para>
/// <see cref="UnlockTimeUtc"/> 为 null 表示未解锁（已解锁但平台没给时间时也为 null）；
/// <see cref="GlobalPercent"/> 为 null 表示平台没有稀有度数据，界面应隐藏该行而不是显示 0%。
/// </para>
/// </summary>
/// <param name="Id">平台内唯一 id（Steam 的 achievement API name）。</param>
/// <param name="Name">成就名称。</param>
/// <param name="Description">成就说明；隐藏成就也保留原文，由展示层决定是否打码。</param>
/// <param name="IsHidden">是否为隐藏成就（未解锁前不剧透）。</param>
/// <param name="IsUnlocked">当前账号是否已解锁。</param>
/// <param name="UnlockTimeUtc">解锁时间（UTC）；未解锁时为 null。</param>
/// <param name="GlobalPercent">全球解锁率（0-100）；平台没有该数据时为 null。</param>
/// <param name="IconUrl">图标地址或本地路径；没有图标时为 null，界面用占位方块。</param>
public sealed record AchievementEntry(
    string Id,
    string Name,
    string Description,
    bool IsHidden,
    bool IsUnlocked,
    DateTimeOffset? UnlockTimeUtc,
    double? GlobalPercent,
    string? IconUrl);
