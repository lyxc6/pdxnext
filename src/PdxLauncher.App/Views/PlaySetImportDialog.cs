namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using Windows.ApplicationModel.DataTransfer;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Playsets.Share;

/// <summary>
/// 导入结果对话框：说明导入到哪个播放集、缺了多少 mod。
/// <para>
/// 有缺失的 Steam 模组且订阅开关可用时，确认键变成「订阅缺失的 Steam 模组（N）」；
/// 否则确认键提供「复制清单」，方便用户把缺失的 id 贴到工坊页手动处理。
/// 订阅只负责提交，Steam 下载完成后还需要再导入同一份文件才会补齐（同名播放集会合并）。
/// </para>
/// </summary>
internal static class PlaySetImportDialog
{
    /// <summary>缺失清单最多显示 60 条，避免对话框被上千个控件拖慢（播放集本身不可虚拟化）。</summary>
    private const int MaxMissingRows = 60;

    /// <summary>展示导入结果；用户选择「订阅缺失的 Steam 模组」时返回 true。</summary>
    public static async Task<bool> ShowAsync(
        XamlRoot xamlRoot,
        PlaySetShareImportResult result,
        bool canSubscribeSteam)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(result);

        var localizer = Localizer.Instance;
        var summary = result.Merged
            ? localizer.T(Keys.ModsImportSummaryMerged, result.PlaySetName, result.AddedMods)
            : localizer.T(Keys.ModsImportSummaryCreated, result.PlaySetName, result.ImportedMods);

        if (result.Missing.Count == 0)
        {
            await ConfirmOverlay
                .ShowMessage(localizer.ModsImportResultTitle, summary, localizer.CommonClose)
                .ConfigureAwait(true);
            return false;
        }

        var steamMissing = result.Missing.Count(static mod => mod.SteamId is { Length: > 0 });
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = summary,
            TextWrapping = TextWrapping.Wrap,
        });
        panel.Children.Add(new TextBlock
        {
            Text = localizer.T(Keys.ModsImportMissingHeader, result.Missing.Count),
            TextWrapping = TextWrapping.Wrap,
        });

        var list = new StackPanel { Spacing = 4 };
        for (var index = 0; index < result.Missing.Count && index < MaxMissingRows; index++)
        {
            list.Children.Add(new TextBlock
            {
                Text = DescribeMissing(result.Missing[index]),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        if (result.Missing.Count > MaxMissingRows)
        {
            list.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.ModsImportMissingMore, result.Missing.Count - MaxMissingRows),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        panel.Children.Add(new ScrollViewer
        {
            Content = list,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        panel.Children.Add(new TextBlock
        {
            Text = localizer.ModsImportMissingHint,
            TextWrapping = TextWrapping.Wrap,
            Foreground = SecondaryTextBrush(),
        });

        if (steamMissing > 0 && !canSubscribeSteam)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.ModsImportSubscribeSettingHint,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        var overlay = ConfirmOverlay.Host;
        if (overlay is null)
        {
            return false;
        }

        if (steamMissing > 0 && canSubscribeSteam)
        {
            return await overlay.ShowAsync(
                localizer.ModsImportResultTitle,
                new ScrollViewer { Content = panel, MaxHeight = 480 },
                localizer.T(Keys.ModsImportSubscribeButton, steamMissing),
                localizer.CommonClose).ConfigureAwait(true);
        }

        await overlay.ShowAsync(
            localizer.ModsImportResultTitle,
            new ScrollViewer { Content = panel, MaxHeight = 480 },
            localizer.ModsImportCopyMissing,
            localizer.CommonClose,
            onConfirm: () => CopyToClipboard(BuildMissingText(result.Missing))).ConfigureAwait(true);
        return false;
    }

    private static string DescribeMissing(PlaySetShareMissingMod mod)
    {
        var id = mod.SteamId ?? mod.PdxId;
        return id is { Length: > 0 } ? mod.DisplayName + "  (" + id + ")" : mod.DisplayName;
    }

    private static string BuildMissingText(IReadOnlyList<PlaySetShareMissingMod> missing)
    {
        var lines = new List<string>(missing.Count);
        foreach (var mod in missing)
        {
            var id = mod.SteamId ?? mod.PdxId;
            lines.Add(id is { Length: > 0 } ? mod.DisplayName + " (" + id + ")" : mod.DisplayName);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static void CopyToClipboard(string text)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(text);
            Clipboard.SetContent(package);
        }
        catch (Exception)
        {
            // 剪贴板被其它进程占用时不影响对话框关闭；用户仍可手工抄屏上的清单。
        }
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
