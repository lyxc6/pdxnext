namespace PdxLauncher.App.Services;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 把「当前激活播放集」的 mod / DLC 状态写成游戏加载文件（content_load.json / playset_config.json）。
/// <para>
/// 主页切播放集、导入合并进激活播放集这类只改数据库的入口需要它：否则数据库已经切了，
/// 游戏启动仍读旧加载文件，玩家进游戏看不到新 mod / DLC 状态。
/// 写加载文件与写库分开做，不重复落 sqlite。EU5 / CS2 没有加载文件，直接成功跳过。
/// </para>
/// </summary>
public static class ActivePlaySetLoadFileWriter
{
    /// <summary>读取当前激活播放集并原子写加载文件。</summary>
    /// <param name="game">当前游戏。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public static async Task<AppResult<bool>> WriteAsync(
        GameInstallation game,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(game);
        cancellationToken.ThrowIfCancellationRequested();

        if (!game.WritesLoadFile)
        {
            return AppResult<bool>.Ok(true);
        }

        var playSet = await AppServices.PlaySets
            .GetActivePlaySetAsync(game, cancellationToken)
            .ConfigureAwait(false);
        if (playSet is null)
        {
            return AppResult<bool>.Fail(ErrorKind.NotFound, "no-active-playSet");
        }

        var mods = await AppServices.PlaySets
            .GetPlaySetModsAsync(game, playSet.Id, cancellationToken)
            .ConfigureAwait(false);
        var savedDlcs = await AppServices.PlaySets
            .GetPlaySetDlcsAsync(game, playSet.Id, cancellationToken)
            .ConfigureAwait(false);
        var dlcCatalog = await AppServices.Dlcs
            .DiscoverAsync(game, cancellationToken)
            .ConfigureAwait(false);
        var dlcs = PlaySetEditor.MergeDlcs(savedDlcs, dlcCatalog);

        var plan = PlaySetEditor.BuildLoadFileContent(game, mods, dlcs, dlcCatalog);
        await AppServices.LoadFiles
            .WriteAsync(game, plan.Content, cancellationToken)
            .ConfigureAwait(false);

        if (plan.Skipped.Count > 0)
        {
            AppServices.Logger.Warn(
                "加载文件跳过 " + plan.Skipped.Count + " 个 mod（gameId=" + game.GameId + "）："
                + string.Join(", ", plan.Skipped.Select(static m => m.Name + " [" + m.ModId.ToString("N") + "]")));
        }

        return AppResult<bool>.Ok(true);
    }
}
