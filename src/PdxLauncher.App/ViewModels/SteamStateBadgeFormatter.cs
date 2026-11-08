namespace PdxLauncher.App.ViewModels;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Steam;

/// <summary>
/// 「Steam 状态徽标」的文案拼装，Mod 库行与创意工坊列表行共用一份。
/// <para>
/// 档位判定在 Core 的 <see cref="WorkshopStateText"/>（可被自检覆盖），这里只负责把
/// 百分比与字节数填进文案：下载中能拿到字节时显示「N% · 已下载 x / y」，
/// 只知道在下载、拿不到总量时退回不带数字的短文案。
/// </para>
/// </summary>
public static class SteamStateBadgeFormatter
{
    /// <summary>这一行要不要显示徽标。</summary>
    /// <param name="state">Steam 状态；null 表示没有。</param>
    /// <returns>是否显示。</returns>
    public static bool IsVisible(WorkshopInstallState? state) => WorkshopStateText.Resolve(state).IsVisible;

    /// <summary>徽标文案；没有可显示的状态时为空串。</summary>
    /// <param name="state">Steam 状态。</param>
    /// <returns>文案。</returns>
    public static string Build(WorkshopInstallState? state)
    {
        var info = WorkshopStateText.Resolve(state);
        return info.Badge switch
        {
            WorkshopStateBadge.None => string.Empty,
            WorkshopStateBadge.Downloading when info.Percent is { } percent => BuildDownloading(info, percent),
            WorkshopStateBadge.Downloading => T(WorkshopStateText.KeyDownloadingNoPercent),
            _ => T(info.Key),
        };
    }

    /// <summary>把字节数写成和列表体积列一致的风格（B / KB / MB / GB，去尾零）。</summary>
    /// <param name="bytes">字节数。</param>
    /// <returns>可读文本。</returns>
    public static string FormatBytes(ulong bytes) => ModSizeText.Format((long)Math.Min(bytes, long.MaxValue));

    private static string BuildDownloading(WorkshopStateBadgeInfo info, int percent)
    {
        if (info is { BytesDownloaded: { } done, BytesTotal: { } total } && total > 0)
        {
            return T(WorkshopStateText.KeyDownloadingWithBytes, percent, FormatBytes(done), FormatBytes(total));
        }

        return T(info.Key, percent);
    }

    private static string T(string key, params object?[] args) => Localizer.Instance.T(key, args);
}
