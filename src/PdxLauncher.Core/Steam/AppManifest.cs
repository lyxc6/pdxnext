namespace PdxLauncher.Core.Steam;

/// <summary>
/// <c>steamapps\appmanifest_&lt;appid&gt;.acf</c> 的关键字段。
/// <see cref="InstallPath"/> = &lt;library&gt;\steamapps\common\&lt;InstallDir&gt;。
/// </summary>
public sealed record AppManifest(
    int AppId,
    string Name,
    string InstallDir,
    string InstallPath,
    int StateFlags,
    long SizeOnDisk)
{
    /// <summary>StateFlags 的 bit 2（值 4）表示"已完整安装"。Steam 用 4 表示 Installed。</summary>
    public bool IsInstalled => (StateFlags & 4) == 4;
}
