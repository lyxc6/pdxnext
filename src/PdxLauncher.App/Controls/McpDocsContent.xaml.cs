namespace PdxLauncher.App.Controls;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;
using PdxLauncher.Core.Mcp;

using Windows.ApplicationModel.DataTransfer;

/// <summary>
/// MCP 文档正文：一句话安装、三步流程、工具标签和默认收起的手动 JSON 配置。
/// 视觉上按卡片分组，避免把步骤、工具和 JSON 挤成一大段文字。
/// </summary>
public sealed partial class McpDocsContent : UserControl, INotifyPropertyChanged
{
    public McpDocsContent()
    {
        InitializeComponent();
    }

    /// <summary>权限文本变化后刷新 XAML 绑定。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _exePath = string.Empty;
    private string _dataRoot = string.Empty;
    private McpCreatedAccessKey? _selectedKey;
    private bool _enabled = true;
    private bool _draftWriteEnabled;

    /// <summary>XAML 里 <c>{x:Bind L.Foo}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>XAML 里显示的“是否可写”状态文本。</summary>
    public string PermissionText { get; private set; } = string.Empty;

    /// <summary>密钥列表由设置页在打开 MCP 分节时注入。</summary>
    public IReadOnlyList<McpAccessKey> AccessKeys { get; private set; } = [];

    /// <summary>填入当前程序的 MCP 路径、数据根和自动生成的配置。</summary>
    /// <param name="exePath">当前程序目录下的 PdxLauncher.Mcp.exe。</param>
    /// <param name="dataRoot">启动器数据根。</param>
    /// <param name="enabled">MCP 总开关是否已开启。</param>
    /// <param name="draftWriteEnabled">是否允许 AI 写入上传草稿。</param>
    public void InitializeContent(
        string exePath,
        string dataRoot,
        bool enabled = true,
        bool draftWriteEnabled = false)
    {
        _exePath = exePath;
        _dataRoot = dataRoot;
        _enabled = enabled;
        _draftWriteEnabled = draftWriteEnabled;
        RefreshPermissions(enabled, draftWriteEnabled);

        ExeMissingText.Text = Localizer.Instance.T(Keys.ModstudioMcpDocsExeMissing, exePath);
        ExeMissingText.Visibility = File.Exists(exePath) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>刷新可用密钥与客户端配置；rawToken 只来自一次性密钥展示，不落库。</summary>
    public void UpdateConfiguration(
        IReadOnlyList<McpAccessKey> accessKeys,
        McpClientKind client,
        McpCreatedAccessKey? selectedKey,
        bool enabled,
        bool draftWriteEnabled)
    {
        AccessKeys = accessKeys;
        _selectedKey = selectedKey;
        _enabled = enabled;
        _draftWriteEnabled = draftWriteEnabled;
        var key = selectedKey?.Key;
        var raw = selectedKey?.RawToken ?? string.Empty;
        ConfigBox.Text = McpClientConfigGenerator.Create(
            client,
            _exePath,
            _dataRoot,
            enabled,
            draftWriteEnabled,
            key,
            raw);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccessKeys)));
    }

    public McpCreatedAccessKey? SelectedKey => _selectedKey;

    /// <summary>让设置页把新建/轮换后的一次性密钥注入配置生成。</summary>
    public void SetSelectedKey(McpCreatedAccessKey? key, IReadOnlyList<McpAccessKey> accessKeys)
    {
        UpdateConfiguration(
            accessKeys,
            (McpClientKind)Math.Clamp(ClientKindSelector.SelectedIndex, 0, 4),
            key,
            _enabled,
            _draftWriteEnabled);
    }

    /// <summary>权限开关变化后刷新状态文本与配置.args，不重新探测路径。</summary>
    public void RefreshPermissions(bool enabled, bool draftWriteEnabled)
    {
        var localizer = Localizer.Instance;
        PermissionText = (enabled, draftWriteEnabled) switch
        {
            (false, _) => localizer.McpPermissionOff,
            (true, false) => localizer.McpPermissionReadOnly,
            (true, true) => localizer.McpPermissionReadWrite,
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PermissionText)));

        PromptTextBlock.Text = localizer.T(
            Keys.ModstudioMcpQuickInstallPrompt,
            _exePath,
            _dataRoot,
            enabled ? "true" : "false",
            draftWriteEnabled ? "true" : "false");
        FlowTextBlock.Text = enabled
            ? draftWriteEnabled
                ? localizer.ModstudioMcpFlowBody
                : localizer.McpFlowReadOnly
            : localizer.McpFlowDisabled;
        ExePathText.Text = _exePath;
        UpdateConfiguration(
            AccessKeys,
            (McpClientKind)Math.Clamp(ClientKindSelector.SelectedIndex, 0, 4),
            _selectedKey,
            enabled,
            draftWriteEnabled);
    }

    private void OnClientKindChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateConfiguration(
            AccessKeys,
            (McpClientKind)Math.Clamp(ClientKindSelector.SelectedIndex, 0, 4),
            _selectedKey,
            _enabled,
            _draftWriteEnabled);
    }

    private void OnCopyPromptClick(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(PromptTextBlock.Text);
        PromptCopiedText.Text = L.ModstudioMcpDocsCopied;
    }

    private void OnShowManualConfigClick(object sender, RoutedEventArgs e)
    {
        ManualConfigExpander.IsExpanded = true;
        ManualConfigExpander.StartBringIntoView();
    }

    private void OnCopyConfigClick(object sender, RoutedEventArgs e)
    {
        CopyToClipboard(ConfigBox.Text);
        ConfigCopiedText.Text = L.ModstudioMcpDocsCopied;
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    /// <summary>
    /// 配置里的路径来自 <see cref="AppContext.BaseDirectory"/>，不是写死的：
    /// dev 构建指向 bin 输出目录，安装版指向安装目录。
    /// 权限参数会写进 args，让 MCP 子进程按设置页授权过滤工具。
    /// </summary>
}
