namespace PdxLauncher.Checks;

using PdxLauncher.Core.Abstractions;

/// <summary>
/// 设置页 MCP 分节与工具权限自检：入口必须离开 Mod 创建页，权限开关必须真正影响 MCP 子进程。
/// </summary>
public static class SettingsMcpChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;

        var root = FindWorkspaceRoot();
        var settingsXaml = Read(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml");
        var settingsCode = Read(root, "src", "PdxLauncher.App", "Views", "SettingsPage.xaml.cs");
        var settingsVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "SettingsViewModel.cs");
        var modStudioXaml = Read(root, "src", "PdxLauncher.App", "Views", "ModStudioPage.xaml");
        var modStudioCode = Read(root, "src", "PdxLauncher.App", "Views", "ModStudioPage.xaml.cs");
        var docsControlXaml = Read(root, "src", "PdxLauncher.App", "Controls", "McpDocsContent.xaml");
        var docsControlCode = Read(root, "src", "PdxLauncher.App", "Controls", "McpDocsContent.xaml.cs");
        var mcpTools = Read(root, "src", "PdxLauncher.Mcp", "McpTools.cs");
        var mcpServer = Read(root, "src", "PdxLauncher.Mcp", "McpServer.cs");
        var mcpProgram = Read(root, "src", "PdxLauncher.Mcp", "Program.cs");
        var appSettings = Read(root, "src", "PdxLauncher.Core", "Configuration", "AppSettings.cs");

        ChecksContext.Check(
            "设置 MCP: 左侧有 MCP 导航（Tag=8）",
            settingsXaml.Contains("AutomationProperties.AutomationId=\"navSettingsMcp\"", StringComparison.Ordinal)
                && settingsXaml.Contains("Tag=\"8\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 内容列有按需加载的 MCP 分节",
            settingsXaml.Contains("x:Name=\"secMcp\"", StringComparison.Ordinal)
                && settingsXaml.Contains("x:Load=\"{x:Bind IsMcpSection", StringComparison.Ordinal)
                && settingsXaml.Contains("controls:McpDocsContent", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: Halo 式四个分区齐全",
            settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpOverview\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpConnection\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpToolCatalog\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpRecentCalls\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 工具目录和调用记录来自 VM",
            settingsXaml.Contains("ViewModel.McpTools", StringComparison.Ordinal)
                && settingsXaml.Contains("ViewModel.McpRecentCalls", StringComparison.Ordinal)
                && settingsVm.Contains("RefreshMcpCalls", StringComparison.Ordinal)
                && settingsVm.Contains("McpCallHistoryStore", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 分节索引与 XAML 顺序一致",
            settingsCode.Contains("IsMcpSection => _sectionIndex == 8", StringComparison.Ordinal)
                && settingsCode.Contains("McpSectionIndex = 8", StringComparison.Ordinal)
                && settingsCode.Contains("OnPropertyChanged(nameof(IsMcpSection))", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 文档按当前路径与权限初始化",
            settingsCode.Contains("InitializeContent(", StringComparison.Ordinal)
                && settingsCode.Contains("PdxLauncher.Mcp.exe", StringComparison.Ordinal)
                && settingsCode.Contains("AppServices.DataRoot", StringComparison.Ordinal)
                && settingsCode.Contains("RefreshPermissions(", StringComparison.Ordinal));

        ChecksContext.Check(
            "设置 MCP: 两个权限开关都落盘",
            settingsVm.Contains("public bool McpEnabled", StringComparison.Ordinal)
                && settingsVm.Contains("public bool McpDraftWriteEnabled", StringComparison.Ordinal)
                && CountOccurrences(settingsVm, "ApplyFlag(") >= 2);
        ChecksContext.Check(
            "设置 MCP: 设置模型有两个权限字段",
            appSettings.Contains("public bool McpEnabled { get; set; } = true", StringComparison.Ordinal)
                && appSettings.Contains("public bool McpDraftWriteEnabled { get; set; }", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 权限文本和配置跟随开关刷新",
            docsControlXaml.Contains("x:Bind PermissionText", StringComparison.Ordinal)
                && docsControlCode.Contains("public void RefreshPermissions", StringComparison.Ordinal)
                && docsControlCode.Contains("--draft-write", StringComparison.Ordinal));

        ChecksContext.Check(
            "设置 MCP: Mod 创建页不再有 AI 助手按钮和弹窗调用",
            !modStudioXaml.Contains("btnModStudioMcpDocs", StringComparison.Ordinal)
                && !modStudioCode.Contains("OnMcpDocsClick", StringComparison.Ordinal)
                && !modStudioCode.Contains("McpDocsDialog", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(root, "src", "PdxLauncher.App", "Views", "McpDocsDialog.cs")));

        ChecksContext.Check(
            "MCP 权限: 总开关关闭时空工具表、调用被拒",
            mcpServer.Contains("if (!_enabled)", StringComparison.Ordinal)
                && mcpServer.Contains("new JsonArray()", StringComparison.Ordinal)
                && mcpServer.Contains("MCP 工具已在 PdxNext 设置中关闭", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 写入工具按开关过滤",
            mcpTools.Contains("IsToolAllowed(name)", StringComparison.Ordinal)
                && mcpTools.Contains("\"save_upload_draft\" or \"clear_upload_draft\" => _draftWriteEnabled", StringComparison.Ordinal)
                && mcpTools.Contains("if (_draftWriteEnabled)", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 命令行接收开关参数",
            mcpProgram.Contains("--enabled", StringComparison.Ordinal)
                && mcpProgram.Contains("--draft-write", StringComparison.Ordinal)
                && mcpProgram.Contains("TryParseBoolOption", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 自然语言提示词带权限参数",
            docsControlCode.Contains("Keys.ModstudioMcpQuickInstallPrompt", StringComparison.Ordinal)
                && docsControlCode.Contains("draftWriteEnabled ? \"true\" : \"false\"", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[]
                 {
                     "SettingsNavMcp",
                     "SettingsSectionMcp",
                     "SettingsMcpEnabled",
                     "SettingsMcpDraftWrite",
                     "SettingsMcpToolSummary",
                     "McpPermissionReadOnly",
                 })
        {
            ChecksContext.Check("设置 MCP: zh-CN 有 " + key, I18nResw.Get(zh, key) is { Length: > 0 });
            ChecksContext.Check("设置 MCP: en-US 有 " + key, I18nResw.Get(en, key) is { Length: > 0 });
        }

        return ChecksContext.Failures == before ? 0 : 1;
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string Read(string root, params string[] parts) =>
        File.Exists(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            ? File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            : string.Empty;

    private static string FindWorkspaceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdxLauncher.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return Directory.GetCurrentDirectory();
    }
}
