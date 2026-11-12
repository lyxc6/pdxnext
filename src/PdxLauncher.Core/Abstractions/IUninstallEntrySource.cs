namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// 卸载项的读取入口。实现可以碰注册表；判定逻辑只吃返回的 DTO，因此可单测。
/// </summary>
public interface IUninstallEntrySource
{
    /// <summary>
    /// 读取所有可访问视图下的卸载项。读不到、权限不足或键损坏一律按「空列表」返回，
    /// 不允许把异常带到启动流程。
    /// </summary>
    IReadOnlyList<UninstallEntry> ReadEntries();
}
