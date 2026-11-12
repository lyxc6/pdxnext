namespace PdxLauncher.App.Controls;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Localization;

using Windows.ApplicationModel.DataTransfer;

/// <summary>
/// MCP 文档正文：一句话安装、三步流程、工具标签和默认收起的手动 JSON 配置。
/// 视觉上按卡片分组，避免把步骤、工具和 JSON 挤成一大段文字。
/// </summary>
public sealed partial class McpDocsContent : UserControl
{
    public McpDocsContent()
    {
        InitializeComponent();
    }

    /// <summary>XAML 里 <c>{x:Bind L.Foo}</c> 的入口。</summary>
    public Localizer L => Localizer.Instance;

    /// <summary>填入当前程序的 MCP 路径、数据根和自动生成的配置。</summary>
    /// <param name="exePath">当前程序目录下的 PdxLauncher.Mcp.exe。</param>
    /// <param name="dataRoot">启动器数据根。</param>
    public void InitializeContent(string exePath, string dataRoot)
    {
        var localizer = Localizer.Instance;
        PromptTextBlock.Text = localizer.T(Keys.ModstudioMcpQuickInstallPrompt, exePath, dataRoot);
        ExePathText.Text = exePath;
        ConfigBox.Text = BuildConfig(exePath, dataRoot);

        ExeMissingText.Text = localizer.T(Keys.ModstudioMcpDocsExeMissing, exePath);
        ExeMissingText.Visibility = File.Exists(exePath) ? Visibility.Collapsed : Visibility.Visible;
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
    /// </summary>
    private static string BuildConfig(string exePath, string dataRoot)
    {
        var config = new
        {
            mcpServers = new Dictionary<string, object>
            {
                ["pdxnext"] = new Dictionary<string, object>
                {
                    ["command"] = exePath,
                    ["args"] = new[] { "--data-root", dataRoot },
                },
            },
        };

        return JsonSerializer.Serialize(
            config,
            new JsonSerializerOptions
            {
                WriteIndented = true,
                // 安装路径里常有中文（例如 G:\Work\paradox启动器）；默认会转成 \uXXXX，
                // 复制到客户端配置里虽然能用，但用户看不懂。这里保留原字符。
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            });
    }
}
