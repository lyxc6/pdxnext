namespace PdxLauncher.Core.Steam;

/// <summary>
/// 创意工坊条目的可见性，取值与 Steamworks 的
/// <c>ERemoteStoragePublishedFileVisibility</c> 一一对应。
/// </summary>
public enum SteamWorkshopVisibility
{
    /// <summary>公开：所有人可见、可在列表里检索到。</summary>
    Public = 0,

    /// <summary>仅限好友：只有好友能看见。</summary>
    FriendsOnly = 1,

    /// <summary>隐藏：只有作者本人能看见。</summary>
    Private = 2,

    /// <summary>非公开：知道链接的人能访问，但不出现在列表与搜索里。</summary>
    Unlisted = 3,
}

/// <summary>可见性枚举与 Steamworks 原生整数之间的转换。</summary>
public static class SteamWorkshopVisibilityExtensions
{
    /// <summary>所有档位，顺序与 Steam 面板一致。</summary>
    public static IReadOnlyList<SteamWorkshopVisibility> All { get; } =
    [
        SteamWorkshopVisibility.Public,
        SteamWorkshopVisibility.FriendsOnly,
        SteamWorkshopVisibility.Private,
        SteamWorkshopVisibility.Unlisted,
    ];

    /// <summary>转成传给 <c>SetItemVisibility</c> 的整数。</summary>
    public static int ToNative(this SteamWorkshopVisibility visibility) => (int)visibility;

    /// <summary>
    /// 从原生整数解析；越界值回落 <see cref="SteamWorkshopVisibility.Public"/>，
    /// 保证坏数据不会把条目意外设成不可见。
    /// </summary>
    public static SteamWorkshopVisibility FromNative(int value) => value switch
    {
        0 => SteamWorkshopVisibility.Public,
        1 => SteamWorkshopVisibility.FriendsOnly,
        2 => SteamWorkshopVisibility.Private,
        3 => SteamWorkshopVisibility.Unlisted,
        _ => SteamWorkshopVisibility.Public,
    };

    /// <summary>
    /// 旧记录只有 <c>bool Public</c> 时用它迁移：true → 公开，false → 隐藏。
    /// 这与旧实现写死的 0 / 2 完全一致，不会改变已发布条目的表现。
    /// </summary>
    public static SteamWorkshopVisibility Effective(bool isPublic) =>
        isPublic ? SteamWorkshopVisibility.Public : SteamWorkshopVisibility.Private;
}
