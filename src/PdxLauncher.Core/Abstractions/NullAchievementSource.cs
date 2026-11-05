namespace PdxLauncher.Core.Abstractions;

using PdxLauncher.Core.Contracts;

/// <summary>
/// 空实现：永远返回 <see cref="AchievementAvailability.NotSupported"/>，让页面在没有数据源时
/// 仍能打开并给出明确说明，而不是白屏或异常。
/// <para>
/// S2 接入 Steam 宿主（<c>--steam-host</c> 子进程 + flat C API）后，由真实现替换
/// <c>AppServices.Achievements</c> 的赋值即可，页面与视图模型不用改。
/// 本实现不发起任何 Steam 调用，也不读 <c>appcache/stats</c> 二进制。
/// </para>
/// </summary>
public sealed class NullAchievementSource : IAchievementSource
{
    /// <summary>给界面用的原因键（与 i18n 的 <c>achievements.unavailable.notSupported</c> 对应）。</summary>
    public const string NotSupportedMessageKey = "achievements.unavailable.notSupported";

    /// <inheritdoc />
    public Task<AchievementSnapshot> GetAsync(string gameId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(new AchievementSnapshot
        {
            Availability = AchievementAvailability.NotSupported,
            FetchedAtUtc = DateTimeOffset.UtcNow,
            Message = NotSupportedMessageKey,
        });
    }
}
