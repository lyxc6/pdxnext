namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 启动器对一款已安装游戏的支持等级。UI 与业务逻辑一律以它为准，
/// 不要再在别处用零散的 gameId 白名单判断"这个游戏能不能管"。
/// </summary>
public enum GameSupport
{
    /// <summary>
    /// 能识别、能显示，但未适配：不保证启动参数与加载文件正确，卡片上标「未适配」。
    /// </summary>
    Unsupported = 0,

    /// <summary>
    /// 仅启动：启动、主题、版本可用；播放集 / mod / DLC 交给游戏本体管理，
    /// 启动器不读它的 sqlite、不写加载文件、不扫 DLC 目录。
    /// Prison Architect 属于这一级（本体不读 content_load.json，也没有 launcher-v2.sqlite）。
    /// </summary>
    LaunchOnly = 1,

    /// <summary>
    /// 完整适配：播放集 / mod / DLC / 加载文件全部由启动器接管。
    /// </summary>
    Full = 2,
}
