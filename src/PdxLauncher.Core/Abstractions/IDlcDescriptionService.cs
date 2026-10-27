namespace PdxLauncher.Core.Abstractions;

/// <summary>
/// DLC 简介来源。CK3/Stellaris 的 .dlc 里没有简介（V3 的 .dlc.json 才带 description.en），
/// 因此缺简介的条目由本服务按 Steam appid 向 Steam 商店接口补全，并落盘缓存。
/// </summary>
public interface IDlcDescriptionService
{
    /// <summary>简介缓存文件路径（诊断、缓存卡片与文档用）。</summary>
    string CachePath { get; }

    /// <summary>
    /// 查询一批 DLC 的简介。
    /// <para>返回的字典只包含已知且非空的简介；从未成功获取过（离线、已下架、请求失败）的 steamId 不会出现在结果里。</para>
    /// <para>缓存命中（含已过期但仍有文本的旧值）优先返回；<paramref name="allowOnline"/> 为 false 时绝不联网。</para>
    /// <para>缓存按语言分桶：同一批查询只读写 <paramref name="language"/> 对应的那部分，切换界面语言不会污染其它语言的结果。</para>
    /// </summary>
    /// <param name="steamIds">DLC 的 Steam appid 集合，空白项会被忽略。</param>
    /// <param name="language">Steam 商店语言（<see cref="SteamStoreLanguage"/> 里的值），未知值回退到默认语言。</param>
    /// <param name="cacheDays">简介的正缓存天数（设置项，按 1..365 截断）。</param>
    /// <param name="allowOnline">是否允许联网补全缺失/过期的简介。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>steamId 到简介文本的映射。</returns>
    Task<IReadOnlyDictionary<string, string>> GetDescriptionsAsync(
        IReadOnlyCollection<string> steamIds,
        string language,
        int cacheDays,
        bool allowOnline,
        CancellationToken cancellationToken);

    /// <summary>
    /// 丢弃缓存（磁盘文件 + 内存副本）。用于设置页的「清除缓存」；下一次查询会重新联网获取。
    /// </summary>
    void ClearCache();
}
