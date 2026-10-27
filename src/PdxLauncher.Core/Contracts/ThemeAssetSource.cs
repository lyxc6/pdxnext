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
}
