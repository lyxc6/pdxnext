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

        // 数百 mod 的播放集可能有上百行移动：全量塞进 StackPanel 会造出上千个控件，
        // 而对话框本身不可虚拟化。只列前 MaxMovedRows 条，其余折成一行计数。
        const int MaxMovedRows = 60;
        const int MaxConflictRows = 30;

        var movedRows = 0;
        var hiddenMoves = 0;
        foreach (var row in diff.Rows)
        {
            if (!row.Moved)
            {
                continue;
            }

            movedRows++;
            if (movedRows > MaxMovedRows)
            {
                hiddenMoves++;
                continue;
            }

            var block = new StackPanel { Spacing = 4 };
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

        if (hiddenMoves > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.T(Keys.ModsAutoSortTruncated, hiddenMoves),
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryTextBrush(),
            });
        }

        if (diff.Warnings.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.ModsAutoSortWarningsTitle,
                TextWrapping = TextWrapping.Wrap,
                Foreground = CautionBrush(),
            });
            // 警告分三类：成环被断开、原版索引缺失（层位失效）、空足迹（没扫到内容）。
            foreach (var warning in diff.Warnings)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = warning.Kind switch
                    {
                        AutoSortWarningKind.VanillaIndexMissing => localizer.ModsAutoSortWarningVanillaIndex,
                        AutoSortWarningKind.EmptyFootprint =>
                            localizer.T(Keys.ModsAutoSortWarningEmptyFootprint, warning.ModIds.Count),
                        _ => localizer.ModsAutoSortWarningCycle,
                    },
                    TextWrapping = TextWrapping.Wrap,
                });
                if (warning.ModIds.Count > 0)
                {
                    panel.Children.Add(new TextBlock
                    {
                        Text = localizer.T(
                            Keys.ModsAutoSortWarningMods,
                            warning.ModIds.Count,
                            DescribeMods(diff, warning.ModIds)),
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = SecondaryTextBrush(),
                    });
                }

                // 成环时把被违背的边写出来：只报「哪几个 mod 成环」不足以让人决定拆哪条。
                if (warning.Edges is { Count: > 0 } edges)
                {
                    foreach (var edge in edges)
                    {
                        panel.Children.Add(new TextBlock
                        {
                            Text = localizer.T(
                                Keys.ModsAutoSortWarningCycleEdge,
                                NameOf(diff, edge.Before),
                                NameOf(diff, edge.After)),
                            TextWrapping = TextWrapping.Wrap,
                            Foreground = SecondaryTextBrush(),
                        });
                    }
                }
            }
        }

        if (diff.Conflicts.Count > 0)
        {
            panel.Children.Add(new TextBlock
            {
                Text = localizer.ModsAutoSortConflictsTitle,
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var conflict in diff.Conflicts.Take(MaxConflictRows))
            {
                var nameA = NameOf(diff, conflict.ModIdA);
                var nameB = NameOf(diff, conflict.ModIdB);
                panel.Children.Add(new TextBlock
                {
                    Text = conflict.Kind switch
                    {
                        AutoSortConflictKind.SharedAddedFileTierDecided =>
                            localizer.T(Keys.ModsAutoSortConflictTierDecided, nameA, nameB, conflict.SharedPathCount),
                        AutoSortConflictKind.SharedAddedFileAmbiguous =>
                            localizer.T(Keys.ModsAutoSortConflictAmbiguous, nameA, nameB, conflict.SharedPathCount),
                        _ => localizer.T(Keys.ModsAutoSortConflictSameTier, nameA, nameB, conflict.SharedPathCount),
                    },
                    TextWrapping = TextWrapping.Wrap,
                });
            }

            if (diff.Conflicts.Count > MaxConflictRows)
            {
                panel.Children.Add(new TextBlock
                {
                    Text = localizer.T(Keys.ModsAutoSortTruncated, diff.Conflicts.Count - MaxConflictRows),
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = SecondaryTextBrush(),
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
            parts.Add(reason.Kind switch
            {
                AutoSortDiffReasonKind.DerivedPatchAfter => localizer.T(Keys.ModsAutoSortReasonDerivedPatch, related),
                AutoSortDiffReasonKind.FootprintOverlapAfter => localizer.T(Keys.ModsAutoSortReasonFootprintOverlap, related),
                _ => localizer.T(Keys.ModsAutoSortReasonDeclaredDep, related),
            });
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

    /// <summary>
    /// 把告警涉及的一批 mod 拼成「「甲」、「乙」…」。只取前几个：
    /// 环可能很长，这里要的是让人认出是哪一组，不是逐个列完。
    /// </summary>
    private static string DescribeMods(AutoSortDiff diff, IReadOnlyList<Guid> modIds)
    {
        const int MaxNames = 5;
        var names = modIds
            .Take(MaxNames)
            .Select(id => "「" + NameOf(diff, id) + "」")
            .ToList();
        var text = string.Join("、", names);
        return modIds.Count > MaxNames ? text + "…" : text;
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
