namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Configuration;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mcp;

/// <summary>MCP 跨页只读快照的来源与敏感字段过滤自检。</summary>
public static class McpPageSnapshotSourceChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var settings = new AppSettings
        {
            McpEnabled = true,
            McpDraftWriteEnabled = true,
            TranslateApiKey = "secret",
            ProxyUrl = "http://secret",
            UiLanguage = "zh-CN",
            AlwaysOnTop = true,
        };
        var snapshot = McpPageSnapshotSource.Settings(settings, 0, 100);
        var keys = snapshot.Rows.Select(row => row.Id).ToArray();

        ChecksContext.Check("设置快照包含 UI 语言", keys.Contains(nameof(AppSettings.UiLanguage)));
        ChecksContext.Check("设置快照包含置顶", keys.Contains(nameof(AppSettings.AlwaysOnTop)));
        ChecksContext.Check("设置快照排除 MCP 开关", !keys.Contains(nameof(AppSettings.McpEnabled)));
        ChecksContext.Check("设置快照排除 MCP 写入开关", !keys.Contains(nameof(AppSettings.McpDraftWriteEnabled)));
        ChecksContext.Check("设置快照排除翻译密钥", !keys.Contains(nameof(AppSettings.TranslateApiKey)));
        ChecksContext.Check("设置快照排除代理地址", !keys.Contains(nameof(AppSettings.ProxyUrl)));
        ChecksContext.Check("设置快照值不含密钥正文", !snapshot.Rows.Any(row => row.ExpectedHash.Contains("secret", StringComparison.OrdinalIgnoreCase))
            && !snapshot.Rows.Any(row => row.Values.Values.Any(value => value.Contains("secret", StringComparison.OrdinalIgnoreCase))));

        var game = new GameInstallation(
            "victoria3", "Victoria 3", "1.0", "C:\\game", "C:\\launcher.json",
            LoadFileFormat.DlcLoadV0, "C:\\docs", "C:\\docs\\dlc", "C:\\docs\\mod",
            "C:\\game\\victoria3.exe", [], [], null!, null, null, null, GameSupport.Full);
        var playSets = McpPageSnapshotSource.PlaySets(
            [game],
            _ => [new PlaySet(Guid.NewGuid(), "Alpha", false), new PlaySet(Guid.NewGuid(), "Beta", true)],
            "victoria3",
            0,
            20);
        ChecksContext.Check("播放集快照有 2 行", playSets.Rows.Count == 2);
        ChecksContext.Check("播放集快照按名称排序", playSets.Rows[0].Values["name"] == "Alpha");
        ChecksContext.Check("播放集快照带激活态", playSets.Rows[1].Values["isActive"] == "true");

        return ChecksContext.Failures == before ? 0 : 1;
    }
}
