namespace PdxLauncher.Core;

/// <summary>
/// 程序身份常量。安装脚本、卸载项与运行时的「是否已安装」判定必须共用这一份，
/// 三处各写一份 GUID 正是版本升级链断掉的常见原因。
/// </summary>
public static class PdxIdentity
{
    /// <summary>Inno Setup 的 AppId（[Setup] 里的字面量，单层大括号；.iss 里写成 {{ 转义）。</summary>
    public const string AppId = "{A2E3D5E1-5133-46BA-A846-1388A878E1A7}";

    /// <summary>主程序文件名；「exe 仍在」这条证据检查的就是它。</summary>
    public const string AppExecutableName = "PdxLauncher.App.exe";

    /// <summary>
    /// Inno 卸载键（相对于 HKCU/HKLM 的路径）。后缀 _is1 由 Inno 固定追加；
    /// installer/PdxNext.iss 与 tools/verify-installed-version.ps1 必须与它一致。
    /// </summary>
    public const string UninstallKeyName =
        "Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall\\" + AppId + "_is1";
}
