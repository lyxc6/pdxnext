namespace PdxLauncher.Checks;

using System.Text.RegularExpressions;
using PdxLauncher.Core.Abstractions;

/// <summary>
/// Mod 库右键「添加到当前播放集」的服务契约与 UI 接线自检。
/// <para>
/// 检查工程按既有约定只链接可脱离 WinUI 的 App 源文件，服务本身由 App 工程编译；
/// 这里逐条钉住服务的分支、串行闸门、写入链与页面/VM/文案接线，断言失败会直接让构建自检变红。
/// </para>
/// </summary>
public static class ModLibraryQuickAddChecks
{
    public static int Run(IFileSystem fs)
    {
        ArgumentNullException.ThrowIfNull(fs);
        var before = ChecksContext.Failures;
        var root = I18nResw.RepoRoot();

        var service = Read(root, "src", "PdxLauncher.App", "Services", "QuickAddToActivePlaySetService.cs");
        var xaml = Read(root, "src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml");
        var code = Read(root, "src", "PdxLauncher.App", "Views", "ModLibraryPage.xaml.cs");
        var vm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModLibraryViewModel.cs");
        var rowVm = Read(root, "src", "PdxLauncher.App", "ViewModels", "ModLibraryRowViewModel.cs");

        RunServiceChecks(service);
        RunUiChecks(xaml, code, vm, rowVm, service);
        RunBilingualChecks(root);

        return ChecksContext.Failures - before;
    }

