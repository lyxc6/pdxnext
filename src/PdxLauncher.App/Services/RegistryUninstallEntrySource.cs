namespace PdxLauncher.App.Services;

using Microsoft.Win32;
using PdxLauncher.Core;
using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;

/// <summary>
/// 从注册表读取本程序的 Inno Setup 卸载项。全项目只有这一处碰注册表。
/// <para>
/// 读 HKCU / HKLM × 64 / 32 位视图四个组合：用户自选目录的安装只靠卸载项里的
/// <c>Inno Setup: App Path</c> 才找得到，猜默认目录（<c>%LOCALAPPDATA%\Programs</c> 或
/// Program Files）会把装在 D 盘的情况直接漏掉。
/// </para>
/// <para>
/// 注册表读取处在启动路径上：权限、视图缺失、键损坏都可能抛异常，任何一个组合失败都
/// 只等同于「该视图没有条目」，绝不让异常冒到启动流程。
/// </para>
/// </summary>
public sealed class RegistryUninstallEntrySource : IUninstallEntrySource
{
    private const string AppPathValueName = "Inno Setup: App Path";
    private const string DisplayVersionValueName = "DisplayVersion";
    private const string InstallLocationValueName = "InstallLocation";

    private static readonly (RegistryHive Hive, InstallScope Scope, UninstallRegistryView View)[] Locations =
    [
        (RegistryHive.CurrentUser, InstallScope.PerUser, UninstallRegistryView.Registry64),
        (RegistryHive.CurrentUser, InstallScope.PerUser, UninstallRegistryView.Registry32),
        (RegistryHive.LocalMachine, InstallScope.PerMachine, UninstallRegistryView.Registry64),
        (RegistryHive.LocalMachine, InstallScope.PerMachine, UninstallRegistryView.Registry32),
    ];

    private readonly IAppLogger? _logger;

    public RegistryUninstallEntrySource(IAppLogger? logger = null)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<UninstallEntry> ReadEntries()
    {
        var entries = new List<UninstallEntry>(Locations.Length);
        foreach (var location in Locations)
        {
            TryRead(location.Hive, location.Scope, location.View, entries);
        }

        return entries;
    }

    private void TryRead(
        RegistryHive hive,
        InstallScope scope,
        UninstallRegistryView view,
        List<UninstallEntry> entries)
    {
        try
        {
            using var baseKey = RegistryKey.OpenBaseKey(hive, ToRegistryView(view));
            using var key = baseKey.OpenSubKey(PdxIdentity.UninstallKeyName, writable: false);
            if (key is null)
            {
                return;
            }

            var appPath = key.GetValue(AppPathValueName) as string;
            var displayVersion = key.GetValue(DisplayVersionValueName) as string;
            var installLocation = key.GetValue(InstallLocationValueName) as string;
            entries.Add(new UninstallEntry(scope, appPath, displayVersion, installLocation, view));
        }
        catch (Exception ex)
        {
            // 这里只处理并继续：日志用于排查，返回值仍是「该视图没有条目」。
            _logger?.Warn("读取卸载项失败（" + hive + "/" + view + "）：" + ex.Message);
        }
    }

    private static RegistryView ToRegistryView(UninstallRegistryView view) => view switch
    {
        UninstallRegistryView.Registry32 => RegistryView.Registry32,
        _ => RegistryView.Registry64,
    };
}
