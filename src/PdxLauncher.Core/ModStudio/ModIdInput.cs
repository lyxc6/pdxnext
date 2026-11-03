namespace PdxLauncher.Core.ModStudio;

using System.Globalization;

/// <summary>
/// 解析上传面板里的 Steam 工坊条目 id。
/// <para>空输入表示新建条目，非空必须是大于 0 的十进制整数。</para>
/// </summary>
public static class ModIdInput
{
    /// <summary>解析用户输入；空值合法且返回 <c>null</c>。</summary>
    public static bool TryParse(string? text, out ulong? publishedFileId)
    {
        publishedFileId = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        if (!ulong.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value == 0)
        {
            return false;
        }

        publishedFileId = value;
        return true;
    }
}
