namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 一款已安装的 Paradox 游戏。所有路径均为绝对路径；GameDataDir 已展开 %USER_DOCUMENTS%。
/// </summary>
public sealed record GameInstallation(
    string GameId,
    string DisplayName,
    string Version,
    string InstallRoot,
    string LauncherSettingsPath,
    LoadFileFormat Format,
    string GameDataDir,
    string DlcPath,
    string ModDir,
    string ExePath,
    IReadOnlyList<string> ExeArgs,
    IReadOnlyList<AlternativeExecutable> Alternatives,
    ThemeSpec Theme,
    string? BrowserDlcUrl,
    string? BrowserModUrl,
    int? SteamAppId,
    GameSupport Support)
{
    /// <summary>能识别并展示（不代表能管理内容）；等价于 <c>Support != Unsupported</c>。</summary>
    public bool IsSupported => Support != GameSupport.Unsupported;

    /// <summary>能否接管播放集 / mod / DLC / 加载文件；只有 <see cref="GameSupport.Full"/> 为 true。</summary>
    public bool SupportsContentManagement => Support == GameSupport.Full;

    /// <summary>
    /// 启动器是否需要写加载文件（dlc_load.json / content_load.json）。
    /// <para>
    /// EU5 为 false：它没有加载文件，播放集、mod 顺序、DLC 开关全部存在 <c>playsets.json</c> 里，
    /// 由游戏本体直接读取。
    /// </para>
    /// </summary>
    public bool WritesLoadFile => Format != LoadFileFormat.PlaysetsJsonV1;
}
