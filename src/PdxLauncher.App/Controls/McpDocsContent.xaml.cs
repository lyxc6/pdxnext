namespace PdxLauncher.App.Controls;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;

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
        _isInitialized = true;
    }

    /// <summary>权限文本变化后刷新 XAML 绑定。</summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    private string _exePath = string.Empty;
    private string _dataRoot = string.Empty;
    private McpCreatedAccessKey? _selectedKey;
    private bool _enabled = true;
    private bool _readOnlyHint;
    private bool _isInitialized;

    /// <summary>XAML 里 <c>{x:Bind L.Foo}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>XAML 里显示的“是否可写”状态文本。</summary>
    public string PermissionText { get; private set; } = string.Empty;

    /// <summary>「可用工具」按当前权限过滤后的目录；不再手写工具名。</summary>
    public IReadOnlyList<McpToolDescriptor> VisibleTools =>
        !_enabled
            ? Array.Empty<McpToolDescriptor>()
            : _selectedKey is not null
                ? McpToolCatalog.Tools.Where(tool =>
                    _selectedKey.Key.CapabilityList.Any(capability =>
                        McpCapabilityCatalog.AllowsTool(capability, tool.Name))).ToArray()
                : McpToolCatalog.Tools;

    /// <summary>「共 N 个 / 当前暴露 M 个」的口径以目录为唯一事实源。</summary>
    public string ToolCountText =>
        Localizer.Instance.T(Keys.McpToolCountHint, McpToolCatalog.Tools.Count, VisibleTools.Count);

    /// <summary>密钥列表由设置页在打开 MCP 分节时注入。</summary>
    public IReadOnlyList<McpAccessKey> AccessKeys { get; private set; } = [];

    /// <summary>填入当前程序的 MCP 路径、数据根和自动生成的配置。</summary>
    /// <param name="exePath">当前程序目录下的 PdxLauncher.Mcp.exe。</param>
    /// <param name="dataRoot">启动器数据根。</param>
    /// <param name="enabled">MCP 总开关是否已开启。</param>
    /// <param name="readOnlyHint">是否只有只读能力；用于兼容模式与未选密钥时的说明。</param>
    public void InitializeContent(
        string exePath,
        string dataRoot,
        bool enabled = true,
        bool readOnlyHint = false)
    {
        _exePath = exePath;
        _dataRoot = dataRoot;
        _enabled = enabled;
        _readOnlyHint = readOnlyHint;
        RefreshPermissions(enabled, readOnlyHint);

        ExeMissingText.Text = Localizer.Instance.T(Keys.ModstudioMcpDocsExeMissing, exePath);
        ExeMissingText.Visibility = File.Exists(exePath) ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>刷新可用密钥与客户端配置；rawToken 只来自一次性密钥展示，不落库。</summary>
    public void UpdateConfiguration(
        IReadOnlyList<McpAccessKey> accessKeys,
        McpClientKind client,
        McpCreatedAccessKey? selectedKey,
        bool enabled,
        bool readOnlyHint)
    {
        AccessKeys = accessKeys;
        _selectedKey = selectedKey;
        _enabled = enabled;
        _readOnlyHint = readOnlyHint;
        var key = selectedKey?.Key;
        var raw = selectedKey?.RawToken ?? string.Empty;
        ConfigBox.Text = McpClientConfigGenerator.Create(
            client,
            _exePath,
            _dataRoot,
            enabled,
            key,
            raw);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleTools)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolCountText)));
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
            _readOnlyHint);
    }

    /// <summary>权限开关变化后刷新状态文本与配置.args，不重新探测路径。</summary>
    public void RefreshPermissions(bool enabled, bool readOnlyHint)
    {
        var localizer = Localizer.Instance;
        PermissionText = (enabled, readOnlyHint) switch
        {
            (false, _) => localizer.McpPermissionOff,
            (true, true) => localizer.McpPermissionReadOnly,
            (true, false) => localizer.McpPermissionReadWrite,
        };
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PermissionText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleTools)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ToolCountText)));

        PromptTextBlock.Text = localizer.T(
            Keys.ModstudioMcpQuickInstallPrompt,
            _exePath,
            _dataRoot,
            enabled ? "true" : "false",
            readOnlyHint ? "true" : "false");
        FlowTextBlock.Text = enabled
            ? readOnlyHint
                ? localizer.McpFlowReadOnly
                : localizer.ModstudioMcpFlowBody
            : localizer.McpFlowDisabled;
        ExePathText.Text = _exePath;
        UpdateConfiguration(
            AccessKeys,
            (McpClientKind)Math.Clamp(ClientKindSelector.SelectedIndex, 0, 4),
            _selectedKey,
            enabled,
            readOnlyHint);
    }

    private void OnClientKindChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isInitialized)
        {
            return;
        }

        UpdateConfiguration(
            AccessKeys,
            (McpClientKind)Math.Clamp(ClientKindSelector.SelectedIndex, 0, 4),
            _selectedKey,
            _enabled,
            _readOnlyHint);
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
