namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Steam;

/// <summary>本地 mod 与创意工坊条目之间的映射；只存 <c>pdxnext.sqlite</c> 的 documents 表，不碰 mods.steamId。</summary>
/// <param name="GameId">游戏 id。</param>
/// <param name="ModPath">本地 mod 目录全路径（规范化后作为键的一部分）。</param>
/// <param name="PublishedFileId">已创建的工坊条目 id；失败但拿到 id 时也会保存。</param>
/// <param name="Title">上次提交的标题（默认语言）。</param>
/// <param name="Description">上次提交的简介（默认语言）。</param>
/// <param name="Tags">上次提交的标签。</param>
/// <param name="Public">旧字段：只区分公开 / 隐藏；新记录一律以 <see cref="Visibility"/> 为准。</param>
/// <param name="PreviewPath">上次提交的主封面路径。</param>
/// <param name="LastError">上次失败原因；成功后清空。</param>
/// <param name="UpdatedAt">最后更新时间。</param>
/// <param name="Visibility">上次提交的可见性四档；旧记录为 null，用 <see cref="EffectiveVisibility"/> 迁移。</param>
/// <param name="ContentDescriptors">上次提交的内容描述词 id（1-5）。</param>
/// <param name="PreviewPaths">上次提交的附加预览图路径。</param>
/// <param name="PreviewVideoIds">上次提交的附加预览视频 YouTube ID。</param>
/// <param name="Localizations">上次提交的附加语言标题与描述。</param>
/// <param name="SteamVisibility">
/// **Steam 侧的事实**可见性：0 公开 / 1 仅好友 / 2 私密 / 3 未列出；null = 没核实过
/// （旧记录，或者没联网查过）。与 <see cref="Visibility"/> 不是一回事：那个是「上次提交时想设成什么」，
/// 这个是从 Steam 读回来的「现在实际是什么」。已发布却被设成私密的条目，只有这个字段能说清楚。
/// </param>
public sealed record ModPublication(
    string GameId,
    string ModPath,
    ulong? PublishedFileId,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    bool Public,
    string? PreviewPath,
    string? LastError,
    DateTimeOffset UpdatedAt,
    SteamWorkshopVisibility? Visibility = null,
    IReadOnlyList<int>? ContentDescriptors = null,
    IReadOnlyList<string>? PreviewPaths = null,
    IReadOnlyList<string>? PreviewVideoIds = null,
    IReadOnlyList<ModLocalizedText>? Localizations = null,
    int? SteamVisibility = null)
{
    /// <summary>实际生效的可见性：有 <see cref="Visibility"/> 用它，否则从旧 <see cref="Public"/> 迁移。</summary>
    public SteamWorkshopVisibility EffectiveVisibility =>
        Visibility ?? SteamWorkshopVisibilityExtensions.Effective(Public);

    /// <summary>已勾选的内容描述词；没有记录时返回空数组。</summary>
    public IReadOnlyList<int> Descriptors => ContentDescriptors ?? [];

    /// <summary>附加预览图；没有记录时返回空数组。</summary>
    public IReadOnlyList<string> ExtraPreviews => PreviewPaths ?? [];

    /// <summary>附加预览视频；没有记录时返回空数组。</summary>
    public IReadOnlyList<string> Videos => PreviewVideoIds ?? [];

    /// <summary>附加语言；没有记录时返回空数组。</summary>
    public IReadOnlyList<ModLocalizedText> ExtraLocalizations => Localizations ?? [];

    /// <summary>Steam 侧现在能公开访问（或还没核实过）。未核实时按「能公开」处理，不误报。</summary>
    public bool IsPubliclyVisible => SteamVisibility is null or 0;

    /// <summary>Steam 侧限制了可见性：仅好友 / 私密 / 未列出。作者常在这一档上以为「没上传成功」。</summary>
    public bool IsUnlisted => SteamVisibility is 1 or 2 or 3;
}
