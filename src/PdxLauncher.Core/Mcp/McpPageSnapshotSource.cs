namespace PdxLauncher.Core.Mcp;

using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Games;

/// <summary>把页面背后的业务数据投影成 MCP 只读快照。</summary>
public static class McpPageSnapshotSource
{
    private static readonly HashSet<string> PublicSettingKeys = new(StringComparer.Ordinal)
    {
        nameof(AppSettings.UiLanguage),
        nameof(AppSettings.PlaySetPageDisplayMode),
        nameof(AppSettings.AlwaysOnTop),
        nameof(AppSettings.BackgroundDisplayMode),
        nameof(AppSettings.ModThumbnailSize),
        nameof(AppSettings.AutoCheckUpdates),
        nameof(AppSettings.TranslateEnabled),
        nameof(AppSettings.TranslateTargetLanguage),
        nameof(AppSettings.TranslateNamesEnabled),
        nameof(AppSettings.TranslateDescriptionEnabled),
        nameof(AppSettings.TranslateChangelogEnabled),
        nameof(AppSettings.TranslateCommentsEnabled),
        nameof(AppSettings.WorkshopPageSize),
    };

    /// <summary>播放集页快照。</summary>
    public static McpPageSnapshot PlaySets(
        IReadOnlyList<GameInstallation> games,
        Func<GameInstallation, IReadOnlyList<PlaySet>> readSets,
        string gameId,
        int offset,
        int limit)
    {
        var game = FindGame(games, gameId);
        if (game is null)
        {
            return McpPageSnapshotPaging.Slice("playsets", "播放集", gameId, [], offset, limit);
        }

        var rows = readSets(game)
            .OrderBy(static set => set.Name, StringComparer.OrdinalIgnoreCase)
            .Select(set => new McpPageRow(
                set.Id.ToString("D"),
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = set.Name,
                    ["isActive"] = set.IsActive ? "true" : "false",
                },
                set.Name))
            .ToArray();
        return McpPageSnapshotPaging.Slice("playsets", "播放集", game.GameId, rows, offset, limit);
    }

    /// <summary>设置页快照；敏感字段不进入输出。</summary>
    public static McpPageSnapshot Settings(AppSettings settings, int offset, int limit)
    {
        var properties = typeof(AppSettings).GetProperties();
        var rows = properties
            .Where(property => PublicSettingKeys.Contains(property.Name))
            .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Select(property => new McpPageRow(
                property.Name,
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["key"] = property.Name,
                    ["value"] = property.GetValue(settings)?.ToString() ?? string.Empty,
                },
                property.GetValue(settings)?.ToString() ?? string.Empty))
            .ToArray();
        return McpPageSnapshotPaging.Slice("settings", "设置", string.Empty, rows, offset, limit);
    }

    private static GameInstallation? FindGame(IReadOnlyList<GameInstallation> games, string gameId) =>
        string.IsNullOrWhiteSpace(gameId)
            ? games.FirstOrDefault()
            : games.FirstOrDefault(game => string.Equals(game.GameId, gameId, StringComparison.OrdinalIgnoreCase));
}
