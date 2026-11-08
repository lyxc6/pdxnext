namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 状态徽标判定（<see cref="WorkshopStateText"/>）的自检：档位优先顺序、百分比、边界。
/// </summary>
public static class WorkshopStateTextChecks
{
    /// <summary>运行本组检查。</summary>
    /// <param name="fileSystem">文件系统（本组不用，参数保留以统一入口）。</param>
    /// <returns>0 表示全部通过。</returns>
    public static int Run(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);

        var before = ChecksContext.Failures;

        try
        {
            CheckUnknown();
            CheckPriority();
            CheckPercent();
            CheckUpdateHint();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("状态徽标：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static void CheckUnknown()
    {
        var none = WorkshopStateText.Resolve(null);
        ChecksContext.Equal("徽标：null → None", WorkshopStateBadge.None, none.Badge);
        ChecksContext.Equal("徽标：None 不显示", false, none.IsVisible);

        var unknown = WorkshopStateText.Resolve(WorkshopInstallState.Unknown);
        ChecksContext.Equal("徽标：Unknown → None", WorkshopStateBadge.None, unknown.Badge);
    }

    private static void CheckPriority()
    {
        // 本地禁用压过一切：用户主动干预比「还在下载」更该先说。
        var disabledAndDownloading = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: true,
            AcfPath: null,
            NeedsUpdate: true,
            IsDisabledLocally: true);

        ChecksContext.Equal(
            "徽标：禁用 + 下载中 → 先报禁用",
            WorkshopStateBadge.DisabledLocally,
            WorkshopStateText.Resolve(disabledAndDownloading).Badge);

        // 下载中压过「需要更新」：正在下就是在解决更新。
        var downloadingAndUpdate = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: true,
            AcfPath: null,
            NeedsUpdate: true);

        ChecksContext.Equal(
            "徽标：下载中 + 需要更新 → 先报下载中",
            WorkshopStateBadge.Downloading,
            WorkshopStateText.Resolve(downloadingAndUpdate).Badge);

        // 排队中压过「需要更新」但输给「下载中」。
        var pending = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: false,
            IsDownloading: true,
            AcfPath: null,
            NeedsUpdate: true,
            IsDownloadPending: true);

        ChecksContext.Equal(
            "徽标：排队 + 需要更新 → 先报排队",
            WorkshopStateBadge.DownloadPending,
            WorkshopStateText.Resolve(pending).Badge);

        // 需要更新必须在已安装之前，否则用户永远看不到有更新。
        var update = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: false,
            AcfPath: null,
            NeedsUpdate: true);

        ChecksContext.Equal(
            "徽标：需要更新 + 已安装 → 报需要更新",
            WorkshopStateBadge.UpdateNeeded,
            WorkshopStateText.Resolve(update).Badge);

        var installed = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: false,
            AcfPath: null);

        ChecksContext.Equal("徽标：已安装", WorkshopStateBadge.Installed, WorkshopStateText.Resolve(installed).Badge);

        // 订阅了但还没装：不显示徽标，别把常态渲染成提示。
        var subscribedOnly = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: false,
            IsDownloading: false,
            AcfPath: null);

        ChecksContext.Equal(
            "徽标：只订阅未安装 → 不显示",
            WorkshopStateBadge.None,
            WorkshopStateText.Resolve(subscribedOnly).Badge);
    }

    private static void CheckPercent()
    {
        var half = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: false,
            IsDownloading: true,
            AcfPath: null,
            BytesDownloaded: 50,
            BytesTotal: 200);

        var info = WorkshopStateText.Resolve(half);
        ChecksContext.Equal("徽标：进度 50/200 = 25", 25, info.Percent);
        ChecksContext.Equal("徽标：下载中键名", WorkshopStateText.KeyDownloading, info.Key);
        ChecksContext.Equal("徽标：下载中带回已下载字节", (ulong?)50, info.BytesDownloaded);
        ChecksContext.Equal("徽标：下载中带回总字节", (ulong?)200, info.BytesTotal);

        var noTotal = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: false,
            IsDownloading: true,
            AcfPath: null,
            BytesDownloaded: 50,
            BytesTotal: 0);

        ChecksContext.Equal("徽标：总量 0 → 无百分比", null, WorkshopStateText.Resolve(noTotal).Percent);
        ChecksContext.Equal("徽标：总量 0 时字节数仍原样带回", (ulong?)0, WorkshopStateText.Resolve(noTotal).BytesTotal);
    }

    private static void CheckUpdateHint()
    {
        var installedOutdated = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: true,
            IsDownloading: false,
            AcfPath: null,
            NeedsUpdate: true);

        ChecksContext.Check("更新提示：已安装且落后", WorkshopStateText.ShowsUpdateHint(installedOutdated));

        var notInstalled = new WorkshopInstallState(
            IsSubscribed: true,
            IsInstalled: false,
            IsDownloading: false,
            AcfPath: null,
            NeedsUpdate: true);

        ChecksContext.Check("更新提示：没安装就不提示", !WorkshopStateText.ShowsUpdateHint(notInstalled));
        ChecksContext.Check("更新提示：null 不提示", !WorkshopStateText.ShowsUpdateHint(null));
    }
}
