namespace PdxLauncher.App.Views;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

using PdxLauncher.App.Controls;
using PdxLauncher.App.Localization;
using PdxLauncher.App.Services;
using PdxLauncher.Core.Contracts;
using PdxLauncher.Core.Mods.Sorting;

/// <summary>
/// 「自动排序」预览确认框：把排序前后的差异（谁移到哪、为什么）摊开给用户过目，
/// 确认后才由调用方写库。本框只读展示，不改任何数据。
/// </summary>
internal static class AutoSortPreviewDialog
{
    /// <summary>弹框展示差异；用户点「应用排序」返回 true，取消或浮层未就绪返回 false。</summary>
    public static async Task<bool> ShowAsync(XamlRoot xamlRoot, AutoSortPreview preview, IReadOnlyList<PlaySetMod> oldRows)
    {
        ArgumentNullException.ThrowIfNull(xamlRoot);
        ArgumentNullException.ThrowIfNull(preview);
        ArgumentNullException.ThrowIfNull(oldRows);

        var localizer = Localizer.Instance;
        var diff = AutoSortDiff.Build(preview, oldRows);
        var panel = new StackPanel { Spacing = 12, MinWidth = 420 };

        panel.Children.Add(new TextBlock
        {
            Text = diff.MovedCount == 0
                ? localizer.ModsAutoSortNoChange
                : localizer.T(Keys.ModsAutoSortSummary, diff.Rows.Count, diff.MovedCount),
            TextWrapping = TextWrapping.Wrap,
        });

        foreach (var row in diff.Rows)
        {
            if (!row.Moved)
            {
                continue;
            }

            var block = new StackPanel { Spacing = 2 };
            block.Children.Add(new TextBlock
            {
                Text = row.Name + "：" + localizer.T(Keys.ModsAutoSortRowMove, row.OldIndex + 1, row.NewIndex + 1),
                TextWrapping = TextWrapping.Wrap,
            });
            block.Children.Add(new TextBlock
            {
                Text = BuildReasonLine(localizer, row),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
            panel.Children.Add(block);
        }

        if (diff.Warnings.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.ModsAutoSortWarningsTitle,
                TextWrapping = TextWrapping.Wrap,
                Foreground = CautionBrush(),
            });
            // 目前只有 DependencyCycleBroken 一种警告（成环被断开）。
            foreach (var _ in diff.Warnings)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = localizer.ModsAutoSortWarningCycle,
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        if (diff.Conflicts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.ModsAutoSortConflictsTitle,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var conflict in diff.Conflicts)
            {
                var nameA = NameOf(diff, conflict.ModIdA);
                var nameB = NameOf(diff, conflict.ModIdB);
                panel.Children.Add(new TextBlock
                {
                    Text = conflict.Kind == AutoSortConflictKind.SharedAddedFileAmbiguous
                        ? localizer.T(Keys.ModsAutoSortConflictAmbiguous, nameA, nameB, conflict.SharedPathCount)
                        : localizer.T(Keys.ModsAutoSortConflictSameTier, nameA, nameB, conflict.SharedPathCount),
                    TextWrapping = TextWrapping.Wrap,
                });
            }
        }

        var overlay = ConfirmOverlay.Host;
        if (overlay is null)
        {
            return false;
        }

        return await overlay.ShowAsync(
            localizer.ModsAutoSortPreviewTitle,
            new ScrollViewer { Content = panel, MaxHeight = 420 },
            localizer.ModsAutoSortApply,
            localizer.CommonCancel).ConfigureAwait(true);
    }

    /// <summary>一行理由：层位 + 原版覆盖量 + 补丁标记 + 依赖/共带关系（以全角分号分隔）。</summary>
    private static string BuildReasonLine(Localizer localizer, AutoSortDiffRow row)
    {
        var parts = new List<string> { TierName(localizer, row.Kind) };
        if (row.VanillaOverrideCount > 0)
        {
            parts.Add(localizer.T(Keys.ModsAutoSortReasonOverrides, row.VanillaOverrideCount));
        }

        if (row.IsDerivedPatch)
        {
            parts.Add(localizer.ModsAutoSortReasonPatchName);
        }

        foreach (var reason in row.Reasons)
        {
            var related = reason.RelatedModName ?? "?";
            parts.Add(reason.Kind == AutoSortDiffReasonKind.DerivedPatchAfter
                ? localizer.T(Keys.ModsAutoSortReasonDerivedPatch, related)
                : localizer.T(Keys.ModsAutoSortReasonDeclaredDep, related));
        }

        return string.Join("；", parts);
    }

    /// <summary>层位的显示名。</summary>
    private static string TierName(Localizer localizer, ModKind kind) => kind switch
    {
        ModKind.Overhaul => localizer.ModsAutoSortTierOverhaul,
        ModKind.LargeChange => localizer.ModsAutoSortTierLargeChange,
        ModKind.MediumChange => localizer.ModsAutoSortTierMediumChange,
        ModKind.SmallTweak => localizer.ModsAutoSortTierSmallTweak,
        ModKind.PureAdditive => localizer.ModsAutoSortTierPureAdditive,
        ModKind.GfxOnly => localizer.ModsAutoSortTierGfxOnly,
        ModKind.Localization => localizer.ModsAutoSortTierLocalization,
        _ => kind.ToString(),
    };

    /// <summary>冲突对里取显示名；查不到时退回短 GUID。</summary>
    private static string NameOf(AutoSortDiff diff, Guid modId)
    {
        foreach (var row in diff.Rows)
        {
            if (row.ModId == modId)
            {
                return row.Name;
            }
        }

        return modId.ToString("N");
    }

    /// <summary>次要说明文字色；主题资源缺失时返回 null 用默认色（不硬编码颜色）。</summary>
    private static Brush? SecondaryTextBrush() => TryBrush("TextFillColorSecondaryBrush");

    /// <summary>警示色；主题资源缺失时返回 null 用默认色（不硬编码颜色）。</summary>
    private static Brush? CautionBrush() => TryBrush("SystemFillColorCautionBrush");

    private static Brush? TryBrush(string key)
    {
        if (Application.Current is Application app
            && app.Resources.TryGetValue(key, out var resource)
            && resource is Brush brush)
        {
            return brush;
        }

        return null;
    }
}
