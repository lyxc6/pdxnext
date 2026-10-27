namespace PdxLauncher.Core.Contracts;

/// <summary>主题素材的来源层，用于设置页展示与降级判断。</summary>
public enum ThemeAssetSource
{
    /// <summary>内置占位图，断网且安装目录无图时使用。</summary>
    BuiltInPlaceholder = 0,

    /// <summary>游戏安装目录 launcher/assets 下的原始素材。</summary>
    InstallDir = 1,

    /// <summary>联网下载到本地缓存的素材（最新）。</summary>
    OnlineCache = 2,

    /// <summary>
    /// Steam 官方胶囊图（header.jpg）兜底：游戏既没有 launcher-settings.json
    /// （如 EU5）也没有任何主题素材时，用 Steam CDN 的商店头图当封面。
    /// </summary>
    SteamCapsule = 3,

    /// <summary>
    /// 我们自带的官方美术（随程序发布，只用于安装目录完全没有主题素材的游戏，目前只有 EU5）。
    /// 层级上等价于「层 2.5」：比内置占位图优先，仍让位给安装目录素材与联网缓存。
    /// </summary>
    Bundled = 4,
}
