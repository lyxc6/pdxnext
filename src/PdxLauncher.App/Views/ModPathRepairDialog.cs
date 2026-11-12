namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.Playsets;

/// <summary>
/// 「修复 mod 路径」确认框。
/// 每一项都能单独取消；改名项给一个可就地编辑的目录名输入框，非 ASCII / 非法字符时禁用主按钮
/// （改了名游戏才认，所以这一步必须让用户看得见自己改成什么）。
/// </summary>
internal static class ModPathRepairDialog
{
    /// <summary>弹框并返回用户确认过的修复项（改名项已带上编辑后的目录名）；取消返回空列表。</summary>
    public static async Task<IReadOnlyList<ModPathRepair>> ShowAsync(XamlRoot xamlRoot, IReadOnlyList<ModPathRepair> repairs)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(repairs);

        if (repairs.Count == 0)
        {
            return Array.Empty<ModPathRepair>();
        }

        var localizer = Localizer.Instance;
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };
        panel.Children.Add(new TextBlock
        {
            Text = localizer.ModsRepairDialogBody,
            TextWrapping = TextWrapping.Wrap,
        });

        var entries = new List<Entry>(repairs.Count);
        foreach (var repair in repairs)
        {
            panel.Children.Add(BuildRow(repair, localizer, entries));
        }

        var error = new TextBlock
        {
            Text = localizer.ModsRepairInvalidName,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCautionBrush"],
            Visibility = Visibility.Collapsed,
        };
        panel.Children.Add(error);

        var overlay = ConfirmOverlay.Host;
        if (overlay is null)
        {
            return Array.Empty<ModPathRepair>();
        }

        // 确认键可用性跟着勾选状态走：没勾任何一项（或改名非法）时不能点「应用」。
        void Refresh()
        {
            var invalid = entries.Any(entry => entry.Include.IsChecked == true && !entry.IsNameValid);
            overlay.IsConfirmEnabled = entries.Any(entry => entry.Include.IsChecked == true) && !invalid;
            error.Visibility = invalid ? Visibility.Visible : Visibility.Collapsed;
        }

        foreach (var entry in entries)
        {
            entry.Include.Checked += (_, _) => Refresh();
            entry.Include.Unchecked += (_, _) => Refresh();
            if (entry.NameBox is not null)
            {
                entry.NameBox.TextChanged += (_, _) =>
                {
                    entry.EditedName = entry.NameBox.Text;
                    Refresh();
                };
            }
        }

        // 修复会重命名目录并改写 .mod 的 path：默认焦点放取消键，Enter 不执行。
        var showTask = overlay.ShowAsync(
            localizer.ModsRepairDialogTitle,
            new ScrollViewer { Content = panel, MaxHeight = 420 },
            localizer.ModsRepairApply,
            localizer.CommonCancel,
            initialFocus: InitialFocus.Cancel);

        Refresh();

        var confirmedResult = await showTask;
        if (!confirmedResult)
        {
            return Array.Empty<ModPathRepair>();
        }

        var confirmed = new List<ModPathRepair>(entries.Count);
        foreach (var entry in entries)
        {
            if (entry.Include.IsChecked != true || !entry.IsNameValid)
            {
                continue;
            }

            confirmed.Add(entry.RenameTo is null
                ? entry.Repair
                : entry.Repair with { SuggestedPath = entry.RenameTo });
        }

        return confirmed;
    }

    private static UIElement BuildRow(ModPathRepair repair, Localizer localizer, List<Entry> entries)
    {
        var stack = new StackPanel { Spacing = 4 };
        var include = new CheckBox
        {
            Content = repair.Name,
            IsChecked = repair.SuggestedPath is not null,
            IsEnabled = repair.SuggestedPath is not null,
        };
        stack.Children.Add(include);

        var hint = repair.Kind == ModPathRepairKind.RenameDirectory
            ? localizer.ModsRepairRenameHint
            : localizer.ModsRepairFillHint;
        stack.Children.Add(new TextBlock
        {
            Text = hint,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });

        stack.Children.Add(new TextBlock
        {
            Text = (repair.CurrentPath ?? "—") + "  →  " + (repair.SuggestedPath ?? "—"),
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            IsTextSelectionEnabled = true,
        });

        TextBox? nameBox = null;
        var entry = new Entry(repair, include);
        if (repair.Kind == ModPathRepairKind.RenameDirectory && repair.SuggestedPath is not null)
        {
            nameBox = new TextBox
            {
                Header = localizer.ModsRepairRenameLabel,
                Text = LeafOf(repair.SuggestedPath),
            };
            entry = new Entry(repair, include) { NameBox = nameBox, EditedName = nameBox.Text };
            stack.Children.Add(nameBox);
        }

        entries.Add(entry);
        return new Border { Padding = new Thickness(0, 4, 0, 4), Child = stack };
    }

    private static string LeafOf(string path)
    {
        var index = path.LastIndexOfAny(new[] { '\\', '/' });
        return index >= 0 && index + 1 < path.Length ? path[(index + 1)..] : path;
    }

    /// <summary>目录名必须非空、不含路径分隔符与非法字符、且全是 ASCII（V3 加载不了的正是非 ASCII 路径）。</summary>
    private static bool IsValidName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..")
        {
            return false;
        }

        if (name.IndexOf('\\') >= 0 || name.IndexOf('/') >= 0 || name.IndexOf(':') >= 0)
        {
            return false;
        }

        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            return false;
        }

        return name.All(ch => ch <= 0x7F);
    }

    private sealed class Entry(ModPathRepair repair, CheckBox include)
    {
        public ModPathRepair Repair { get; } = repair;

        public CheckBox Include { get; } = include;

        public TextBox? NameBox { get; init; }

        /// <summary>输入框里当前的目录名；非改名项恒为 null。</summary>
        public string? EditedName { get; set; }

        /// <summary>改名后的完整建议路径；非改名项为 null。</summary>
        public string? RenameTo
        {
            get
            {
                if (NameBox is null)
                {
                    return null;
                }

                var suggested = Repair.SuggestedPath;
                if (string.IsNullOrWhiteSpace(suggested))
                {
                    return null;
                }

                var index = suggested.LastIndexOfAny(new[] { '\\', '/' });
                var parent = index >= 0 ? suggested[..index] : string.Empty;
                var name = string.IsNullOrWhiteSpace(EditedName) ? LeafOf(suggested) : EditedName.Trim();
                return string.IsNullOrEmpty(parent) ? name : parent + System.IO.Path.DirectorySeparatorChar + name;
            }
        }

        public bool IsNameValid => NameBox is null || IsValidName(string.IsNullOrWhiteSpace(EditedName) ? null : EditedName);
    }
}
