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
}
