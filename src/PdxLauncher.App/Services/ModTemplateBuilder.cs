namespace PdxLauncher.App.Services;

using System;
using System.Collections.Generic;

using PdxLauncher.Core.ModStudio;

/// <summary>
/// 一键建模板的表单归一与请求构造（第 3 部分）：复用 <see cref="ModDescriptorRules"/> 与
/// <see cref="ModCreator.IsValidFolderName"/> 的唯一口径，页面与自检共用，避免判定漂移。
/// 只依赖 System.* + Core.ModStudio；不碰 UI 类型，可被自检工程原样链接。
/// <para>全程全本地：不构造 URL、不发 HTTP；真正建目录仍走 <c>ModStudioService.Create</c>。</para>
/// </summary>
public static class ModTemplateBuilder
{
    /// <summary>创建按钮是否可用的判定：游戏支持 + 名称与目录名都填了且目录名合法。</summary>
    public static bool CanCreate(string? gameId, string? name, string? folderName)
    {
        return ModDescriptorRules.CanCreate(gameId)
            && !string.IsNullOrWhiteSpace(name)
            && ModCreator.IsValidFolderName(folderName);
    }

    /// <summary>
    /// 目录名的校验提示键：空名提示必填；非法定目录名提示格式；合法但游戏不支持提示不支持；
    /// 否则返回描述符家族说明键（由展示层翻译）。
    /// </summary>
    public static string FolderHintKey(string? gameId, string? folderName)
    {
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return "ModEditorTemplateNameRequired";
        }

        if (!ModCreator.IsValidFolderName(folderName))
        {
            return "ModEditorTemplateFolderHint";
        }

        if (!ModDescriptorRules.CanCreate(gameId))
        {
            return "ModEditorTemplateUnsupported";
        }

        return ModDescriptorRules.DescribeKey(ModDescriptorRules.FamilyFor(gameId));
    }

    /// <summary>把逗号分隔的标签串拆成去空、去重的列表（保持原顺序）。</summary>
    public static IReadOnlyList<string> ParseTags(string? tags)
    {
        if (string.IsNullOrWhiteSpace(tags))
        {
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in tags.Split(','))
        {
            var tag = raw.Trim();
            if (tag.Length > 0 && seen.Add(tag))
            {
                result.Add(tag);
            }
        }

        return result;
    }

    /// <summary>
    /// 由表单字段构造创建请求；所有字符串先 Trim。调用方应先用 <see cref="CanCreate"/> 判定。
    /// </summary>
    public static ModCreateRequest BuildRequest(
        string? name,
        string? folderName,
        string? version,
        string? supportedVersion,
        string? tags,
        string? description,
        string? previewPath)
    {
        return new ModCreateRequest(
            (name ?? string.Empty).Trim(),
            (folderName ?? string.Empty).Trim(),
            (version ?? string.Empty).Trim(),
            (supportedVersion ?? string.Empty).Trim(),
            ParseTags(tags),
            string.IsNullOrWhiteSpace(description) ? null : description.Trim(),
            string.IsNullOrWhiteSpace(previewPath) ? null : previewPath.Trim());
    }
}
