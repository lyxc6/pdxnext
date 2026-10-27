namespace PdxLauncher.Core.Contracts;

/// <summary>启动器写入的加载文件格式，由 launcher-settings.json 的 formatVersion 决定。</summary>
public enum LoadFileFormat
{
    /// <summary>formatVersion 0：dlc_load.json {disabled_dlcs, enabled_mods}。</summary>
    DlcLoadV0 = 0,

    /// <summary>formatVersion 1：content_load.json {disabledDLC, enabledMods, enabledUGC}。</summary>
    ContentLoadV1 = 1,

    /// <summary>formatVersion 1.1：content_load.json，enabledMods 按 dir/archive 分组。</summary>
    ContentLoadV1_1 = 11,

    /// <summary>
    /// EU5（1.0 起）：没有加载文件。游戏本体直接读文档目录的 <c>playsets.json</c>，
    /// 启动器要做的就是维护这个文件，因此 <see cref="GameInstallation.WritesLoadFile"/> 为 false。
    /// </summary>
    PlaysetsJsonV1 = 2,
}
