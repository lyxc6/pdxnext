namespace PdxLauncher.App.Views;

using System;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Playsets.Share;

/// <summary>
/// 导出成功后的说明框：给出保存路径，并明确告诉用户哪些条目<b>没有</b>写进文件
/// （官方规则排除本地 mod；没有 Steam / PDX 平台 id 的条目无法表达）。
/// <para>只有存在被排除/无法表达的条目时才弹，全部导出时只更新状态栏。</para>
/// </summary>
internal static class PlaySetExportNoticeDialog
{
    private const int MaxUnsupportedNames = 10;

    public static async Task ShowAsync(XamlRoot xamlRoot, PlaySetShareExportResult result)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(result);

        var localizer = Localizer.Instance;
        var panel = new StackPanel { Spacing = 8, MinWidth = 400 };
        panel.Children.Add(new TextBlock
        {
            Text = localizer.T(Keys.ModsExportNoticeBody, result.FilePath, result.ExportedMods),
            TextWrapping = TextWrapping.Wrap,
        });

        if (result.LocalExcluded > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.ModsExportNoticeLocalExcluded, result.LocalExcluded),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        if (result.UnsupportedNames.Count > 0)
        {
            var names = string.Join("、", result.UnsupportedNames.Take(MaxUnsupportedNames));
            if (result.UnsupportedNames.Count > MaxUnsupportedNames)
            {
                names += "…";
            }

            panel.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.ModsExportNoticeUnsupported, result.UnsupportedNames.Count, names),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        await ConfirmOverlay
            .ShowMessage(localizer.ModsExportNoticeTitle, panel, localizer.CommonClose)
            .ConfigureAwait(true);
    }

    /// <summary>次要说明文字色；主题资源缺失时返回 null 用默认色（不硬编码颜色）。</summary>
    private static Brush? SecondaryTextBrush()
    {
        if (Application.Current is Application app
            && app.Resources.TryGetValue("TextFillColorSecondaryBrush", out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        return null;
    }
}
