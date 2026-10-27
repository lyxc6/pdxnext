namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Contracts;

/// <summary>
/// <c>launcher-settings.json</c> 解析后的中间结果。所有路径均已展开/绝对化：
/// <see cref="ExePath"/>、<see cref="DlcPath"/>、<see cref="ModDir"/>、<see cref="GameDataDir"/>
/// 都是绝对路径。<see cref="GameId"/> 保持原始大小写，比较时请忽略大小写。
/// </summary>
public sealed record LauncherSettings(
    string GameId,
    string DisplayName,
    string Version,
    string RawVersion,
    double FormatVersion,
    LoadFileFormat Format,
    string GameDataDir,
    string DlcPath,
    string ModDir,
    string ExePath,
    IReadOnlyList<string> ExeArgs,
    IReadOnlyList<AlternativeExecutable> Alternatives,
    string? BrowserDlcUrl,
    string? BrowserModUrl,
    string? ThemeFile,
    string LauncherSettingsPath,
    string InstallRoot,
    bool IsLooseFilesModSupported);
