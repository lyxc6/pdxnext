namespace PdxLauncher.Core.Dlc;

/// <summary>
/// 一条 DLC 与「补丁清单」（<c>dlc-catalog.json</c>，按游戏分节）的关系。
/// <para>
/// 这里的判定回答的是「打补丁能不能让游戏读到这份 DLC 的授权」，不是「现在有没有授权」——
/// 后者由 <see cref="DlcListEntry.Owned"/> 表达。两个维度必须分开：
/// 一条 DLC 完全可能既在补丁清单里、又确实被账号拥有。
/// </para>
/// <para>
/// 关键边界：只有 <see cref="Applied"/> 才能对外说「已打补丁」。
/// Steam 更新覆盖掉 loader 时补丁清单里仍有这条 DLC，但那时它只是「可覆盖」（<see cref="Covered"/>），
/// 页面按补丁态把它显示成「补丁被覆盖」。
/// </para>
/// </summary>
public enum DlcPatchCoverage
{
    /// <summary>补丁清单里没有这条 DLC：打不打补丁都拿不到它的授权。</summary>
    None,

    /// <summary>补丁清单里有，但 loader 当前不在岗（未打补丁 / 被 Steam 覆盖）：打了才生效。</summary>
    Covered,

    /// <summary>补丁清单里有，且 loader 在岗：游戏现在就能读到它的授权。</summary>
    Applied,

    /// <summary>读不到补丁清单（素材缺失或解析失败）：不能下结论，界面上不显示任何徽标。</summary>
    Unknown,
}
