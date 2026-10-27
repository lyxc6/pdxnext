namespace PdxLauncher.Core.Games;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>按安装特征识别出来的一款游戏（没有 launcher-settings.json 时使用）。</summary>
/// <param name="GameId">gameId（与 launcher-settings 里的写法一致，如 <c>eu5</c>）。</param>
/// <param name="DisplayName">展示名。</param>
/// <param name="ExePath">相对安装根目录的可执行文件路径，如 <c>binaries/eu5.exe</c>。</param>
/// <param name="Support">这款游戏由特征识别时的支持等级上限。</param>
/// <param name="Content">内容管理布局；null 表示「仅启动、不接管播放集/DLC/mod」。</param>
public sealed record DetectedInstall(
    string GameId,
    string DisplayName,
    string ExePath,
    GameSupport Support,
    DetectedContentLayout? Content = null);

/// <summary>
/// 特征识别出来的游戏的内容管理布局（对应 launcher-settings.json 里的同等信息）。
/// </summary>
/// <param name="GameDataDirTemplate">
/// 文档目录模板，可含 <c>%USER_DOCUMENTS%</c>；展开后就是游戏数据目录。
/// </param>
/// <param name="DlcPathRelativeToInstallRoot">相对安装根目录的 DLC 描述文件目录，如 <c>game/dlc</c>。</param>
/// <param name="Format">内容格式；<see cref="LoadFileFormat.PlaysetsJsonV1"/> 表示不写加载文件。</param>
public sealed record DetectedContentLayout(
    string GameDataDirTemplate,
    string DlcPathRelativeToInstallRoot,
    LoadFileFormat Format);

/// <summary>
/// 给「官方安装里根本没有 launcher-settings.json」的游戏做兜底识别。
/// <para>
/// EU5（1.0 起）就是这种：安装目录只有 <c>binaries\</c>、<c>game\</c>、<c>clausewitz\</c>、<c>jomini\</c>、
/// <c>platform_specific_game_data\</c>，官方启动器靠云端配置拿到 gameId，本地没有任何元数据文件。
/// 所以这里按「可执行文件名」识别，只提供启动必需的信息（gameId / 展示名 / exe）。
/// </para>
/// <para>
/// 规则表是有序的固定列表，不做启发式猜测：识别不到就当作普通目录跳过，
/// 免得把 <c>steamapps\common</c> 里其它厂商的游戏误收进来。
/// </para>
/// </summary>
public sealed class InstallSignatureDetector
{
    private sealed record Rule(
        string GameId,
        string DisplayName,
        string RelativeExe,
        GameSupport Support,
        DetectedContentLayout? Content = null);

    /// <summary>
    /// 识别规则表。新增游戏时在这里加一条：相对 exe 路径存在即算识别成功。
    /// <para>
    /// EU5 从 1.0 起把播放集/DLC 全部收进文档目录的 <c>playsets.json</c>（游戏本体直接读，
    /// 没有 dlc_load.json/content_load.json 这一层），所以它是 Full 支持、格式为
    /// <see cref="LoadFileFormat.PlaysetsJsonV1"/>，并且要自己推导文档目录与 DLC 目录。
    /// </para>
    /// </summary>
    private static readonly Rule[] Rules =
    [
        new(
            "eu5",
            "Europa Universalis V",
            "binaries/eu5.exe",
            GameSupport.Full,
            new DetectedContentLayout(
                "%USER_DOCUMENTS%/Paradox Interactive/Europa Universalis V",
                "game/dlc",
                LoadFileFormat.PlaysetsJsonV1)),
    ];

    private readonly IFileSystem _fileSystem;

    public InstallSignatureDetector(IFileSystem fileSystem)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        _fileSystem = fileSystem;
    }

    /// <summary>按安装特征识别；识别不到返回 null。</summary>
    public DetectedInstall? Detect(string installRoot)
    {
        if (string.IsNullOrWhiteSpace(installRoot))
        {
            return null;
        }

        foreach (var rule in Rules)
        {
            var exePath = _fileSystem.Combine(installRoot, rule.RelativeExe);
            if (_fileSystem.FileExists(exePath))
            {
                return new DetectedInstall(
                    rule.GameId,
                    rule.DisplayName,
                    rule.RelativeExe,
                    rule.Support,
                    rule.Content);
            }
        }

        return null;
    }
}
