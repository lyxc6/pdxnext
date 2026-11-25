namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;

/// <summary>
/// 「工坊 mod 转本地 mod」的界面侧薄封装：把展示名收敛成合法的本地目录名（允许中文），
/// 并统一经 <see cref="AppServices.WorkshopToLocal"/> 调 Core 的固定 API。
/// <para>
/// Core 端会用 <c>_2.._99</c> 递增规避撞名（见 WorkshopToLocalConverter.ChooseFolderName），
/// 这里只需给一个合法且可读的基础名；占位符与错误文案都由调用方按语言取。
/// </para>
/// </summary>
public static class WorkshopToLocalInvoker
{
    /// <summary>
    /// 目录名建议：默认按 <see cref="LocalModFolderName.Normalize"/> 保留原名（中文、词间单空格）；
    /// 只吃 ASCII 路径的游戏（<see cref="GamePathRules.RequiresAsciiPaths"/>）改走
    /// <see cref="LocalModFolderName.NormalizeAscii"/>，用码点标记代替丢字，避免多个中文 mod 又撞回同一个名字。
    /// </summary>
    public static string SuggestFolderName(string? gameId, string? displayName) =>
        GamePathRules.RequiresAsciiPaths(gameId)
            ? LocalModFolderName.NormalizeAscii(displayName)
            : LocalModFolderName.Normalize(displayName);

    /// <summary>不带游戏的重载：按「保留原名」处理（仅用于展示名预览/自检）。</summary>
    public static string SuggestFolderName(string? displayName) => LocalModFolderName.Normalize(displayName);

    /// <summary>
    /// 经 AppServices 注册的实例执行一次转换，返回 Core 的原始结果（文案交给调用方）。
    /// 成功与回滚都失效 WorkshopStates 的整库缓存：转换改写了 mod 来源与路径，
    /// 不清掉 60 秒 TTL 的话返回列表仍显示旧订阅 / 旧路径（与订阅变更同口径）。
    /// </summary>
    public static async Task<AppResult<WorkshopToLocalResult>> ConvertAsync(
        GameInstallation game,
        Guid oldModId,
        string? displayName,
        CancellationToken cancellationToken)
    {
        var result = await AppServices.WorkshopToLocal.ConvertToLocalAsync(
            game, oldModId, SuggestFolderName(game.GameId, displayName), null, cancellationToken).ConfigureAwait(true);

        if (WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId) is int appId)
        {
            AppServices.WorkshopStates.Invalidate(appId);
        }

        return result;
    }
}
