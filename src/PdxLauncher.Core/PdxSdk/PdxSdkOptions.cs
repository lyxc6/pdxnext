namespace PdxLauncher.Core.PdxSdk;

using PdxLauncher.Core.Accounts;
using PdxLauncher.Core.Games;

/// <summary>
/// 建一个 <see cref="PdxSdkHost"/> 所需的全部输入。
/// <para>
/// <see cref="DiskIORoot"/> 是 SDK 自己的状态目录（<c>database.json</c>、缓存都写这里），
/// <see cref="ModsRootPath"/> 必须指向游戏自己的 <c>.cache\Mods</c>，否则 SDK 写出的播放集
/// 与游戏/Skyve 不互通。
/// </para>
/// </summary>
/// <param name="PlatformGameId">SDK 的 <c>namespace</c>，如 <c>cities_skylines_2</c>。</param>
/// <param name="ModsRootPath">游戏 <c>.cache\Mods</c> 的绝对路径。</param>
/// <param name="DiskIORoot">SDK 状态目录（通常是 <c>&lt;dataRoot&gt;\pdxsdk\&lt;userId&gt;</c>）。</param>
/// <param name="UserId">当前 Paradox 账号 id；匿名浏览时可空。</param>
/// <param name="Session">已有会话；用于写入 <c>database.json</c> 复用登录态。</param>
/// <param name="GameVersion">游戏版本号，SDK 会随请求带上。</param>
public sealed record PdxSdkOptions(
    string PlatformGameId,
    string ModsRootPath,
    string DiskIORoot,
    string UserId,
    PdxSession? Session,
    string GameVersion = "")
{
    /// <summary>
    /// 按「数据根 + 账号 id + 游戏目录」算出该游戏的 SDK 状态目录：
    /// <c>&lt;dataRoot&gt;\pdxsdk\&lt;userId&gt;</c>。
    /// 账号 id 为空时用 <c>anonymous</c>，避免拼出根目录本身。
    /// </summary>
    /// <param name="dataRoot">本启动器的数据根。</param>
    /// <param name="userId">Paradox 账号 id。</param>
    public static string GetDiskIoRoot(string dataRoot, string? userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
        var folder = string.IsNullOrWhiteSpace(userId) ? "anonymous" : userId.Trim();
        return Path.Combine(dataRoot, "pdxsdk", folder);
    }
}
