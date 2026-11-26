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

        if (plan.Domain == "playsets" && plan.Operation == "create_play_set")
        {
            return await ApplyPlaySetCreateAsync(plan, gameCatalog, playSets).ConfigureAwait(false);
        }

        if (plan.Domain == "playsets" && plan.Operation == "set_mod_enabled")
        {
            return await ApplyPlaySetModEnabledAsync(plan, gameCatalog, playSets).ConfigureAwait(false);
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

        // 与 get_settings_page 的 value / expectedHash 必须同源，否则计划永远对不上。
        var current = McpPageValueText.FromSetting(settings, key);
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
        else if (property.PropertyType == typeof(int))
        {
            if (!int.TryParse(
                    value,
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var intValue))
            {
                return false;
            }

            converted = intValue;
        }
        else if (property.PropertyType == typeof(string))
        {
            // 快照把 null 归一成 follow-system 等默认语义；AI 回写这个展示值时必须还原成 null，
            // 不然「跟随系统」会被写成字面量 "follow-system"。
            converted = value switch
            {
                "follow-system" or "follow-ui" => null,
                _ => value,
            };
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

    private static async Task<bool> ApplyPlaySetCreateAsync(
        McpChangePlan plan,
        IGameCatalog gameCatalog,
        IPlaySetRepository playSets)
    {
        if (!plan.Patch.TryGetValue("name", out var newName) || string.IsNullOrWhiteSpace(newName))
        {
            return false;
        }

        // 新建没有「旧值」，expectedHash 约定为空串；非空视为调用方误用，直接拒绝。
        if (!string.IsNullOrEmpty(plan.ExpectedHash))
        {
            return false;
        }

        var game = await gameCatalog.DiscoverOneAsync(plan.GameId, CancellationToken.None).ConfigureAwait(false);
        if (game is null)
        {
            return false;
        }

        return (await playSets.CreatePlaySetAsync(game, newName.Trim(), CancellationToken.None).ConfigureAwait(false)).IsSuccess;
    }

    private static async Task<bool> ApplyPlaySetModEnabledAsync(
        McpChangePlan plan,
        IGameCatalog gameCatalog,
        IPlaySetRepository playSets)
    {
        if (!Guid.TryParse(plan.TargetId, out var playSetId)
            || !plan.Patch.TryGetValue("modId", out var modIdText)
            || !Guid.TryParse(modIdText, out var modId)
            || !plan.Patch.TryGetValue("enabled", out var enabledText)
            || !bool.TryParse(enabledText, out var enabled))
        {
            return false;
        }

        var game = await gameCatalog.DiscoverOneAsync(plan.GameId, CancellationToken.None).ConfigureAwait(false);
        if (game is null)
        {
            return false;
        }

        var mods = await playSets.GetPlaySetModsAsync(game, playSetId, CancellationToken.None).ConfigureAwait(false);
        var target = mods.FirstOrDefault(mod => mod.ModId == modId);
        if (target is null
            || !string.Equals(McpPageValueText.Bool(target.Enabled), plan.ExpectedHash, StringComparison.Ordinal))
        {
            return false;
        }

        var updated = mods
            .Select(mod => mod.ModId == modId ? mod with { Enabled = enabled } : mod)
            .ToArray();
        return (await playSets.SavePlaySetModsAsync(game, playSetId, updated, CancellationToken.None).ConfigureAwait(false)).IsSuccess;
    }

    private static McpChangePlanRequest ToRequest(McpChangePlan plan) =>
        new(plan.Domain, plan.Operation, plan.GameId, plan.TargetId, plan.Patch, plan.ExpectedHash);
}
