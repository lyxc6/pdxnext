namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 「扫描结果可以显式作废」的能力。
/// <para>
/// <see cref="IModCatalog"/> 与 <see cref="IDlcCatalog"/> 的扫描结果按 TTL 缓存（60 秒），
/// 用户刚装了 DLC / mod 后要立刻看到，就得先让缓存失效。这个接口单独拿出来，
/// 是为了不把缓存细节塞进两个扫描接口本身 —— 只读消费者仍然只依赖
/// <see cref="IModCatalog"/> / <see cref="IDlcCatalog"/>。
/// </para>
/// </summary>
public interface IScanCacheInvalidator
{
    /// <summary>让某款游戏的扫描结果失效；下一次查询会重新遍历目录。</summary>
    /// <param name="game">游戏安装信息。</param>
    void InvalidateCache(GameInstallation game);

    /// <summary>清空全部扫描结果（设置页「清缓存」用）。</summary>
    void ClearCache();
}
