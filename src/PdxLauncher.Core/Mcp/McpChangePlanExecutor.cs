namespace PdxLauncher.Core.Mcp;

using System.Globalization;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;

/// <summary>
/// 由启动器执行的已批准变更计划。仅处理白名单字段，并先用 ExpectedHash 防止覆盖用户后续修改。
/// </summary>
public static class McpChangePlanExecutor
{
    public static async Task<bool> ApplyAsync(
        McpChangePlan plan,
        IGameCatalog gameCatalog,
        IPlaySetRepository playSets,
        AppSettings settings,
        AppSettingsStore settingsStore)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(gameCatalog);
        ArgumentNullException.ThrowIfNull(playSets);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(settingsStore);

        if (plan.Status != McpChangePlanStatus.Approved
            || plan.ApprovedBy != "app"
            || !McpChangePlanPolicy.Validate(ToRequest(plan)).IsValid
            || McpChangePlanPolicy.IsExpired(plan, DateTimeOffset.UtcNow))
        {
            return false;
        }

        if (plan.Domain == "settings" && plan.Operation == "set_setting")
        {
            return ApplySetting(plan, settings, settingsStore);
        }

        if (plan.Domain == "playsets" && plan.Operation == "rename_play_set")
        {
            return await ApplyPlaySetRenameAsync(plan, gameCatalog, playSets).ConfigureAwait(false);
        }

        return false;
    }

    private static bool ApplySetting(McpChangePlan plan, AppSettings settings, AppSettingsStore settingsStore)
    {
        if (!plan.Patch.TryGetValue("key", out var key)
            || !plan.Patch.TryGetValue("value", out var value))
        {
            return false;
        }

        var property = typeof(AppSettings).GetProperty(key);
        if (property is null || !property.CanWrite)
        {
            return false;
        }

        var current = property.GetValue(settings)?.ToString() ?? string.Empty;
        if (!string.Equals(current, plan.ExpectedHash, StringComparison.Ordinal))
        {
            return false;
        }

        object? converted;
        if (property.PropertyType == typeof(bool))
        {
            if (!bool.TryParse(value, out var boolValue))
            {
                return false;
            }

            converted = boolValue;
        }
        else if (property.PropertyType == typeof(string))
        {
            converted = value;
        }
        else
        {
            return false;
        }

        property.SetValue(settings, converted);
        return settingsStore.Save(settings).IsSuccess;
    }

    private static async Task<bool> ApplyPlaySetRenameAsync(McpChangePlan plan, IGameCatalog gameCatalog, IPlaySetRepository playSets)
    {
        if (!Guid.TryParse(plan.TargetId, out var playSetId)
            || !plan.Patch.TryGetValue("name", out var newName)
            || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        var game = await gameCatalog.DiscoverOneAsync(plan.GameId, CancellationToken.None).ConfigureAwait(false);
        if (game is null)
        {
            return false;
        }

        var sets = await playSets.GetPlaySetsAsync(game, CancellationToken.None).ConfigureAwait(false);
        var target = sets.FirstOrDefault(set => set.Id == playSetId);
        if (target is null || !string.Equals(target.Name, plan.ExpectedHash, StringComparison.Ordinal))
        {
            return false;
        }

        return (await playSets.RenamePlaySetAsync(game, playSetId, newName.Trim(), CancellationToken.None).ConfigureAwait(false)).IsSuccess;
    }

    private static McpChangePlanRequest ToRequest(McpChangePlan plan) =>
        new(plan.Domain, plan.Operation, plan.GameId, plan.TargetId, plan.Patch, plan.ExpectedHash);
}
