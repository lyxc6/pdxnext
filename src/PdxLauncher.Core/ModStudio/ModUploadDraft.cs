namespace PdxLauncher.Core.ModStudio;

using PdxLauncher.Core.Steam;

/// <summary>
/// 上传表单草稿：外部工具（MCP）写入、启动器读取后填入上传面板。
/// <para>
/// 只存会出现在上传表单里的元数据；预览图、附加语言和 Mod ID 仍由启动器自己的
/// 发布记录维护，草稿不碰这些字段。
/// </para>
/// </summary>
/// <param name="SchemaVersion">草稿格式版本；不认识的版本按“没有草稿”处理。</param>
/// <param name="GameId">游戏 id（正名）。</param>
/// <param name="ModPath">本地 mod 目录全路径。</param>
/// <param name="Title">工坊标题。</param>
/// <param name="Description">工坊描述（BBCode 原文）。</param>
/// <param name="Tags">发往 Steam 的标签机器名。</param>
/// <param name="Visibility">可见性。</param>
/// <param name="ChangeNote">更新说明；null 表示不写。</param>
/// <param name="ContentDescriptors">内容描述词 id（1-5）；null 表示不覆盖。</param>
/// <param name="UpdatedAt">写入时间（UTC）。</param>
public sealed record ModUploadDraft(
    string GameId,
    string ModPath,
    string Title,
    string Description,
    IReadOnlyList<string> Tags,
    SteamWorkshopVisibility Visibility,
    string? ChangeNote,
    IReadOnlyList<int>? ContentDescriptors,
    DateTimeOffset UpdatedAt,
    int SchemaVersion = ModUploadDraft.CurrentSchemaVersion)
{
    /// <summary>当前草稿格式版本。</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>内容描述词；没有记录时为空数组。</summary>
    public IReadOnlyList<int> Descriptors => ContentDescriptors ?? [];
}
