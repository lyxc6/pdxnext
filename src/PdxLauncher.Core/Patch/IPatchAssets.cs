namespace PdxLauncher.Core.Patch;

/// <summary>
/// 补丁素材来源：随启动器发布的内嵌资源（默认）+ 可选的用户覆盖目录（<c>data\patch\assets\</c>）。
/// <para>
/// 覆盖目录里的同名文件优先：整合包更新后只要把新的 loader / ini 丢进目录即可，
/// 不需要重新编译启动器。
/// </para>
/// </summary>
public interface IPatchAssets
{
    /// <summary>外部覆盖目录的绝对路径（不存在也可以，界面「打开素材目录」用它）。</summary>
    string AssetsDirectory { get; }

    /// <summary>是否有文件实际来自外部覆盖目录。</summary>
    bool UsesExternalAssets { get; }

    /// <summary>取对应架构的 loader 字节；内置资源也缺失时返回 false。</summary>
    bool TryGetLoader(bool x64, out byte[] data);

    /// <summary>取 <c>cream_api.ini</c> 模板文本；内置资源也缺失时返回 false。</summary>
    bool TryGetIniTemplate(out string text);

    /// <summary>
    /// 取按游戏分节的 DLC 源清单（<c>dlc-catalog.json</c>）；外部覆盖目录优先，内置资源也缺失时返回 false。
    /// </summary>
    bool TryGetDlcCatalog(out PatchDlcCatalog catalog);
}
