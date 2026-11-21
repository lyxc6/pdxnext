namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 用 .NET 内置 API 读取系统快照：目前只统计游戏数据目录与安装目录所在盘的可用空间。
/// 页面文件与显卡驱动日期需要 WMI，当前项目没有对应依赖，因此保持「未知」，
/// 取证逻辑对未知字段一律跳过，不会误报。
/// </summary>
public sealed class PcSystemSnapshotReader : IPcSystemSnapshotReader
{
    /// <inheritdoc />
    public PcSystemSnapshot Read(string? dataDirectory, string? gameDirectory)
    {
        return new PcSystemSnapshot(
            FreeBytes(dataDirectory),
            FreeBytes(gameDirectory),
            PageFileEnabled: null,
            GraphicsDriverDate: null);
    }

    private static long? FreeBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var root = System.IO.Path.GetPathRoot(path);
            if (string.IsNullOrWhiteSpace(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
