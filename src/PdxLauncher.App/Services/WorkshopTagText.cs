namespace PdxLauncher.App.Services;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Online;

/// <summary>
/// Steam 创意工坊的标签展示名：按当前界面语言取文案。
/// <para>
/// 只翻译展示名，发给 Steam 的标签机器名保持不变；未收录的标签原样显示英文，
/// 这样 Steam 新增标签时不会变成空白。
/// </para>
/// </summary>
public static class WorkshopTagText
{
    /// <summary>取标签的本地化展示名。</summary>
    /// <param name="tag">发给 Steam 的机器名（或 DLC appid）。</param>
    /// <param name="displayName">Steam 给的展示名；机器名查不到时再用它查。</param>
    public static string Localize(string? tag, string? displayName = null)
    {
        var key = WorkshopTagKeys.KeyFor(tag) ?? WorkshopTagKeys.KeyFor(displayName);
        if (key is not null)
        {
            return Localizer.Instance.T(key);
        }

        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return tag ?? string.Empty;
    }
}
