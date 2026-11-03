namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.ModStudio;

/// <summary>
/// 「绑定已有的创意工坊条目」对话框。
///
/// 为什么需要它：在 Steam 客户端（或网页）里发布、或把可见性设成私密/仅好友的条目，
/// 对启动器用的匿名接口一律返回 EResult 9（FileNotFound）——「查不到」不等于「不存在」。
/// 这个框让人把本地 mod 目录与一条工坊 id 绑起来，绑定本身不上传任何文件。
///
/// 语义：确认键只在 id 能解析成正整数时可用；返回 null = 取消。
/// </summary>
internal static class BindWorkshopItemDialog
{
    /// <summary>
    /// 弹框并返回用户填写的创意工坊条目 id；取消返回 null。
    /// </summary>
    /// <param name="xamlRoot">当前窗口的 XamlRoot（浮层挂在窗口级）。</param>
    /// <param name="modName">本地 mod 名，用于说明文案。</param>
    /// <param name="loadMine">
    /// 可选的「从我的 Steam 条目里选」数据源；传 null 则不显示该按钮。
    /// 返回 (条目列表, 失败原因)——列表为空时用失败原因说明为什么没读出来。
    /// </param>
    public static async Task<ulong?> ShowAsync(
        XamlRoot xamlRoot,
        string modName,
        Func<CancellationToken, Task<(IReadOnlyList<(ulong Id, string Title, bool Unlisted)> Items, string? Error)>>? loadMine = null)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);

        var overlay = ConfirmOverlay.Host;
        if (overlay is null)
        {
            return null;
        }

        var localizer = Localizer.Instance;
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };

        panel.Children.Add(new TextBlock
        {
            Text = localizer.T(Keys.ModstudioBindBody, modName),
            TextWrapping = TextWrapping.Wrap,
        });

        panel.Children.Add(new TextBlock
        {
            Text = localizer.ModstudioBindHint,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });

        var idBox = new TextBox
        {
            Header = localizer.ModstudioBindIdLabel,
            PlaceholderText = "3832809800",
        };
        panel.Children.Add(idBox);

        var loadButton = new Button
        {
            Content = localizer.ModstudioBindLoadMine,
            Visibility = loadMine is null ? Visibility.Collapsed : Visibility.Visible,
        };
        panel.Children.Add(loadButton);

        var list = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            MaxHeight = 200,
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(list);

        var status = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed,
            Foreground = (Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
        };
        panel.Children.Add(status);

        var listed = new List<ulong>();

        void ShowStatus(string text)
        {
            status.Text = text;
            status.Visibility = Visibility.Visible;
        }

        void RefreshConfirm()
        {
            overlay.IsConfirmEnabled = TryReadId(idBox.Text, out _);
        }

        idBox.TextChanged += (_, _) => RefreshConfirm();

        // 选中一行就把它填进输入框：用户既能手打 id，也能从枚举结果里挑。
        list.SelectionChanged += (_, _) =>
        {
            var index = list.SelectedIndex;
            if (index >= 0 && index < listed.Count)
            {
                idBox.Text = listed[index].ToString(CultureInfo.InvariantCulture);
            }
        };

        loadButton.Click += async (_, _) =>
        {
            if (loadMine is null)
            {
                return;
            }

            loadButton.IsEnabled = false;
            status.Visibility = Visibility.Collapsed;
            try
            {
                var (items, error) = await loadMine(CancellationToken.None);
                list.Items.Clear();
                listed.Clear();

                foreach (var item in items)
                {
                    listed.Add(item.Id);
                    var row = item.Id.ToString(CultureInfo.InvariantCulture);
                    if (item.Unlisted)
                    {
                        row += " · " + localizer.ModstudioVisibilityUnlisted;
                    }

                    list.Items.Add(row);
                }

                if (items.Count == 0)
                {
                    list.Visibility = Visibility.Collapsed;
                    ShowStatus(localizer.T(Keys.ModstudioBindMyItemsEmpty, error ?? localizer.CommonUnknown));
                }
                else
                {
                    list.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                list.Visibility = Visibility.Collapsed;
                ShowStatus(localizer.T(Keys.ModstudioBindMyItemsEmpty, ex.Message));
            }
            finally
            {
                loadButton.IsEnabled = true;
            }
        };

        RefreshConfirm();

        var showTask = overlay.ShowAsync(
            localizer.ModstudioBindTitle,
            new ScrollViewer { Content = panel, MaxHeight = 460 },
            localizer.ModstudioBindExisting,
            localizer.CommonCancel);

        // 浮层自己会把焦点交给确认键；这里再补一次，让用户打开就能直接输入 id。
        overlay.DispatcherQueue?.TryEnqueue(() => idBox.Focus(FocusState.Programmatic));

        var confirmed = await showTask;
        return confirmed && TryReadId(idBox.Text, out var id) ? id : null;
    }

    /// <summary>id 必须是正整数：0 与非数字一律按「还没填」处理。</summary>
    private static bool TryReadId(string? text, out ulong id)
    {
        if (ModIdInput.TryParse(text, out var parsed) && parsed is > 0)
        {
            id = parsed.Value;
            return true;
        }

        id = 0;
        return false;
    }
}
