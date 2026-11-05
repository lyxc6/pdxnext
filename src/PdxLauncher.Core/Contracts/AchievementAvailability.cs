namespace PdxLauncher.Core.Contracts;

/// <summary>
/// 成就数据的可用性。页面据此选择说明文案，避免把「没有数据源」「连不上 Steam」
/// 和「这款游戏确实没有成就」混成同一个空白页。
/// </summary>
public enum AchievementAvailability
{
    /// <summary>数据源可用，<see cref="AchievementSnapshot.Entries"/> 是当前账号的真实列表（可能为空）。</summary>
    Available,

    /// <summary>本启动器还没有该游戏的数据源（例如尚未接入 Steam 宿主）。</summary>
    NotSupported,

    /// <summary>数据源存在，但这次没能连上 Steam（子进程失败 / 未运行 / 超时）。</summary>
    SteamUnavailable,

    /// <summary>数据源可用且连接正常，但这款游戏没有成就（或当前账号没有任何成就定义）。</summary>
    Empty,

    /// <summary>
    /// 正在取数（只用于界面过渡，数据源本身不会返回这个值）。
    /// <para>
    /// 放在末尾是为了不改变已有成员的值：契约里 <see cref="AchievementSnapshot"/> 的默认可用性
    /// 是 <see cref="NotSupported"/>，序列化 / 比较都依赖现有数值稳定。
    /// </para>
    /// </summary>
    Loading,
}