    private static void RunServiceChecks(string source)
    {
        ChecksContext.Check(
            "快速添加服务: 结果包含成功 / 已存在 / 未登记 / 无游戏 / 无激活集 / 不支持 / 失败",
            source.Contains("Added,", StringComparison.Ordinal)
                && source.Contains("AlreadyInPlaySet,", StringComparison.Ordinal)
                && source.Contains("Unregistered,", StringComparison.Ordinal)
                && source.Contains("NoGame,", StringComparison.Ordinal)
                && source.Contains("NoActivePlaySet,", StringComparison.Ordinal)
                && source.Contains("Unsupported,", StringComparison.Ordinal)
                && source.Contains("SaveFailed,", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加服务: 结果暴露 IsSuccess / IsFailure 供界面反馈",
            source.Contains("public bool IsSuccess", StringComparison.Ordinal)
                && source.Contains("public bool IsFailure", StringComparison.Ordinal));

        var method = SliceMember(source, "public async Task<QuickAddToActivePlaySetResult> AddAsync");
        ChecksContext.Check(
            "快速添加: 无游戏返回 NoGame，不触碰仓储",
            method.IndexOf("game is null", StringComparison.Ordinal) >= 0
                && method.IndexOf("QuickAddToActivePlaySetStatus.NoGame", StringComparison.Ordinal) >= 0
                && method.IndexOf("game is null", StringComparison.Ordinal)
                    < method.IndexOf("_gate.WaitAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 仅启动游戏返回 Unsupported",
            method.Contains("!game.SupportsContentManagement", StringComparison.Ordinal)
                && method.Contains("QuickAddToActivePlaySetStatus.Unsupported", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 未登记 mod 返回 Unregistered 且不写库",
            method.Contains("entry.IsUnregistered", StringComparison.Ordinal)
                && method.Contains("QuickAddToActivePlaySetStatus.Unregistered", StringComparison.Ordinal)
                && method.IndexOf("entry.IsUnregistered", StringComparison.Ordinal)
                    < method.IndexOf("_gate.WaitAsync", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 串行闸门包住读库 → 写库，finally 释放",
            method.Contains("await _gate.WaitAsync", StringComparison.Ordinal)
                && method.Contains("finally", StringComparison.Ordinal)
                && method.Contains("_gate.Release()", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 无激活集返回 NoActivePlaySet",
            method.Contains("GetActivePlaySetAsync", StringComparison.Ordinal)
                && method.Contains("active is null", StringComparison.Ordinal)
                && method.Contains("QuickAddToActivePlaySetStatus.NoActivePlaySet", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 重复 ModId 返回 AlreadyInPlaySet",
            method.Contains("GetPlaySetModsAsync", StringComparison.Ordinal)
                && method.Contains("mod.ModId == entry.ModId", StringComparison.Ordinal)
                && method.Contains("QuickAddToActivePlaySetStatus.AlreadyInPlaySet", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 先按 Position 排序再追加到末尾并重排",
            method.Contains("OrderBy(static mod => mod.Position)", StringComparison.Ordinal)
                && method.Contains("appended.Add(ToPlaySetMod(entry, appended.Count))", StringComparison.Ordinal)
                && method.Contains("PlaySetEditor.Renumber(appended)", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 必要 DLC 合并与 SaveAsync 写入同一激活集",
            method.Contains("PlaySetEditor.MergeDlcs(savedDlcs, dlcCatalog)", StringComparison.Ordinal)
                && method.Contains(".SaveAsync(game, active.Id, ordered, dlcs, dlcCatalog, cancellationToken)", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 保存 / 写加载文件失败按 AppResult 语义返回 SaveFailed",
            method.Contains("if (!save.IsSuccess)", StringComparison.Ordinal)
                && method.Contains("QuickAddToActivePlaySetStatus.SaveFailed", StringComparison.Ordinal)
                && method.Contains("save.ErrorMessage", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加: 异常收敛为失败，取消继续抛出",
            method.Contains("catch (OperationCanceledException)", StringComparison.Ordinal)
                && method.Contains("throw;", StringComparison.Ordinal)
                && method.Contains("catch (Exception ex)", StringComparison.Ordinal));

        var factory = SliceMember(source, "internal static PlaySetMod ToPlaySetMod");
        ChecksContext.Check(
            "快速添加: 与添加模组页同构造口径，默认启用",
            factory.Contains("entry.ModId", StringComparison.Ordinal)
                && factory.Contains("entry.DirPath ?? string.Empty", StringComparison.Ordinal)
                && factory.Contains("true,", StringComparison.Ordinal)
                && factory.Contains("position", StringComparison.Ordinal));
    }

    private static void RunUiChecks(string xaml, string code, string vm, string rowVm, string service)
    {
        var flyoutStart = xaml.IndexOf("<MenuFlyout Opening=\"OnTranslationMenuOpening\">", StringComparison.Ordinal);
        var flyout = flyoutStart >= 0
            ? xaml[flyoutStart..xaml.IndexOf("</MenuFlyout>", flyoutStart, StringComparison.Ordinal)]
            : string.Empty;
        var firstItemStart = flyout.IndexOf("<MenuFlyoutItem", StringComparison.Ordinal);
        var firstItem = firstItemStart >= 0
            ? flyout[firstItemStart..flyout.IndexOf("/>", firstItemStart, StringComparison.Ordinal)]
            : string.Empty;
        ChecksContext.Check(
            "快速添加 UI: 菜单首项含文案 / Click / AutomationId",
            firstItem.Contains("L.ModlibQuickAddToActivePlaySet", StringComparison.Ordinal)
                && firstItem.Contains("Click=\"OnQuickAddToActivePlaySetClick\"", StringComparison.Ordinal)
                && firstItem.Contains("AutomationProperties.AutomationId=\"menuModLibraryAddToActivePlaySet\"", StringComparison.Ordinal),
            "ModLibraryPage.xaml");
        ChecksContext.Check(
            "快速添加 UI: 保留 Opening 与 PageRefreshGesture",
            xaml.Contains("<MenuFlyout Opening=\"OnTranslationMenuOpening\">", StringComparison.Ordinal)
                && code.Contains("PageRefreshGesture.ShouldRefresh(e.OriginalSource, LayoutRoot)", StringComparison.Ordinal));

        var opening = SliceMember(code, "private void OnTranslationMenuOpening");
        var quickAddCase = opening.IndexOf("case \"menuModLibraryAddToActivePlaySet\"", StringComparison.Ordinal);
        var quickAddBreak = quickAddCase >= 0
            ? opening.IndexOf("break;", quickAddCase, StringComparison.Ordinal)
            : -1;
        var quickAddGate = quickAddCase >= 0 && quickAddBreak > quickAddCase
            ? opening[quickAddCase..quickAddBreak]
            : string.Empty;
        ChecksContext.Check(
            "快速添加 UI: Opening 按页面与行状态门控新项",
            opening.Contains("CanQuickAddToPlaySet", StringComparison.Ordinal)
                && quickAddGate.Contains("IsUnregistered", StringComparison.Ordinal)
                && opening.Contains("default:", StringComparison.Ordinal)
                && opening.Contains("continue;", StringComparison.Ordinal));

        var click = SliceMember(code, "private async void OnQuickAddToActivePlaySetClick");
        ChecksContext.Check(
            "快速添加 UI: Click 取行 DataContext 并调用 VM",
            click.Contains("DataContext: ModLibraryRowViewModel row", StringComparison.Ordinal)
                && click.Contains("_viewModel.QuickAddToActivePlaySetAsync(row)", StringComparison.Ordinal)
                && click.Contains("try", StringComparison.Ordinal)
                && click.Contains("catch", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加 UI: 页面构造注入服务",
            code.Contains("new QuickAddToActivePlaySetService(", StringComparison.Ordinal)
                && code.Contains("AppServices.PlaySets", StringComparison.Ordinal)
                && code.Contains("AppServices.LoadFiles", StringComparison.Ordinal)
                && code.Contains("AppServices.Dlcs", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加 UI: VM 暴露门控、状态文案与就地更新",
            vm.Contains("public bool CanQuickAddToPlaySet", StringComparison.Ordinal)
                && vm.Contains("QuickAddToActivePlaySetAsync", StringComparison.Ordinal)
                && vm.Contains("MarkAddedToPlaySet()", StringComparison.Ordinal)
                && vm.Contains("Keys.ModlibQuickAddAdded", StringComparison.Ordinal)
                && vm.Contains("Keys.ModlibQuickAddFailed", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加 UI: 状态文案覆盖全部结果分支",
            new[]
            {
                "Keys.ModlibQuickAddAdded",
                "Keys.ModlibQuickAddAlreadyInSet",
                "Keys.ModlibQuickAddUnregistered",
                "Keys.ModlibQuickAddNoGame",
                "Keys.ModlibQuickAddNoActivePlaySet",
                "Keys.ModlibQuickAddUnsupported",
                "Keys.ModlibQuickAddFailed",
            }.All(key => vm.Contains(key, StringComparison.Ordinal)));
        ChecksContext.Check(
            "快速添加 UI: 行 VM 就地更新归属且暴露未登记门控",
            rowVm.Contains("public bool IsUnregistered", StringComparison.Ordinal)
                && rowVm.Contains("public void MarkAddedToPlaySet()", StringComparison.Ordinal)
                && rowVm.Contains("nameof(PlaySetText)", StringComparison.Ordinal)
                && rowVm.Contains("partial void OnIsInPlaySetChanged", StringComparison.Ordinal));
        ChecksContext.Check(
            "快速添加服务: 依赖全注入，便于单独构造与自检",
            service.Contains("IPlaySetRepository playSets", StringComparison.Ordinal)
                && service.Contains("ILoadFileWriter loadFiles", StringComparison.Ordinal)
                && service.Contains("IDlcCatalog dlcs", StringComparison.Ordinal)
                && service.Contains("IAppLogger logger", StringComparison.Ordinal));
    }

    private static void RunBilingualChecks(string root)
    {
        var zh = I18nResw.Read(root, I18nResw.SimplifiedChinese);
        var en = I18nResw.Read(root, I18nResw.English);
        var expectedPlaceholders = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ModlibQuickAddToActivePlaySet"] = [],
            ["ModlibQuickAddAdded"] = ["{0}", "{1}"],
            ["ModlibQuickAddAlreadyInSet"] = ["{0}", "{1}"],
            ["ModlibQuickAddUnregistered"] = ["{0}"],
            ["ModlibQuickAddNoGame"] = [],
            ["ModlibQuickAddNoActivePlaySet"] = [],
            ["ModlibQuickAddUnsupported"] = [],
            ["ModlibQuickAddFailed"] = ["{0}"],
        };

        foreach (var (key, expected) in expectedPlaceholders)
        {
            var zhValue = I18nResw.Get(zh, key);
            var enValue = I18nResw.Get(en, key);
            ChecksContext.Check(
                "快速添加文案: " + key + " 两语存在且非空",
                !string.IsNullOrWhiteSpace(zhValue) && !string.IsNullOrWhiteSpace(enValue),
                "Resources.resw");
            if (zhValue is null || enValue is null)
            {
                continue;
            }

            ChecksContext.Check(
                "快速添加文案: " + key + " 占位符两语一致且符合设计",
                Placeholders(zhValue).SequenceEqual(expected, StringComparer.Ordinal)
                    && Placeholders(enValue).SequenceEqual(expected, StringComparer.Ordinal));
        }

        ChecksContext.Check(
            "快速添加文案: 资源名 PascalCase 且无点",
            expectedPlaceholders.Keys.All(static key => Regex.IsMatch(key, @"^[A-Z][A-Za-z0-9]*$")));
    }

    private static IReadOnlyList<string> Placeholders(string value) => Regex
        .Matches(value, @"\{(\d+)\}")
        .Cast<Match>()
        .Select(static match => match.Value)
        .OrderBy(static token => token, StringComparer.Ordinal)
        .ToArray();

    private static string Read(string root, params string[] parts) =>
        File.Exists(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            ? File.ReadAllText(Path.Combine(new[] { root }.Concat(parts).ToArray()))
            : string.Empty;

    private static string SliceMember(string source, string signature)
    {
        var start = source.IndexOf(signature, StringComparison.Ordinal);
        if (start < 0)
        {
            return string.Empty;
        }

        var nextDoc = source.IndexOf("    /// <summary>", start + signature.Length, StringComparison.Ordinal);
        return source[start..(nextDoc > start ? nextDoc : Math.Min(source.Length, start + 5000))];
    }
}
