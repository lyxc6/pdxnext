using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Online;

namespace PdxLauncher.Core.Abstractions;

/// <summary>主题三层解析：联网缓存 → 安装目录 → 内置占位。</summary>
public interface IThemeResolver
{
    /// <param name="forceRefresh">
    /// true = 设置页「立即检查」：绕过 TTL 与 10 分钟退避，但仍遵守 allowOnlineRefresh=false 的离线约束。
    /// </param>
    Task<ThemeSpec> ResolveAsync(
        GameInstallation game,
        bool allowOnlineRefresh,
        CancellationToken cancellationToken,
        bool forceRefresh = false);

    /// <summary>
    /// 该游戏上次成功联网检查主题的时间（来自 &lt;gameId&gt;.assets.json 的 lastCheckedUtc）；
    /// 从未检查过或索引损坏时返回 null。设置页据此显示「上次检查 / 下次检查」。
    /// </summary>
    DateTimeOffset? GetLastCheckedUtc(string gameId);

    /// <summary>索引里的失败/退避/元数据缺失诊断；损坏或缺失时返回全空值，绝不抛。</summary>
    ThemeCacheDiagnostics GetDiagnostics(string gameId);
}
