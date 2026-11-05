namespace PdxLauncher.Core.Saves;

/// <summary>存档落点所属的系统目录根，供备份层判断该目录是否会随环境变化。</summary>
public enum SaveLocationSource
{
    /// <summary><c>Documents\Paradox Interactive</c> 下。</summary>
    Documents = 0,

    /// <summary><c>%LOCALAPPDATA%</c> 下。</summary>
    LocalAppData = 1,

    /// <summary><c>%USERPROFILE%\AppData\LocalLow</c> 下。</summary>
    LocalAppDataLow = 2,
}

/// <summary>
/// 某个游戏登记过的存档落点及其当前是否存在。
/// <para>与 <see cref="SaveLocation"/> 不同，本类型保留「目录不存在」的状态，
/// 备份/恢复层据此决定是等待目录出现，还是直接报「该游戏没有存档目录」。</para>
/// </summary>
/// <param name="GameId">归一后的游戏 id。</param>
/// <param name="Directory">登记的目标目录（不保证存在）。</param>
/// <param name="Exists">该目录当前是否存在。</param>
/// <param name="Source">目录所属的系统根。</param>
public sealed record SaveLocationInfo(
    string GameId,
    string Directory,
    bool Exists,
    SaveLocationSource Source);
