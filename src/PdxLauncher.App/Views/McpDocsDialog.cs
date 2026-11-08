namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

using Windows.ApplicationModel.DataTransfer;

/// <summary>
/// 上传面板里的「AI 助手（MCP）」文档：说明独立 MCP 程序的位置、客户端配置和可用工具。
/// <para>
/// 用 <see cref="ContentDialog"/> 而不是项目的 <c>ConfirmOverlay</c>：这里的正文里有
/// 「复制配置」按钮，需要 Enter 先触发按钮自己，而不是被浮层的确认语义抢走。
/// </para>
/// </summary>
internal static class McpDocsDialog
{
    /// <summary>弹出文档对话框。</summary>
    /// <param name="xamlRoot">当前窗口的 XamlRoot。</param>
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var localizer = Localizer.Instance;
        var exePath = Path.Combine(AppContext.BaseDirectory, "PdxLauncher.Mcp.exe");
        var config = BuildConfig(exePath, AppServices.DataRoot);

        var panel = new StackPanel { Spacing = 12, MaxWidth = 560 };
        panel.Children.Add(Body(localizer.ModstudioMcpDocsIntro));
        panel.Children.Add(Body(localizer.ModstudioMcpDocsStepConfig));
        panel.Children.Add(Body(localizer.ModstudioMcpDocsStepRestart));
        panel.Children.Add(Body(localizer.ModstudioMcpDocsStepAsk));

        panel.Children.Add(Caption(localizer.ModstudioMcpDocsConfigLabel));
        var configBox = new TextBox
        {
            Text = config,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = new FontFamily("Consolas"),
            MinHeight = 160,
        };
        AutomationProperties.SetAutomationId(configBox, "txtModStudioMcpConfig");
        panel.Children.Add(configBox);

        var copyRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var copiedText = Caption(string.Empty);
        var copyButton = new Button { Content = localizer.CommonCopy };
        AutomationProperties.SetAutomationId(copyButton, "btnModStudioMcpCopyConfig");
        copyButton.Click += (_, _) =>
        {
            CopyToClipboard(config);
            copiedText.Text = localizer.ModstudioMcpDocsCopied;
        };
        copyRow.Children.Add(copyButton);
        copyRow.Children.Add(copiedText);
        panel.Children.Add(copyRow);

        if (!File.Exists(exePath))
        {
            panel.Children.Add(Body(localizer.T(Keys.ModstudioMcpDocsExeMissing, exePath)));
        }

        panel.Children.Add(Caption(localizer.ModstudioMcpDocsToolsLabel));
        panel.Children.Add(Body(localizer.ModstudioMcpDocsToolsBody));
        panel.Children.Add(Body(localizer.ModstudioMcpDocsSafety));

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localizer.ModstudioMcpDocsTitle,
            Content = new ScrollViewer { Content = panel, MaxHeight = 520 },
            CloseButtonText = localizer.CommonClose,
            DefaultButton = ContentDialogButton.Close,
        };

        await dialog.ShowAsync();
    }

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

        return JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });
    }

    private static void CopyToClipboard(string text)
    {
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static TextBlock Body(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
    };

    private static TextBlock Caption(string text) => new()
    {
        Text = text,
        TextWrapping = TextWrapping.Wrap,
        Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
    };
}
