namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Steam;

/// <summary>
/// 工坊条目状态位映射（<see cref="WorkshopItemStateBits"/>）的自检。
/// <para>
/// 位运算写错不会抛异常，只会让「需要更新」显示成「已安装」这类错误，只能靠断言钉住。
/// 位值来源：<c>sdk/public/steam/isteamugc.h</c> 的 <c>EItemState</c>。
/// </para>
/// </summary>
public static class WorkshopItemStateBitsChecks
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
            CheckBitValues();
            CheckToInstallState();
            CheckPendingIsNotDownloading();
            CheckHelpers();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            ChecksContext.Check("状态位：未抛出异常", false, ex.Message);
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    /// <summary>位值必须与 SDK 的 EItemState 一致：错一位整列状态都会串。</summary>
    private static void CheckBitValues()
    {
        ChecksContext.Equal("状态位：Subscribed", 0x0001u, WorkshopItemStateBits.Subscribed);
        ChecksContext.Equal("状态位：LegacyItem", 0x0002u, WorkshopItemStateBits.LegacyItem);
        ChecksContext.Equal("状态位：Installed", 0x0004u, WorkshopItemStateBits.Installed);
        ChecksContext.Equal("状态位：NeedsUpdate", 0x0008u, WorkshopItemStateBits.NeedsUpdate);
        ChecksContext.Equal("状态位：Downloading", 0x0010u, WorkshopItemStateBits.Downloading);
        ChecksContext.Equal("状态位：DownloadPending", 0x0020u, WorkshopItemStateBits.DownloadPending);
        ChecksContext.Equal("状态位：DisabledLocally", 0x0040u, WorkshopItemStateBits.DisabledLocally);
    }

    private static void CheckToInstallState()
    {
        var subscribed = WorkshopItemStateBits.ToInstallState(WorkshopItemStateBits.Subscribed);
        ChecksContext.Equal("映射：只订阅 → IsSubscribed", true, subscribed.IsSubscribed);
        ChecksContext.Equal("映射：只订阅 → 未安装", false, subscribed.IsInstalled);
        ChecksContext.Equal("映射：只订阅 → 没在下载", false, subscribed.IsDownloading);
        ChecksContext.Equal("映射：只订阅 → 无需更新", false, subscribed.NeedsUpdate);
        ChecksContext.Equal("映射：只订阅 → 未本地禁用", false, subscribed.IsDisabledLocally);

        var installed = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Subscribed | WorkshopItemStateBits.Installed,
            sizeOnDisk: 2048,
            folder: "1234567");

        ChecksContext.Equal("映射：已安装 → IsInstalled", true, installed.IsInstalled);
        ChecksContext.Equal("映射：已安装 → 磁盘占用透传", (ulong?)2048, installed.SizeOnDisk);
        ChecksContext.Equal("映射：已安装 → 目录透传", "1234567", installed.Folder);
        ChecksContext.Equal("映射：已安装 → 未在下载", false, installed.IsDownloading);

        var update = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Subscribed | WorkshopItemStateBits.Installed | WorkshopItemStateBits.NeedsUpdate);

        ChecksContext.Equal("映射：需要更新 → NeedsUpdate", true, update.NeedsUpdate);
        ChecksContext.Equal("映射：需要更新 → 仍然算已安装", true, update.IsInstalled);

        var downloading = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Subscribed | WorkshopItemStateBits.Downloading,
            bytesDownloaded: 30,
            bytesTotal: 120);

        ChecksContext.Equal("映射：下载中 → IsDownloading", true, downloading.IsDownloading);
        ChecksContext.Equal("映射：下载中 → 需要更新位不误报", false, downloading.NeedsUpdate);
        ChecksContext.Equal("映射：下载进度 30/120 = 25%", 25d, downloading.DownloadPercent);

        var disabled = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Subscribed | WorkshopItemStateBits.Installed | WorkshopItemStateBits.DisabledLocally);

        ChecksContext.Equal("映射：本地禁用 → IsDisabledLocally", true, disabled.IsDisabledLocally);
        ChecksContext.Equal("映射：本地禁用 → 仍然算已安装", true, disabled.IsInstalled);

        // 总量为 0（Steam 还没给出大小）时不能算 0%，否则界面会闪一下「下载中 0%」。
        var noTotal = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Downloading,
            bytesDownloaded: 10,
            bytesTotal: 0);

        ChecksContext.Equal("映射：总量为 0 → 进度未知", null, noTotal.DownloadPercent);

        // raw == 0 只代表「Steam 还没认出」，不能当成「确定未订阅」——
        // 这一条由 ReadStateDetail 负责兜底（返回 Unknown），这里只钉位映射本身。
        var zero = WorkshopItemStateBits.ToInstallState(0);
        ChecksContext.Equal("映射：raw=0 → 订阅位为 false", false, zero.IsSubscribed);
    }

    private static void CheckPendingIsNotDownloading()
    {
        var pending = WorkshopItemStateBits.ToInstallState(WorkshopItemStateBits.DownloadPending);
        ChecksContext.Equal("排队中：IsDownloadPending", true, pending.IsDownloadPending);
        ChecksContext.Equal("排队中：也算 IsDownloading（整体口径）", true, pending.IsDownloading);

        var both = WorkshopItemStateBits.ToInstallState(
            WorkshopItemStateBits.Downloading | WorkshopItemStateBits.DownloadPending);
        ChecksContext.Equal("既在下载又在排队 → 不算纯排队", false, both.IsDownloadPending);

        var downloading = WorkshopItemStateBits.ToInstallState(WorkshopItemStateBits.Downloading);
        ChecksContext.Equal("只下载不排队 → 不算排队", false, downloading.IsDownloadPending);
    }

    private static void CheckHelpers()
    {
        ChecksContext.Check(
            "HasUpdate：需要更新位命中",
            WorkshopItemStateBits.HasUpdate(WorkshopItemStateBits.NeedsUpdate));

        ChecksContext.Check(
            "HasUpdate：已安装不算需要更新",
            !WorkshopItemStateBits.HasUpdate(WorkshopItemStateBits.Installed));

        ChecksContext.Check(
            "IsDisabled：本地禁用位命中",
            WorkshopItemStateBits.IsDisabled(WorkshopItemStateBits.DisabledLocally));

        ChecksContext.Check(
            "IsDisabled：订阅不算本地禁用",
            !WorkshopItemStateBits.IsDisabled(WorkshopItemStateBits.Subscribed));
    }
}
