namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.Core.ModStudio;

/// <summary>
/// 「编辑描述符」对话框：就地改 mod 名称、版本、支持版本、标签与简介。
///
/// 只列会写回文件的字段；<c>.mod</c> 里的注释、未知键、<c>path</c>，以及
/// <c>metadata.json</c> 里的 <c>id</c> / <c>relationships</c> 等键都由 Core 的编辑器原样保留。
/// 确认键只在名称非空且不含换行时可用；返回 null = 取消。
/// </summary>
internal static class EditModDescriptorDialog
{
    /// <summary>弹框并返回用户编辑后的字段；取消返回 null。</summary>
    /// <param name="xamlRoot">当前窗口的 XamlRoot。</param>
    /// <param name="descriptor">要编辑的描述符（决定字段与文件路径）。</param>
    public static async Task<ModDescriptorEdit?> ShowAsync(XamlRoot xamlRoot, ModDescriptorInfo descriptor)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(descriptor);

        var overlay = ConfirmOverlay.Host;
        if (overlay is null)
        {
            return null;
        }

        var localizer = Localizer.Instance;
        var isMetadata = descriptor.Family == ModDescriptorFamily.Metadata;

        var panel = new StackPanel { Spacing = 12, MinWidth = 440 };

        panel.Children.Add(new TextBlock
        {
            Text = localizer.ModstudioEditHint,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
        });

        panel.Children.Add(new TextBlock
        {
            Text = descriptor.DescriptorPath ?? string.Empty,
            TextWrapping = TextWrapping.Wrap,
            Style = Application.Current.Resources["CaptionTextBlockStyle"] as Style,
            IsTextSelectionEnabled = true,
        });

        var nameBox = new TextBox
        {
            Header = localizer.ModstudioEditNameLabel,
            Text = descriptor.Name,
        };
        panel.Children.Add(nameBox);

        var versionBox = new TextBox
        {
            Header = localizer.ModstudioEditVersionLabel,
            Text = descriptor.Version,
        };
        panel.Children.Add(versionBox);

        var supportedBox = new TextBox
        {
            Header = localizer.ModstudioEditSupportedLabel,
            Text = descriptor.SupportedVersion,
        };
        panel.Children.Add(supportedBox);

        var tagsBox = new TextBox
        {
            Header = localizer.ModstudioEditTagsLabel,
            Text = string.Join(", ", descriptor.Tags),
        };
        panel.Children.Add(tagsBox);

        TextBox? descriptionBox = null;
        if (isMetadata)
        {
            descriptionBox = new TextBox
            {
                Header = localizer.ModstudioEditDescriptionLabel,
                Text = descriptor.Description ?? string.Empty,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                MinHeight = 80,
            };
            panel.Children.Add(descriptionBox);
        }

        void RefreshConfirm()
        {
            var name = nameBox.Text?.Trim() ?? string.Empty;
            overlay.IsConfirmEnabled = name.Length > 0
                && name.IndexOfAny(['\r', '\n']) < 0;
        }

        nameBox.TextChanged += (_, _) => RefreshConfirm();
        RefreshConfirm();

        var showTask = overlay.ShowAsync(
            localizer.ModstudioEditTitle,
            new ScrollViewer { Content = panel, MaxHeight = 480 },
            localizer.CommonSave,
            localizer.CommonCancel);

        overlay.DispatcherQueue?.TryEnqueue(() => nameBox.Focus(FocusState.Programmatic));

        var confirmed = await showTask;
        if (!confirmed)
        {
            return null;
        }

        var editedName = nameBox.Text?.Trim() ?? string.Empty;
        if (editedName.Length == 0)
        {
            return null;
        }

        var description = isMetadata && !string.IsNullOrWhiteSpace(descriptionBox?.Text)
            ? descriptionBox!.Text.Trim()
            : null;

        return new ModDescriptorEdit(
            editedName,
            versionBox.Text?.Trim() ?? string.Empty,
            supportedBox.Text?.Trim() ?? string.Empty,
            SplitTags(tagsBox.Text),
            description);
    }

    /// <summary>与新建表单同口径：中英文逗号、分号都当分隔符。</summary>
    private static IReadOnlyList<string> SplitTags(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text
            .Split([',', '，', ';', '；'], StringSplitOptions.RemoveEmptyEntries)
            .Select(tag => tag.Trim())
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
