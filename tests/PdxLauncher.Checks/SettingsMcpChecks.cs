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
        var editorXaml = Read(root, "src", "PdxLauncher.App", "Views", "McpKeyEditorPage.xaml");
        var editorCode = Read(root, "src", "PdxLauncher.App", "Views", "McpKeyEditorPage.xaml.cs");
        var editorVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "McpKeyEditorViewModel.cs");

        ChecksContext.Check(
            "设置 MCP: 左侧有 MCP 导航（Tag=7）",
            settingsXaml.Contains("AutomationProperties.AutomationId=\"navSettingsMcp\"", StringComparison.Ordinal)
                && settingsXaml.Contains("Tag=\"7\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 内容列有按需加载的 MCP 分节",
            settingsXaml.Contains("x:Name=\"secMcp\"", StringComparison.Ordinal)
                && settingsXaml.Contains("Visibility=\"{x:Bind svc:XamlVisibility.FromBool(IsMcpSection)", StringComparison.Ordinal)
                && settingsXaml.Contains("controls:McpDocsContent", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: Halo 式四个分区齐全",
            settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpOverview\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpConnection\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpToolCatalog\"", StringComparison.Ordinal)
                && settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpRecentCalls\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 工具目录和调用记录来自 VM",
            settingsXaml.Contains("ViewModel.McpVisibleTools", StringComparison.Ordinal)
                && settingsXaml.Contains("ViewModel.McpRecentCalls", StringComparison.Ordinal)
                && settingsVm.Contains("RefreshMcpCalls", StringComparison.Ordinal)
                && settingsVm.Contains("McpCallHistoryStore", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 密钥列表与子页入口齐全",
            settingsXaml.Contains("AutomationProperties.AutomationId=\"expMcpAccessKeys\"", StringComparison.Ordinal)
                && settingsXaml.Contains("Click=\"OnNewMcpKeyClick\"", StringComparison.Ordinal)
                && settingsXaml.Contains("Click=\"OnEditMcpKeyClick\"", StringComparison.Ordinal)
                && !settingsXaml.Contains("McpKeyScopeBox", StringComparison.Ordinal)
                && !settingsXaml.Contains("tglMcpDraftWrite", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 密钥子页按能力勾选",
            File.Exists(Path.Combine(root, "src", "PdxLauncher.App", "Views", "McpKeyEditorPage.xaml"))
                && File.Exists(Path.Combine(root, "src", "PdxLauncher.App", "Views", "McpKeyEditorPage.xaml.cs"))
                && Read(root, "src", "PdxLauncher.App", "Views", "McpKeyEditorPage.xaml")
                    .Contains("chkMcpCapUploadWrite", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 新建后先留在页内展示一次性密钥",
            editorCode.Contains("_createdTokenToReturn", StringComparison.Ordinal)
                && editorCode.Contains("if (_viewModel.HasCreatedToken)", StringComparison.Ordinal)
                && editorCode.Contains("RefreshFooter()", StringComparison.Ordinal)
                && editorVm.Contains("public string CreatedToken", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 删除密钥要二次确认",
            editorCode.Contains("ConfirmOverlay.ConfirmAsync(", StringComparison.Ordinal)
                && editorCode.Contains("SettingsMcpKeyDeleteContent", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 文档按当前路径与权限初始化",
            settingsCode.Contains("InitializeContent(", StringComparison.Ordinal)
                && settingsCode.Contains("PdxLauncher.Mcp.exe", StringComparison.Ordinal)
                && settingsCode.Contains("AppServices.DataRoot", StringComparison.Ordinal)
                && settingsCode.Contains("RefreshPermissions(", StringComparison.Ordinal));

        ChecksContext.Check(
            "设置 MCP: 只有服务总开关落盘",
            settingsVm.Contains("public bool McpEnabled", StringComparison.Ordinal)
                && !settingsVm.Contains("public bool McpDraftWriteEnabled", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 全局写入开关已退役（只留兼容字段）",
            appSettings.Contains("public bool McpEnabled { get; set; } = true", StringComparison.Ordinal)
                && appSettings.Contains("[Obsolete(", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 权限文本和配置跟随开关刷新",
            docsControlXaml.Contains("x:Bind PermissionText", StringComparison.Ordinal)
                && docsControlCode.Contains("public void RefreshPermissions", StringComparison.Ordinal)
                && settingsVm.Contains("McpClientConfigGenerator.Create", StringComparison.Ordinal));

        ChecksContext.Check(
            "设置 MCP: Mod 创建页不再有 AI 助手按钮和弹窗调用",
            !modStudioXaml.Contains("btnModStudioMcpDocs", StringComparison.Ordinal)
                && !modStudioCode.Contains("OnMcpDocsClick", StringComparison.Ordinal)
                && !modStudioCode.Contains("McpDocsDialog", StringComparison.Ordinal)
                && !File.Exists(Path.Combine(root, "src", "PdxLauncher.App", "Views", "McpDocsDialog.cs")));

        ChecksContext.Check(
            "MCP 权限: 总开关关闭时空工具表、调用被拒",
            mcpServer.Contains("if (!IsConnected())", StringComparison.Ordinal)
                && mcpServer.Contains("new JsonArray()", StringComparison.Ordinal)
                && mcpServer.Contains("MCP 工具已在 PdxNext 设置中关闭", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 工具放行只看密钥能力",
            mcpTools.Contains("private bool IsToolAllowed(string name, JsonObject? arguments = null)", StringComparison.Ordinal)
                && mcpTools.Contains("McpLegacyAuthorization.Instance", StringComparison.Ordinal)
                && !mcpTools.Contains("_draftWriteEnabled", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 命令行接收兼容只读参数并容忍旧写入参数",
            mcpProgram.Contains("--enabled", StringComparison.Ordinal)
                && mcpProgram.Contains("--legacy-read-only", StringComparison.Ordinal)
                && mcpProgram.Contains("SkipLegacyDraftWriteOption", StringComparison.Ordinal)
                && mcpProgram.Contains("TryParseBoolOption", StringComparison.Ordinal));
        ChecksContext.Check(
            "MCP 权限: 自然语言提示词带只读参数",
            docsControlCode.Contains("Keys.ModstudioMcpQuickInstallPrompt", StringComparison.Ordinal)
                && docsControlCode.Contains("readOnlyHint ? \"true\" : \"false\"", StringComparison.Ordinal));

        ChecksContext.Check(
            "设置 MCP: 概览状态来自真实探测而非权限文案",
            settingsXaml.Contains("ViewModel.McpServiceStateText", StringComparison.Ordinal)
                && settingsXaml.Contains("btnRunMcpProbe", StringComparison.Ordinal)
                && settingsVm.Contains("McpConnectionProbe.Run", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 工具数与目录同源并按权限过滤",
            settingsXaml.Contains("ViewModel.McpToolCount", StringComparison.Ordinal)
                && settingsXaml.Contains("ViewModel.McpVisibleTools", StringComparison.Ordinal)
                && settingsVm.Contains("McpToolCatalog.Tools.Count", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 三个筛选都接 SelectionChanged",
            CountOccurrences(settingsXaml, "SelectionChanged=\"OnMcpFilterChanged\"") == 3
                && settingsCode.Contains("OnMcpFilterChanged", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 筛选工具项来自目录",
            settingsXaml.Contains("ViewModel.McpFilterToolOptions", StringComparison.Ordinal)
                && !settingsXaml.Contains("<ComboBoxItem Content=\"list_games\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 最近调用空态使用反向可见性",
            settingsXaml.Contains("FromBoolInverse(ViewModel.HasMcpRecentCalls)", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 变更计划面板走 resw 文案",
            settingsXaml.Contains("L.SettingsMcpChangePlans", StringComparison.Ordinal)
                && !settingsXaml.Contains("Text=\"AI change plans\"", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 文档区工具名来自目录而不是手抄",
            docsControlXaml.Contains("ItemsSource=\"{x:Bind VisibleTools", StringComparison.Ordinal)
                && !docsControlXaml.Contains("<TextBlock Text=\"list_games\"", StringComparison.Ordinal)
                && docsControlCode.Contains("McpToolCatalog.Tools", StringComparison.Ordinal));
        ChecksContext.Check(
            "设置 MCP: 安装说明默认收起",
            docsControlXaml.Contains("expMcpDocs", StringComparison.Ordinal)
                && docsControlXaml.Contains("IsExpanded=\"False\"", StringComparison.Ordinal));

        var zh = I18nResw.Read(root, "zh-CN");
        var en = I18nResw.Read(root, "en-US");
        foreach (var key in new[]
                 {
                     "SettingsNavMcp",
                     "SettingsSectionMcp",
                     "SettingsMcpEnabled",
                     "SettingsMcpKeyOnlyHint",
                     "SettingsMcpCapUploadWrite",
                     "SettingsMcpKeyEdit",
                     "SettingsMcpKeyCreateTitle",
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
