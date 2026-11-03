namespace PdxLauncher.App.Services;

/// <summary>
/// Mod 创建页里两块表单（新建 / 上传）的草稿：页面销毁重建时把用户已经填的内容放回去，
/// 避免离开一下详情就丢掉半张表单。
/// </summary>
/// <param name="CreateName">新建表单：mod 名称。</param>
/// <param name="CreateFolderName">新建表单：目录名。</param>
/// <param name="CreateVersion">新建表单：版本号。</param>
/// <param name="CreateSupportedVersion">新建表单：支持的游戏版本。</param>
/// <param name="CreateTags">新建表单：标签。</param>
/// <param name="CreateDescription">新建表单：描述。</param>
/// <param name="CreatePreviewPath">新建表单：预览图路径。</param>
/// <param name="UploadTitle">上传表单：标题。</param>
/// <param name="UploadDescription">上传表单：描述。</param>
/// <param name="UploadTags">上传表单：标签。</param>
/// <param name="UploadChangeNote">上传表单：更新说明。</param>
/// <param name="UploadPreviewPath">上传表单：预览图路径。</param>
/// <param name="UploadPublic">上传表单：是否公开。</param>
/// <param name="UploadTarget">上传表单：目标平台。</param>
/// <param name="UploadModId">上传表单：工坊条目 id。</param>
/// <param name="UploadDirectory">上传表单：目录。</param>
/// <param name="UploadVisibility">上传表单：可见性。</param>
/// <param name="UploadDescriptorAdult">上传表单：成人内容标记。</param>
/// <param name="UploadDescriptorViolence">上传表单：暴力内容标记。</param>
public sealed record ModStudioDraft(
    string CreateName,
    string CreateFolderName,
    string CreateVersion,
    string CreateSupportedVersion,
    string CreateTags,
    string CreateDescription,
    string? CreatePreviewPath,
    string UploadTitle,
    string UploadDescription,
    string UploadTags,
    string UploadChangeNote,
    string? UploadPreviewPath,
    bool UploadPublic,
    string UploadTarget,
    string UploadModId,
    string UploadDirectory,
    string UploadVisibility,
    bool UploadDescriptorAdult,
    bool UploadDescriptorViolence);
