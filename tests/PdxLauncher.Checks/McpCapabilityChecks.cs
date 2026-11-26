namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 能力目录与密钥授权的语义自检：写入只认密钥能力，旧作用域要能平滑归一。</summary>
public static class McpCapabilityChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        Console.WriteLine("[McpCapabilityChecks] 密钥能力与旧作用域迁移");

        ChecksContext.Check(
            "能力目录含七个能力",
            McpCapabilityCatalog.All.Count == 7);
        ChecksContext.Check(
            "读取能力覆盖旧版隐式放行的工具",
            McpCapabilityCatalog.ReadOnly.Any(capability => McpCapabilityCatalog.AllowsTool(capability, "list_games"))
            && McpCapabilityCatalog.ReadOnly.Any(capability => McpCapabilityCatalog.AllowsTool(capability, "get_upload_draft"))
            && McpCapabilityCatalog.ReadOnly.Any(capability => McpCapabilityCatalog.AllowsTool(capability, "get_playsets_page"))
            && McpCapabilityCatalog.ReadOnly.Any(capability => McpCapabilityCatalog.AllowsTool(capability, "get_settings_page")));
        ChecksContext.Check(
            "读取能力不含任何写入工具",
            !McpCapabilityCatalog.ReadOnly.Any(capability =>
                McpCapabilityCatalog.AllowsTool(capability, "save_upload_draft")
                || McpCapabilityCatalog.AllowsTool(capability, "clear_upload_draft")
                || McpCapabilityCatalog.AllowsTool(capability, "create_change_plan")));
        ChecksContext.Check(
            "写入能力只放行对应工具",
            McpCapabilityCatalog.AllowsTool(McpCapabilityCatalog.UploadWrite, "save_upload_draft")
            && !McpCapabilityCatalog.AllowsTool(McpCapabilityCatalog.UploadWrite, "create_change_plan"));

        var readOnly = new McpAuthorizedKey("key-read", "只读", McpCapabilityCatalog.ReadOnly);
        var writer = new McpAuthorizedKey(
            "key-write",
            "上传写入",
            [McpCapabilityCatalog.GamesRead, McpCapabilityCatalog.UploadWrite]);
        var playSetPlanner = new McpAuthorizedKey(
            "key-playsets",
            "播放集计划",
            [McpCapabilityCatalog.PlaySetsRead, McpCapabilityCatalog.PlaySetsPlan]);
        var settingsPlanner = new McpAuthorizedKey(
            "key-settings",
            "设置计划",
            [McpCapabilityCatalog.SettingsRead, McpCapabilityCatalog.SettingsPlan]);
        var all = new McpAuthorizedKey("key-all", "全部", McpCapabilityCatalog.All);

        ChecksContext.Check("只读密钥允许读取工具", readOnly.Allows("get_playsets_page"));
        ChecksContext.Check("只读密钥拒绝写入草稿", !readOnly.Allows("save_upload_draft"));
        ChecksContext.Check("只读密钥拒绝计划工具", !readOnly.Allows("create_change_plan", "playsets"));
        ChecksContext.Check("上传写入密钥允许写入草稿", writer.Allows("save_upload_draft"));
        ChecksContext.Check("上传写入密钥不因写权限顺带拿到计划工具", !writer.Allows("create_change_plan", "playsets"));
        ChecksContext.Check("播放集计划密钥允许播放集计划", playSetPlanner.Allows("create_change_plan", "playsets"));
        ChecksContext.Check(
            "播放集计划密钥不能提交设置计划",
            !playSetPlanner.Allows("create_change_plan", "settings"));
        ChecksContext.Check("设置计划密钥允许设置计划", settingsPlanner.Allows("create_change_plan", "settings"));
        ChecksContext.Check(
            "设置计划密钥不能提交播放集计划",
            !settingsPlanner.Allows("create_change_plan", "playsets"));
        ChecksContext.Check("全部能力允许任意工具", all.Allows("save_upload_draft") && all.AllowsAllCapabilities);
        ChecksContext.Check("未知工具默认拒绝", !readOnly.Allows("unknown_tool"));

        ChecksContext.Check(
            "旧 * 归一成全部能力",
            McpCapabilityCatalog.Normalize(["*"]).Count == McpCapabilityCatalog.All.Count);
        ChecksContext.Check(
            "旧作用域归一并补上游戏浏览",
            McpCapabilityCatalog.Normalize(["upload.read", "playsets.write.safe"])
                is var migrated
            && migrated.Contains(McpCapabilityCatalog.GamesRead)
            && migrated.Contains(McpCapabilityCatalog.PlaySetsPlan)
            && !migrated.Contains(McpCapabilityCatalog.UploadWrite));
        ChecksContext.Check(
            "旧设置写作用域映射成设置计划而不是播放集计划",
            McpCapabilityCatalog.Normalize(["settings.write.safe"])
                .Contains(McpCapabilityCatalog.SettingsPlan));
        ChecksContext.Check(
            "旧工具名归一成对应能力",
            McpCapabilityCatalog.Normalize(["get_upload_draft", "save_upload_draft"])
                .Contains(McpCapabilityCatalog.UploadWrite));
        ChecksContext.Check(
            "落库按能力目录排序",
            McpCapabilityCatalog.ToStorage(
                [McpCapabilityCatalog.SettingsPlan, McpCapabilityCatalog.GamesRead])
                == McpCapabilityCatalog.SettingsPlan + "," + McpCapabilityCatalog.GamesRead
            || McpCapabilityCatalog.ToStorage(
                [McpCapabilityCatalog.SettingsPlan, McpCapabilityCatalog.GamesRead])
                == McpCapabilityCatalog.GamesRead + "," + McpCapabilityCatalog.SettingsPlan);

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
