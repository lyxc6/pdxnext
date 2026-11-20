namespace PdxLauncher.App.Services;

using System.Text;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.ModStudio;
using PdxLauncher.Core.Online;

/// <summary>
/// 「工坊 mod 转本地 mod」的界面侧薄封装：把展示名收敛成合法的 ASCII 目录名，
/// 并统一经 <see cref="AppServices.WorkshopToLocal"/> 调 Core 的固定 API。
/// <para>
/// Core 端会用 <c>_2.._99</c> 递增规避撞名（见 WorkshopToLocalConverter.ChooseFolderName），
/// 这里只需给一个合法且可读的基础名；占位符与错误文案都由调用方按语言取。
/// </para>
/// </summary>
public static class WorkshopToLocalInvoker
{
    /// <summary>
    /// 把任意展示名收敛成满足 <see cref="ModCreator.IsValidFolderName"/> 的名字：
    /// 只保留 <c>[A-Za-z0-9._-]</c>，其余折叠成单个 <c>_</c>，去掉首尾分隔符，
    /// 保证首字符是字母或数字，最长 64 字符；空串兜底 <c>localmod</c>。
    /// </summary>
    public static string SuggestFolderName(string? displayName)
    {
        var raw = (displayName ?? string.Empty).Trim();
        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                builder.Append(c);
            }
            else if (builder.Length > 0 && builder[^1] != '_')
            {
                builder.Append('_');
            }
        }

        var name = builder.ToString().Trim('_', '.', '-');
        if (name.Length == 0)
        {
            name = "localmod";
        }

        if (!char.IsAsciiLetterOrDigit(name[0]))
        {
            name = "m" + name;
        }

        if (name.Length > 64)
        {
            name = name[..64].Trim('_', '.', '-');
            if (name.Length == 0 || !char.IsAsciiLetterOrDigit(name[0]))
            {
                name = "localmod";
            }
        }

        return name;
    }

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
            game, oldModId, SuggestFolderName(displayName), null, cancellationToken).ConfigureAwait(true);

        if (WorkshopSourceChooser.ResolveAppId(game.SteamAppId, game.GameId) is int appId)
        {
            AppServices.WorkshopStates.Invalidate(appId);
        }

        return result;
    }
}
