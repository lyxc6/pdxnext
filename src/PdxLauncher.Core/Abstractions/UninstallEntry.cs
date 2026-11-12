namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Configuration;

/// <summary>卸载项是从哪个注册表视图读到的；WOW6432Node 对应 Registry32。</summary>
public enum UninstallRegistryView
{
    /// <summary>64 位视图（Inno Setup 6 在 64 位系统上默认写这里）。</summary>
    Registry64,

    /// <summary>32 位视图（32 位安装器 / WOW6432Node）。</summary>
    Registry32,
}

/// <summary>
/// Inno Setup 卸载项里与「是否已安装」相关的字段。只保留判定需要的部分，不塞无关项。
/// </summary>
public sealed record UninstallEntry(
    InstallScope Scope,
    string? AppPath,
    string? DisplayVersion = null,
    string? InstallLocation = null,
    UninstallRegistryView RegistryView = UninstallRegistryView.Registry64);
