using PdxLauncher.Core.Contracts;

namespace PdxLauncher.Core.Abstractions;

/// <summary>主题三层解析：联网缓存 → 安装目录 → 内置占位。</summary>
public interface IThemeResolver
{
    Task<ThemeSpec> ResolveAsync(
        GameInstallation game,
        bool allowOnlineRefresh,
        CancellationToken cancellationToken);

    /// <summary>
    /// 该游戏上次成功联网检查主题的时间（来自 &lt;gameId&gt;.assets.json 的 lastCheckedUtc）；
    /// 从未检查过或索引损坏时返回 null。设置页据此显示「上次检查 / 下次检查」。
    /// </summary>
    DateTimeOffset? GetLastCheckedUtc(string gameId);
}
