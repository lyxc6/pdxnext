namespace PdxLauncher.Core.Configuration;

using PdxLauncher.Core.Abstractions;

/// <summary>判定出的安装形态。</summary>
public enum InstallModeKind
{
    /// <summary>没有可信的安装证据，按绿色/便携副本运行。</summary>
    PortableRun,

    /// <summary>命中 Inno Setup 卸载项且主程序仍在，属于正式安装。</summary>
    InstalledInno,
}

/// <summary>安装范围，对应卸载项所在的注册表根。</summary>
public enum InstallScope
{
    /// <summary>HKCU 下的卸载项：安装者不需要管理员权限。</summary>
    PerUser,

    /// <summary>HKLM 下的卸载项：安装时提权，对所有用户可见。</summary>
    PerMachine,
}

/// <summary>判定依据；既用于「已安装」的匹配条件，也用于「不算已装」的具体原因。</summary>
public enum InstallModeReason
{
    /// <summary>卸载项的 <c>Inno Setup: App Path</c> 与程序目录归一化后一致，且主程序存在。</summary>
    MatchedInstallDir,

    /// <summary>找到了卸载项，但 App Path 指向别的目录（典型是卸载残留或绿色拷贝）。</summary>
    PathMismatch,

    /// <summary>卸载项路径指向这里，但主程序文件不在（卸载残留、文件被清理或隔离）。</summary>
    ExeMissing,

    /// <summary>四个注册表视图里都没有本程序的卸载项。</summary>
    NoEntry,

    /// <summary>只有 install-mode.txt，没有可匹配的卸载项；marker 不能单独证明已安装。</summary>
    MarkerOnly,
}

/// <summary>
/// 安装模式判定结果。
/// <para>
/// 三条证据必须同时成立才判 <see cref="InstallModeKind.InstalledInno"/>：卸载项的 App Path
/// 归一化后等于程序目录、该卸载项来自 HKCU（PerUser）或 HKLM（PerMachine）、程序目录下
/// <see cref="PdxIdentity.AppExecutableName"/> 存在。marker 只作为佐证（Scope 冲突时做平票取舍），
/// 本身绝不构成「已安装」。其余情况一律 <see cref="InstallModeKind.PortableRun"/>，
/// 并让 <see cref="Reason"/> 说明差在哪条证据。
/// </para>
/// </summary>
public sealed record InstallMode(
    InstallModeKind Kind,
    InstallModeReason Reason,
    InstallScope? Scope = null,
    string? InstallDirectory = null,
    string? DisplayVersion = null,
    UninstallRegistryView? RegistryView = null)
{
    /// <summary>只有 <see cref="InstallModeKind.InstalledInno"/> 才算已安装；残留条目为 false。</summary>
    public bool IsInstalled => Kind == InstallModeKind.InstalledInno;

    /// <summary>构造「已安装」结果。</summary>
    public static InstallMode Installed(
        InstallScope scope,
        string installDirectory,
        string? displayVersion,
        UninstallRegistryView? registryView) =>
        new(
            InstallModeKind.InstalledInno,
            InstallModeReason.MatchedInstallDir,
            scope,
            installDirectory,
            displayVersion,
            registryView);

    /// <summary>
    /// 构造「便携运行」结果。残留场景可以带上条目的 Scope / 版本，供界面或日志解释，
    /// 但 <see cref="IsInstalled"/> 仍为 false。
    /// </summary>
    public static InstallMode PortableRun(
        InstallModeReason reason,
        InstallScope? scope = null,
        string? installDirectory = null,
        string? displayVersion = null,
        UninstallRegistryView? registryView = null) =>
        new(InstallModeKind.PortableRun, reason, scope, installDirectory, displayVersion, registryView);
}
