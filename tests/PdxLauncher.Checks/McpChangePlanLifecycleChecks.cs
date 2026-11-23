namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;
using PdxLauncher.Core.Mcp;
using PdxLauncher.Core.Storage;

/// <summary>创建计划后必须进入待审批状态，否则 UI 无法确认。</summary>
public static class McpChangePlanLifecycleChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var mcpTools = ReadSource("src", "PdxLauncher.Mcp", "McpTools.cs");
        var settingsVm = ReadSource("src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs");
        var settingsXaml = ReadSource("src", "PdxLauncher.App", "Views", "SettingsPage.xaml");

        ChecksContext.Check(
            "计划创建后进入 awaiting_ui_approval",
            mcpTools.Contains("McpChangePlanStatus.AwaitingApproval", StringComparison.Ordinal)
                && mcpTools.Contains("\"awaiting_ui_approval\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "UI 审批只接受 awaiting approval",
            settingsVm.Contains("plan.Status != McpChangePlanStatus.AwaitingApproval", StringComparison.Ordinal));
        ChecksContext.Check(
            "无计划时隐藏面板",
            settingsXaml.Contains("HasMcpChangePlans", StringComparison.Ordinal)
                && settingsVm.Contains("HasMcpChangePlans", StringComparison.Ordinal));

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static string ReadSource(params string[] parts)
    {
        var root = FindWorkspaceRoot();
        var path = Path.Combine(new[] { root }.Concat(parts).ToArray());
        return File.Exists(path) ? File.ReadAllText(path) : string.Empty;
    }

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
