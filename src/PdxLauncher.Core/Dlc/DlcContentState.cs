namespace PdxLauncher.Core.Dlc;

/// <summary>
/// 一条 DLC 的**内容**（资源文件）在磁盘上的来源。
/// <para>
/// 与 <see cref="DlcPatchCoverage"/> 是两个正交的维度：补丁（<c>cream_api.ini</c> + loader）
/// 只决定游戏能不能读到授权，它<b>不会</b>把 DLC 的资源文件放到磁盘上。
/// 实测证据（2026-11-08，Victoria 3 1.15）：<c>appmanifest_529340.acf</c> 的
/// <c>InstalledDepots</c> 只登记了 9 个带 <c>dlcappid</c> 的 depot，其 <c>size</c> 与
/// <c>game\dlc\dlc001…dlc010</c> 的目录字节数逐字节相等；而 <c>dlc011…dlc019</c>
/// 有内容却没有登记 —— 说明前者是 Steam 装的，后者是人工从整合包拷进去的。
/// </para>
/// <para>
/// 所以「补丁打好了但 DLC 打不开/没有界面元素」通常不是补丁的问题，而是这里的
/// <see cref="Missing"/>。启动器只负责如实显示，不负责修补。
/// </para>
/// </summary>
public enum DlcContentState
{
    /// <summary>没有 <c>steam_id</c>（或清单不可用、无从判断）：界面上不显示内容徽标。</summary>
    Unknown,

    /// <summary>Steam 清单里登记了这个 DLC 的 depot：内容由 Steam 安装，随游戏更新一起维护。</summary>
    SteamInstalled,

    /// <summary>
    /// 磁盘上有描述文件（<c>.dlc</c> / <c>.dlc.json</c>），但 Steam 清单里没登记这个 depot：
    /// 内容在，来源是人工拷贝，Steam 不认（校验/更新不会维护它）。
    /// </summary>
    FilesPresent,

    /// <summary>只有 <c>game\dlc_metadata\*.txt</c> 里的元数据（没有落在磁盘上的内容）：打不开。</summary>
    Missing,
}
