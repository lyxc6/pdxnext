namespace PdxLauncher.Core.Playsets.PlaysetConfig;

using PdxLauncher.Core.Contracts;

/// <summary>
/// CS2 的 mod 状态文件路径常量与解析。
/// <para>
/// 全部集中在 <c>{gameDataDir}\.cache\Mods\</c> 下（真机实测：
/// <c>%USERPROFILE%\AppData\LocalLow\Colossal Order\Cities Skylines II\.cache\Mods</c>）：
/// </para>
/// <list type="bullet">
/// <item><c>playset_config.json</c> —— 播放集与 mod 启用状态（权威来源，游戏本体直接读）。</item>
/// <item><c>playset_config_lock.json</c> —— 官方启动器的写锁，存在且未过期时我们不许写。</item>
/// <item><c>playset_sync.json</c> —— 本地播放集 id ↔ 后端数字播放集 id 的映射。</item>
/// <item><c>pdx_mods_cache.json</c> —— 已安装 mod 的元数据缓存（显示名、版本、封面、依赖）。</item>
/// <item><c>mod_directory.json</c> —— mod 根目录登记（pdx_mods / local）。</item>
/// <item><c>pdx_mods\&lt;modId&gt;_&lt;version&gt;\</c> —— 已落地的 mod 本体。</item>
/// </list>
/// </summary>
public static class PlaysetConfigPaths
{
    /// <summary>播放集配置文件。</summary>
    public const string FileName = "playset_config.json";

    /// <summary>官方启动器写锁文件。</summary>
    public const string LockFileName = "playset_config_lock.json";

    /// <summary>播放集同步映射文件。</summary>
    public const string SyncFileName = "playset_sync.json";

    /// <summary>已安装 mod 的元数据缓存。</summary>
    public const string ModsCacheFileName = "pdx_mods_cache.json";

    /// <summary>mod 根目录登记文件。</summary>
    public const string ModDirectoryFileName = "mod_directory.json";

    /// <summary>已下载 mod 的落地目录名（相对 mods 根）。</summary>
    public const string PdxModsFolderName = "pdx_mods";

    /// <summary>mod 状态目录：<c>{gameDataDir}\.cache\Mods</c>。</summary>
    public static string GetDirectory(GameInstallation game)
    {
        ArgumentNullException.ThrowIfNull(game);
        return Path.Combine(game.GameDataDir, ".cache", "Mods");
    }

    /// <summary><c>playset_config.json</c> 的绝对路径。</summary>
    public static string GetConfigPath(GameInstallation game) =>
        Path.Combine(GetDirectory(game), FileName);

    /// <summary><c>playset_config_lock.json</c> 的绝对路径。</summary>
    public static string GetLockPath(GameInstallation game) =>
        Path.Combine(GetDirectory(game), LockFileName);

    /// <summary><c>playset_sync.json</c> 的绝对路径。</summary>
    public static string GetSyncPath(GameInstallation game) =>
        Path.Combine(GetDirectory(game), SyncFileName);

    /// <summary><c>pdx_mods_cache.json</c> 的绝对路径。</summary>
    public static string GetModsCachePath(GameInstallation game) =>
        Path.Combine(GetDirectory(game), ModsCacheFileName);

    /// <summary>已下载 mod 的根目录：优先用 <c>mod_directory.json</c> 登记的路径，回落到 <c>mods 根\pdx_mods</c>。</summary>
    public static string GetPdxModsRoot(GameInstallation game) =>
        Path.Combine(GetDirectory(game), PdxModsFolderName);
}
