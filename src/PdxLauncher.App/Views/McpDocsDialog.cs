namespace PdxLauncher.App.Views;

using System;
using System.IO;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;

/// <summary>
/// 上传面板里的「AI 助手（MCP）」文档宿主：只负责路径探测和 ContentDialog 外壳，
/// 具体排版与复制交互在 <see cref="McpDocsContent"/>。
/// </summary>
internal static class McpDocsDialog
{
    /// <summary>弹出文档对话框。</summary>
    /// <param name="xamlRoot">当前窗口的 XamlRoot。</param>
    public static async Task ShowAsync(XamlRoot xamlRoot)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var localizer = Localizer.Instance;

        // 路径不是写死的：始终取当前进程所在目录，dev 是 bin 输出，安装版是安装目录。
        var exePath = Path.Combine(AppContext.BaseDirectory, "PdxLauncher.Mcp.exe");
        var content = new McpDocsContent();
        content.InitializeContent(exePath, AppServices.DataRoot);

        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localizer.ModstudioMcpDocsTitle,
            Content = new ScrollViewer { Content = content, MaxHeight = 680 },
            CloseButtonText = localizer.CommonClose,
            DefaultButton = ContentDialogButton.Close,
            MinWidth = 640,
            MaxWidth = 760,
        };

        // ContentDialog 模板用主题资源限制宽度；只设控件的 MaxWidth 会被模板里的
        // ContentDialogMaxWidth 覆盖，宽卡片仍会被裁掉。
        dialog.Resources["ContentDialogMaxWidth"] = 760d;

        await dialog.ShowAsync();
    }
}
