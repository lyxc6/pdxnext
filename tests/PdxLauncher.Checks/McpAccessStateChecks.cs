namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Mcp;

/// <summary>
/// MCP 访问密钥状态与展示值归一化自检。
/// <para>
/// 这两组规则是「页面看起来能用、实际全开」的根源：证书校验失败不能退化成兼容模式，
/// 设置快照与计划 expectedHash 必须同源。断言钉死它们，避免再次静默回归。
/// </para>
/// </summary>
public static class McpAccessStateChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var authorized = new McpAuthorizedKey("key-1", "Claude", ["list_games"]);
        ChecksContext.Check(
            "没传密钥 = 兼容模式",
            McpAccessStatePolicy.Resolve(accessKeySupplied: false, authorized) == McpAccessState.Legacy);
        ChecksContext.Check(
            "传了有效密钥 = 已授权",
            McpAccessStatePolicy.Resolve(accessKeySupplied: true, authorized) == McpAccessState.Authorized);
        ChecksContext.Check(
            "传了无效密钥 = 拒绝（不能退化成兼容）",
            McpAccessStatePolicy.Resolve(accessKeySupplied: true, authorizedKey: null) == McpAccessState.Rejected);

        var settings = new AppSettings
        {
            UiLanguage = null,
            TranslateTargetLanguage = null,
            BackgroundDisplayMode = null,
            ModThumbnailSize = null,
            PlaySetPageDisplayMode = null,
            AlwaysOnTop = true,
            WorkshopPageSize = 30,
        };
        ChecksContext.Check("UiLanguage null → follow-system", McpPageValueText.FromSetting(settings, nameof(AppSettings.UiLanguage)) == "follow-system");
        ChecksContext.Check("TranslateTargetLanguage null → follow-ui", McpPageValueText.FromSetting(settings, nameof(AppSettings.TranslateTargetLanguage)) == "follow-ui");
        ChecksContext.Check("BackgroundDisplayMode null → cover", McpPageValueText.FromSetting(settings, nameof(AppSettings.BackgroundDisplayMode)) == BackgroundDisplayModes.Cover);
        ChecksContext.Check("ModThumbnailSize null → medium", McpPageValueText.FromSetting(settings, nameof(AppSettings.ModThumbnailSize)) == ModThumbnailSizes.Medium);
        ChecksContext.Check("PlaySetPageDisplayMode null → official", McpPageValueText.FromSetting(settings, nameof(AppSettings.PlaySetPageDisplayMode)) == PlaySetPageDisplayModes.OfficialLauncher);
        ChecksContext.Check("bool 统一小写", McpPageValueText.FromSetting(settings, nameof(AppSettings.AlwaysOnTop)) == "true");
        ChecksContext.Check("int 走不变文化", McpPageValueText.FromSetting(settings, nameof(AppSettings.WorkshopPageSize)) == "30");

        ChecksContext.Check("敏感键不返回值", McpPageValueText.FromSetting(settings, nameof(AppSettings.TranslateApiKey)) == string.Empty);

        var snapshot = McpPageSnapshotSource.Settings(settings, 0, 100);
        ChecksContext.Check(
            "设置快照 13 个白名单字段都有非空展示值",
            snapshot.Rows.Count == McpPageSnapshotSource.PublicSettingKeys.Count
            && snapshot.Rows.All(row => row.Values["value"].Length > 0));
        ChecksContext.Check(
            "快照 expectedHash 与展示值同源",
            snapshot.Rows.All(row => string.Equals(row.Values["value"], row.ExpectedHash, StringComparison.Ordinal)));

        var setSetting = new McpChangePlanRequest(
            "settings",
            "set_setting",
            string.Empty,
            nameof(AppSettings.AlwaysOnTop),
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["key"] = nameof(AppSettings.AlwaysOnTop),
                ["value"] = "false",
            },
            "true");
        ChecksContext.Check("设置域空 gameId 可建计划", McpChangePlanPolicy.Validate(setSetting).IsValid);
        ChecksContext.Check(
            "设置域带游戏 id 被拒绝",
            McpChangePlanPolicy.Validate(setSetting with { GameId = "ck3" }).ErrorCode == "PLAN_GAME_NOT_ALLOWED");

        var createPlaySet = new McpChangePlanRequest(
            "playsets",
            "create_play_set",
            "victoria3",
            "unused",
            new Dictionary<string, string>(StringComparer.Ordinal) { ["name"] = "新集" },
            string.Empty);
        ChecksContext.Check("新建播放集可建计划", McpChangePlanPolicy.Validate(createPlaySet).IsValid);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
